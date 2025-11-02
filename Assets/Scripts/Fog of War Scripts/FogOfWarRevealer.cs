// Assets/Scripts/Fog of War/FogOfWarRevealer.cs
using UnityEngine;

[DisallowMultipleComponent]
public class FogOfWarRevealer : MonoBehaviour
{
    public FogOfWarManager manager;
    public Transform target;                // usually the Player
    [Tooltip("Optional world-space offset added to the target position (e.g., center vs feet).")]
    public Vector3 worldOffset = Vector3.zero;

    [Header("Brush Override")]
    [Tooltip("<= 0 uses manager.brushWorldRadius; set to override.")]
    public float radiusOverride = -1f;

    [Header("Cut Behavior")]
    [Tooltip("Seed a cut immediately once mapping is ready (after AutoCalibrateUV).")]
    public bool seedCutOnCalibration = true;

    [Tooltip("Minimum world distance moved before we add new cut points along the path.")]
    public float minDistanceToPaint = 0.05f;

    private Vector3 _lastWorldPos;
    private bool _haveLast;
    private bool _seededAfterCalibration;

    // Track calibration changes so we can re-seed when mapping updates
    private Vector2 _lastOrigin;
    private Vector2 _lastSize;

    void Reset()
    {
        target = transform;
    }

    void OnEnable()
    {
        _haveLast = false;
        _seededAfterCalibration = false;
        if (manager == null) manager = FogOfWarManager.Instance;
        if (target == null) target = transform;
        if (manager != null)
        {
            _lastOrigin = manager.worldOriginXZ;
            _lastSize = manager.worldSizeXZ;
        }
    }

    void LateUpdate()
    {
        if (manager == null || target == null) return;

        bool calibChanged = (manager.worldOriginXZ != _lastOrigin || manager.worldSizeXZ != _lastSize);
        if (calibChanged)
        {
            _haveLast = false; // reset trail to avoid streaks with new mapping
            _seededAfterCalibration = false;
            _lastOrigin = manager.worldOriginXZ;
            _lastSize = manager.worldSizeXZ;
        }

        // Do nothing until mapping is valid — prevents mirrored artifacts and smearing
        if (!manager.IsCalibrated) return;

        Vector3 pos = target.position + worldOffset;
        float? r = (radiusOverride > 0f) ? (float?)radiusOverride : null;

        if (seedCutOnCalibration && !_seededAfterCalibration)
        {
            manager.CutAtWorldPoint(pos, r);
            _lastWorldPos = pos;
            _haveLast = true;
            _seededAfterCalibration = true;
            return;
        }

        if (!_haveLast)
        {
            manager.CutAtWorldPoint(pos, r);
            _lastWorldPos = pos;
            _haveLast = true;
            return;
        }

        // Only sweep if we moved meaningfully
        if ((pos - _lastWorldPos).sqrMagnitude >= (minDistanceToPaint * minDistanceToPaint))
        {
            manager.CutAlongWorldSegment(_lastWorldPos, pos, r);
            _lastWorldPos = pos;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Visualize the brush radius around the target (XZ circle)
        if (target == null) return;
        var mgr = (Application.isPlaying ? manager : FogOfWarManager.Instance);
        if (mgr == null) return;

        float r = (radiusOverride > 0f) ? radiusOverride : mgr.brushWorldRadius;
        Vector3 center = target.position + worldOffset;
        const int SEG = 48;
        Vector3 prev = center + new Vector3(r, 0, 0);
        Gizmos.color = new Color(0f, 1f, 0.3f, 0.8f);
        for (int i = 1; i <= SEG; i++)
        {
            float ang = i * (Mathf.PI * 2f / SEG);
            Vector3 p = center + new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
#endif
}

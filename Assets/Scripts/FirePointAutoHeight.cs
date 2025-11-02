using UnityEngine;

/// <summary>
/// Keeps a muzzle/firePoint at an appropriate height above the ground.
/// Attach this script to your firePoint transform.
/// </summary>
[ExecuteAlways]
public class FirePointAutoHeight : MonoBehaviour
{
    [Header("Ground Sampling")]
    [Tooltip("Layers considered 'ground' (terrain, environment meshes, etc.).")]
    public LayerMask groundLayers = ~0;

    [Header("Height Settings")]
    [Tooltip("Keep the firePoint at least this far above the ground.")]
    public float heightAboveGround = 1.1f;
    [Tooltip("Optional extra forward offset from the owner (if set).")]
    public float forwardOffset = 0.0f;

    [Header("Owner (optional)")]
    [Tooltip("If set, sample ground under this transform instead of under the firePoint.")]
    public Transform owner;

    [Header("Runtime Maintenance")]
    [Tooltip("If true, re-assert height periodically at runtime.")]
    public bool maintainAtRuntime = true;
    [Tooltip("Seconds between re-checks while playing.")]
    public float recheckInterval = 0.5f;

    private float _nextCheckTime;

    void OnEnable()
    {
        SnapNow();
    }

    void Start()
    {
        SnapNow();
    }

    void Update()
    {
        // Keep editor view correct too
#if UNITY_EDITOR
        if (!Application.isPlaying) { SnapNow(); return; }
#endif

        if (maintainAtRuntime && Time.time >= _nextCheckTime)
        {
            SnapNow();
            _nextCheckTime = Time.time + Mathf.Max(0.05f, recheckInterval);
        }
    }

    private void SnapNow()
    {
        var refT = owner ? owner : transform;
        Vector3 samplePos = refT.position;

        if (owner && forwardOffset != 0f)
            samplePos += owner.forward * forwardOffset;

        float groundY;
        if (TrySampleGround(samplePos, out groundY))
        {
            var p = transform.position;

            // If owner provided and you want the muzzle slightly in front of them:
            if (owner && forwardOffset != 0f)
            {
                p.x = (owner.position + owner.forward * forwardOffset).x;
                p.z = (owner.position + owner.forward * forwardOffset).z;
            }

            p.y = groundY + heightAboveGround;
            transform.position = p;
        }
    }

    private bool TrySampleGround(Vector3 refPos, out float groundY)
    {
        const float up = 2.0f, down = 10.0f;

        // Cast down from above
        Vector3 fromAbove = refPos + Vector3.up * up;
        if (Physics.Raycast(fromAbove, Vector3.down, out RaycastHit hit, up + down, groundLayers, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
            return true;
        }

        // Fallback: small cast up from slightly below
        Vector3 fromBelow = refPos + Vector3.down * 0.25f;
        if (Physics.Raycast(fromBelow, Vector3.up, out hit, up + 0.5f, groundLayers, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
            return true;
        }

        groundY = refPos.y; // fallback: keep current height
        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, new Vector3(transform.position.x, transform.position.y - heightAboveGround, transform.position.z));
        Gizmos.DrawSphere(transform.position, 0.05f);
    }
}

using UnityEngine;
using UnityEngine.AI;
using System.Collections;

/// <summary>
/// Keeps the NavMeshAgent at ground (baseOffset=0) and shifts the visual so the feet
/// sit exactly on the agent base. Use this when the agent position is correct but the mesh isn't.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MeshFeetAlignToAgentBase : MonoBehaviour
{
    [Header("Visual Root")]
    [Tooltip("Root of your model/rig (where the renderers live).")]
    public Transform visualRoot;

    [Header("Optional: explicit feet markers")]
    [Tooltip("If provided (e.g., left/right toe bones), these are used to align instead of renderer bounds.")]
    public Transform[] feetMarkers;

    [Header("Timing")]
    [Tooltip("Defer a few frames so animators/skins update bounds before we align.")]
    public int deferFrames = 1;
    public bool alignOnce = true;         // usually once is enough for pooled enemies
    public bool keepAligned = false;      // re-check each LateUpdate (costs a tiny bit)

    [Header("Tuning")]
    [Tooltip("Lift a tiny bit to avoid z-fighting with the floor.")]
    public float extraPadding = 0.0f;
    public bool log = false;

    private NavMeshAgent agent;
    private bool aligned;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (!visualRoot)
        {
            var r = GetComponentInChildren<Renderer>(true);
            if (r) visualRoot = r.transform;
            else visualRoot = transform; // last resort
        }

        // If you also have AgentAutoBaseOffset, disable it to avoid fighting
        var auto = GetComponent<AgentAutoBaseOffset>();
        if (auto) auto.enabled = false;
    }

    private void OnEnable()
    {
        // We want the bottom of the agent to sit on ground for this approach
        if (agent) agent.baseOffset = 0f;
        StartCoroutine(AlignRoutine());
    }

    private IEnumerator AlignRoutine()
    {
        for (int i = 0; i < Mathf.Max(0, deferFrames); i++) yield return null;
        AlignNow();
    }

    private void LateUpdate()
    {
        if (keepAligned && (!alignOnce || !aligned))
            AlignNow();
    }

    /// <summary>Shifts visualRoot so feet touch agent base (transform.position.y + extraPadding).</summary>
    public void AlignNow()
    {
        if (!visualRoot) return;

        float feetWorldY;
        if (TryGetFeetYFromMarkers(out feetWorldY) || TryGetFeetYFromRenderers(out feetWorldY))
        {
            float targetY = transform.position.y + Mathf.Max(0f, extraPadding);
            float deltaY = feetWorldY - targetY; // how far feet are above target plane
            if (Mathf.Abs(deltaY) > 0.0005f)
            {
                // Move the visual root down/up in world Y by deltaY
                Vector3 world = visualRoot.position;
                world.y -= deltaY;
                visualRoot.position = world;
                if (log) Debug.Log($"{name}: shifted visual by {-deltaY:0.###}m to place feet on agent base.");
            }
            aligned = true;
        }
        else if (log)
        {
            Debug.LogWarning($"{name}: couldn't find feet (no markers/renderers?).");
        }
    }

    private bool TryGetFeetYFromMarkers(out float feetY)
    {
        feetY = 0f;
        if (feetMarkers == null || feetMarkers.Length == 0) return false;

        float minY = float.PositiveInfinity;
        for (int i = 0; i < feetMarkers.Length; i++)
        {
            var t = feetMarkers[i];
            if (!t) continue;
            float y = t.position.y;
            if (y < minY) minY = y;
        }
        if (float.IsNaN(minY) || float.IsInfinity(minY)) return false;
        feetY = minY;
        return true;
    }

    private bool TryGetFeetYFromRenderers(out float feetY)
    {
        feetY = 0f;
        var rends = visualRoot.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return false;

        float minY = float.PositiveInfinity;
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (!r || !r.enabled) continue;
            float y = r.bounds.min.y;      // world-space bottom of this renderer
            if (y < minY) minY = y;
        }
        if (float.IsNaN(minY) || float.IsInfinity(minY)) return false;
        feetY = minY;
        return true;
    }
}

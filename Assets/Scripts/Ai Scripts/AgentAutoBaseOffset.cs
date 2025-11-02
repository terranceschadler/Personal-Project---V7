using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Computes the correct NavMeshAgent.baseOffset so the model's feet sit on the ground
/// even if the prefab's pivot isn't at the feet.
/// Attach this to your enemy prefab (same GameObject as NavMeshAgent).
/// </summary>
[DefaultExecutionOrder(50)]
public class AgentAutoBaseOffset : MonoBehaviour
{
    [Tooltip("Optional: root of the visual (SkinnedMeshRenderer / Renderers). If null, searches children.")]
    public Transform visualRoot;

    [Header("Preferred Sources (optional)")]
    [Tooltip("If present, compute from CharacterController bottom.")]
    public bool preferCharacterController = true;
    [Tooltip("If present and no CharacterController, compute from CapsuleCollider bottom.")]
    public bool preferCapsuleCollider = true;

    [Header("Tuning")]
    [Tooltip("Extra padding (meters) to avoid z-fighting/clipping.")]
    public float extraFootPadding = 0.0f;
    public bool logComputed = false;

    private NavMeshAgent agent;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (!visualRoot)
        {
            // try to find a reasonable visual root that actually has renderers
            var r = GetComponentInChildren<Renderer>(true);
            if (r != null) visualRoot = r.transform;
            else if (transform.childCount > 0) visualRoot = transform;
        }
    }

    private void OnEnable()
    {
        RecomputeNow();
    }

    /// <summary>Recompute offset now (call this after spawning/warping).</summary>
    public void RecomputeNow()
    {
        if (agent == null) return;

        // 1) CharacterController bottom
        if (preferCharacterController)
        {
            var cc = GetComponent<CharacterController>();
            if (cc != null)
            {
                float bottomLocalY = cc.center.y - (cc.height * 0.5f) + cc.skinWidth;
                float offset = Mathf.Max(0f, -bottomLocalY);
                agent.baseOffset = offset + extraFootPadding;
                if (logComputed) Debug.Log(name + ": baseOffset via CharacterController = " + agent.baseOffset.ToString("0.###"));
                return;
            }
        }

        // 2) CapsuleCollider bottom
        if (preferCapsuleCollider)
        {
            var cap = GetComponent<CapsuleCollider>();
            if (cap != null)
            {
                float bottomLocalY = cap.center.y - (cap.height * 0.5f) + cap.radius;
                float offset = Mathf.Max(0f, -bottomLocalY);
                agent.baseOffset = offset + extraFootPadding;
                if (logComputed) Debug.Log(name + ": baseOffset via CapsuleCollider = " + agent.baseOffset.ToString("0.###"));
                return;
            }
        }

        // 3) Renderer bounds (world-space)
        Transform root = visualRoot ? visualRoot : transform;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

        float minY = float.PositiveInfinity;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (!r || !r.enabled) continue;
            if (r.bounds.min.y < minY) minY = r.bounds.min.y;
        }

        // Unity’s older runtime doesn’t have float.IsFinite
        if (float.IsNaN(minY) || float.IsInfinity(minY)) return;

        // Pivot -> feet distance (world)
        float pivotToFeet = transform.position.y - minY;
        float finalOffset = Mathf.Max(0f, pivotToFeet) + extraFootPadding;

        agent.baseOffset = finalOffset;
        if (logComputed) Debug.Log(name + ": baseOffset via Renderer bounds = " + agent.baseOffset.ToString("0.###"));
    }
}

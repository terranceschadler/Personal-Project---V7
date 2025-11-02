using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// Helper for the "first-attack decides order" system:
/// - No spawn-time scaling here anymore.
/// - (Optional) ensure the object has the "Boss" tag.
/// - Logs when the GameManager scales this boss on first engage.
/// - Shows the assigned boss order as a gizmo label in the editor.
[DefaultExecutionOrder(-1000)]
public class BossHealthScaler : MonoBehaviour
{
    [Header("Tagging")]
    [Tooltip("If true, auto-set this GameObject's tag to 'Boss' on Awake if not already.")]
    public bool setBossTagIfMissing = true;

    [Header("Runtime")]
    [Tooltip("Disable (or destroy) this component after Start to avoid overhead.")]
    public bool disableSelfAfterStart = true;

    [Tooltip("If true, destroy this component after Start instead of just disabling it.")]
    public bool destroySelfAfterStart = false;

    [Header("Debug")]
    [Tooltip("Print logs when engagement order is assigned / scaling happens.")]
    public bool showDebugLogs = true;

    private GameManager gm;
    private EnemyController ec;

    private void Awake()
    {
        gm = GameManager.Instance;
        ec = GetComponent<EnemyController>();

        // Ensure this object is recognized as a boss by tag (or you can rely on a BossEnemy component).
        if (setBossTagIfMissing)
        {
            if (!CompareTag("Boss"))
            {
                try { gameObject.tag = "Boss"; } catch { /* tag might not exist */ }
            }
        }

        // NOTE: We intentionally DO NOT scale here anymore.
        // Scaling happens when the boss FIRST TAKES DAMAGE, via GameManager.TryScaleBossOnFirstEngage.
    }

    private void Start()
    {
        // Nothing to do here anymore; all scaling is driven by first damage.
        if (disableSelfAfterStart)
        {
            if (destroySelfAfterStart) Destroy(this);
            else enabled = false;
        }
    }

    /// <summary>
    /// Invoked by GameManager via SendMessage when a boss gets scaled on first engage.
    /// Do NOT modify HP here — the GameManager has already set the scaled HP and preserved %.
    /// </summary>
    public void OnBossHealthScaled(float newMax)
    {
        if (showDebugLogs)
        {
            int order = (gm != null) ? gm.GetBossEngagementOrder(gameObject) : 0;
          //  Debug.Log($"[BossHealthScaler] '{name}' scaled on first engage. Order=#{order}, newMax={newMax:0.#}");
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        // Draw a small label showing the assigned order, if any (editor only).
        if (!Application.isPlaying) return;
        var gmi = GameManager.Instance;
        if (gmi == null) return;

        int order = gmi.GetBossEngagementOrder(gameObject);
        if (order <= 0) return;

        Handles.Label(transform.position + Vector3.up * 2f, $"Boss #{order}");
    }
#endif
}

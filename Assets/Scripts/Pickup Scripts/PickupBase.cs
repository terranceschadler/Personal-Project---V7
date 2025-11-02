using UnityEngine;

[RequireComponent(typeof(Collider))]
public abstract class PickupBase : MonoBehaviour
{
    [Header("Who can collect")]
    [Tooltip("Tags allowed to collect this pickup.")]
    public string[] collectorTags = new[] { "Player", "FriendlyAI" };

    [Header("FX (optional)")]
    public GameObject collectVfx;
    public AudioClip collectSfx;
    [Range(0f, 1f)] public float collectSfxVolume = 0.8f;

    [Header("Presentation")]
    [Tooltip("Spin speed (deg/sec). 0 = no spin.")]
    public float rotateSpeed = 90f;

    [Header("Lifetime / Pooling")]
    [Tooltip("If > 0, auto-despawn after this many seconds if not collected.")]
    public float lifetimeSeconds = 0f;
    [Tooltip("Use SetActive(false) instead of Destroy on collect.")]
    public bool usePooling = false;

    [Header("Ground Snap (optional)")]
    [Tooltip("Snap to ground on enable using these layers.")]
    public bool snapToGroundOnEnable = false;
    public LayerMask groundLayers = ~0;
    public float groundCheckDistance = 5f;

    protected bool collected;

    // ---------- Unity lifecycle ----------
    protected virtual void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    protected virtual void OnEnable()
    {
        collected = false;

        if (snapToGroundOnEnable) SnapToGround();

        if (lifetimeSeconds > 0f)
            Invoke(nameof(Despawn), lifetimeSeconds);
    }

    protected virtual void OnDisable()
    {
        CancelInvoke();
    }

    protected virtual void Update()
    {
        if (rotateSpeed != 0f)
            transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
    }

    protected virtual void OnTriggerEnter(Collider other)
    {
        if (collected || other == null) return;
        if (!IsAllowedCollector(other.tag)) return;

        OnCollected(other.gameObject);
    }

    // ---------- Public collection hooks (for AI SendMessage etc.) ----------
    public void OnCollected(GameObject collector)
    {
        if (collector == null || collected) return;

        bool applied = ApplyEffect(collector);
        // Collect even if effect wasn't applied (e.g., wrong component); adjust if you prefer otherwise
        Collect();
    }

    public void OnCollected(MonoBehaviour collector)
    {
        if (collector == null) return;
        OnCollected(collector.gameObject);
    }

    // ---------- Implemented by subclasses ----------
    /// <summary>Apply the pickup effect to the collector. Return true if applied.</summary>
    protected abstract bool ApplyEffect(GameObject collector);

    // ---------- Helpers ----------
    protected bool IsAllowedCollector(string tagToCheck)
    {
        if (collectorTags == null || collectorTags.Length == 0) return tagToCheck == "Player";
        for (int i = 0; i < collectorTags.Length; i++)
        {
            var t = collectorTags[i];
            if (!string.IsNullOrEmpty(t) && tagToCheck == t) return true;
        }
        return false;
    }

    protected void Collect()
    {
        if (collected) return;
        collected = true;
        CancelInvoke();

        if (collectVfx) Instantiate(collectVfx, transform.position, Quaternion.identity);
        if (collectSfx) AudioSource.PlayClipAtPoint(collectSfx, transform.position, collectSfxVolume);

        Despawn();
    }

    protected void Despawn()
    {
        if (usePooling)
            gameObject.SetActive(false);
        else
            Destroy(gameObject);
    }

    protected void SnapToGround()
    {
        if (Physics.Raycast(transform.position + Vector3.up, Vector3.down,
            out RaycastHit hit, groundCheckDistance, groundLayers, QueryTriggerInteraction.Ignore))
        {
            transform.position = hit.point;
            transform.up = Vector3.up;
        }
    }
}

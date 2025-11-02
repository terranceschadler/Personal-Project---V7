using UnityEngine;

/// <summary>
/// MarrowSpike — simple hazard spawned in a line.
/// - Pops up, deals damage once per victim (on trigger), then optionally persists briefly.
/// - Put a trigger collider on the prefab (e.g., capsule/box) aligned with its spike mesh.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class MarrowSpike : MonoBehaviour
{
    [Header("Ownership")]
    public BoneforgeTitanBoss owner;

    [Header("Damage")]
    public float damage = 16f;
    public float lifeTime = 6f;
    public float riseTime = 0.2f; // optional grow-in animation (scale)

    [Header("One-Hit Logic")]
    [Tooltip("Prevents dealing damage multiple times to the same victim.")]
    public bool oneHitPerVictim = true;

    private float _dieAt;
    private Collider _col;
    private Vector3 _startScale;
    private bool _rising;
    private System.Collections.Generic.HashSet<Transform> _touched = new System.Collections.Generic.HashSet<Transform>();

    private void Awake()
    {
        _col = GetComponent<Collider>();
        _col.isTrigger = true;
        _startScale = transform.localScale;
    }

    private void OnEnable()
    {
        _dieAt = Time.time + lifeTime;
        if (riseTime > 0f)
        {
            _rising = true;
            transform.localScale = new Vector3(_startScale.x, 0.01f, _startScale.z);
        }
    }

    private void Update()
    {
        if (_rising)
        {
            float t = Mathf.Clamp01((lifeTime - (_dieAt - Time.time)) / Mathf.Max(0.0001f, riseTime));
            float y = Mathf.Lerp(0.01f, _startScale.y, t);
            transform.localScale = new Vector3(_startScale.x, y, _startScale.z);
            if (t >= 1f) _rising = false;
        }

        if (Time.time >= _dieAt) Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other) return;

        Transform root = other.transform.root;
        if (root == transform.root) return; // ignore self/team

        if (oneHitPerVictim && _touched.Contains(root)) return;
        _touched.Add(root);

        GameObject victim = other.attachedRigidbody ? other.attachedRigidbody.gameObject : other.gameObject;
        victim.SendMessage("ApplyDamageFrom", new BossEnemy.DamageEnvelope(damage, owner ? owner.gameObject : gameObject), SendMessageOptions.DontRequireReceiver);
        victim.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
    }
}

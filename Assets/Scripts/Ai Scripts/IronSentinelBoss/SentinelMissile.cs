using UnityEngine;

/// <summary>
/// Homing missile used by Iron Sentinel's barrage.
/// - Homes toward a target Transform with configurable turn rate and speed.
/// - On trigger with a valid victim, applies damage via DamageEnvelope.
/// - Self-destructs on lifetime expiry or when hitting world.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SentinelMissile : MonoBehaviour
{
    [Header("Kinetics")]
    public float speed = 16f;
    public float turnRateDegPerSec = 420f;
    public float lifetime = 6f;

    [Header("Damage")]
    public float damage = 18f;
    public float blastRadius = 0f; // set >0 for small splash
    public LayerMask worldMask = ~0;

    [Header("Ownership")]
    public GameObject instigator; // usually the boss

    [Header("VFX/SFX (optional)")]
    public AudioSource sfx;
    public AudioClip explodeSfx;

    private Transform _target;
    private float _dieAt;

    private void OnEnable()
    {
        _dieAt = Time.time + lifetime;
    }

    public void SetTarget(Transform t) => _target = t;

    private void Update()
    {
        if (Time.time >= _dieAt) { Destroy(gameObject); return; }

        Vector3 forward = transform.forward;

        if (_target != null)
        {
            Vector3 toTarget = (_target.position + Vector3.up * 1.0f) - transform.position;
            Vector3 desiredDir = toTarget.normalized;

            // Turn toward target
            Quaternion want = Quaternion.LookRotation(desiredDir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnRateDegPerSec * Time.deltaTime);
            forward = transform.forward;
        }

        // Move
        Vector3 next = transform.position + forward * speed * Time.deltaTime;

        // Collide with world by linecast
        if (Physics.Linecast(transform.position, next, out RaycastHit hit, worldMask, QueryTriggerInteraction.Ignore))
        {
            ImpactAt(hit.point, hit.collider);
            return;
        }

        transform.position = next;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Avoid hitting the owner
        if (instigator != null && other.transform.root == instigator.transform.root) return;

        // World or victim – explode / apply damage
        ImpactAt(transform.position, other);
    }

    private void ImpactAt(Vector3 pos, Collider hitCol)
    {
        if (sfx && explodeSfx) sfx.PlayOneShot(explodeSfx, 0.8f);

        if (blastRadius > 0.01f)
        {
            var cols = Physics.OverlapSphere(pos, blastRadius, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < cols.Length; i++)
            {
                GameObject victim = cols[i].attachedRigidbody ? cols[i].attachedRigidbody.gameObject : cols[i].gameObject;
                if (instigator != null && victim.transform.root == instigator.transform.root) continue;

                victim.SendMessage("ApplyDamageFrom", new BossEnemy.DamageEnvelope(damage, instigator), SendMessageOptions.DontRequireReceiver);
                victim.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
            }
        }
        else if (hitCol != null)
        {
            GameObject victim = hitCol.attachedRigidbody ? hitCol.attachedRigidbody.gameObject : hitCol.gameObject;
            if (!(instigator != null && victim.transform.root == instigator.transform.root))
            {
                victim.SendMessage("ApplyDamageFrom", new BossEnemy.DamageEnvelope(damage, instigator), SendMessageOptions.DontRequireReceiver);
                victim.SendMessage("TakeDamage", damage, SendMessageOptions.DontRequireReceiver);
            }
        }

        Destroy(gameObject);
    }
}

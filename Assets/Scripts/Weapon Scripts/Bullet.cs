using UnityEngine;

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(Rigidbody))]
public class Bullet : MonoBehaviour, IBullet
{
    [Header("Kinetics")]
    public float speed = 20f;
    public float lifetime = 3f;

    [Header("Damage")]
    public float damage = 10f;

    [Header("Ownership (REQUIRED)")]
    [Tooltip("Defaults to Player if not set.")]
    public GameObject owner; // tag = "Player" or "FriendlyAI" or "Enemy"

    // ------------- IBullet implementation -------------
    public void Initialize(GameObject newOwner)
    {
        owner = newOwner;
    }
    // --------------------------------------------------

    void Start()
    {
        Destroy(gameObject, lifetime);

        // Ensure Rigidbody/Collider are set for trigger-based hits
        var rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true; // we're moving via Transform.Translate
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // ? correct for kinematic bodies
        rb.interpolation = RigidbodyInterpolation.None;

        var myCol = GetComponent<Collider>();
        myCol.isTrigger = true;

        // Default owner to Player if not set
        if (owner == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                owner = playerObj;
                Debug.Log($"[Bullet] No owner provided. Defaulting to Player.", this);
            }
            else
            {
                Debug.LogWarning($"[Bullet] No owner found and no Player in scene!", this);
            }
        }

        // Prevent self-collision
        if (owner != null)
        {
            var ownerCols = owner.GetComponentsInChildren<Collider>(true);
            foreach (var col in ownerCols)
                if (col) Physics.IgnoreCollision(myCol, col, true);
        }
    }

    void Update()
    {
        transform.Translate(Vector3.forward * speed * Time.deltaTime, Space.Self);
    }

    private void OnTriggerEnter(Collider other)
    {
        // Ignore hitting shooter/children
        if (owner && other.transform.IsChildOf(owner.transform)) return;

        // 1) Bone plates take priority
        var plate = other.GetComponent<BonePlate>();
        if (plate != null && !plate.IsBroken)
        {
            plate.ApplyDamageFrom(new BossEnemy.DamageEnvelope(damage, owner));
            Destroy(gameObject);
            return;
        }

        // 2) Bosses (use SendMessage so derived classes with `new ApplyDamageFrom` can catch)
        var boss = other.GetComponentInParent<BossEnemy>();
        if (boss)
        {
            boss.gameObject.SendMessage(
                "ApplyDamageFrom",
                (damage, owner),
                SendMessageOptions.DontRequireReceiver
            );
            Destroy(gameObject);
            return;
        }

        // 3) Regular enemies (non-boss)
        var enemy = other.GetComponentInParent<EnemyController>();
        if (enemy)
        {
            enemy.TakeDamage(damage);
            Destroy(gameObject);
            return;
        }

        // 4) Friendlies / Player (consume without damage)
        if (other.CompareTag("FriendlyAI") || other.CompareTag("Player"))
        {
            Destroy(gameObject);
            return;
        }

        // 5) Hit solid world -> consume
        if (!other.isTrigger) Destroy(gameObject);
    }
}

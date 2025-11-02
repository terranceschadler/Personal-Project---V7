using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ExplodingBullet : MonoBehaviour, IBullet
{
    [Header("Kinetics")]
    public float speed = 20f;
    public float lifetime = 3f;

    [Header("Direct-Hit Damage")]
    public float damage = 10f;

    [Header("Explosion Ring Settings")]
    [Tooltip("Prefab for the spawned shards. Must have your Bullet script attached.")]
    public GameObject shardBulletPrefab;

    [Tooltip("How many shards to spawn in the ring.")]
    public int shardCount = 12;

    [Tooltip("Speed for each shard bullet.")]
    public float shardSpeed = 22f;

    [Tooltip("Damage for each shard bullet.")]
    public float shardDamage = 6f;

    [Tooltip("Random offset (degrees) applied to the ring for variety.")]
    public float randomRingOffset = 0f;

    [Tooltip("Small lift to avoid clipping into the ground when spawning shards.")]
    public float spawnLift = 0.02f;

    [Header("Impact Flash")]
    [Tooltip("Enable a quick light flash at the impact point.")]
    public bool enableImpactFlash = true;
    [Tooltip("Peak brightness of the flash.")]
    [Min(0f)] public float flashPeakIntensity = 6f;
    [Tooltip("Radius of the flash influence.")]
    [Min(0.1f)] public float flashRange = 12f;
    [Tooltip("How long the flash lasts (fade-out included).")]
    [Min(0.02f)] public float flashDuration = 0.18f;
    [Tooltip("Optional Y offset to keep light nicely above ground.")]
    public float flashHeight = 1.5f;
    [Tooltip("Flash color")]
    public Color flashColor = new Color(1f, 0.85f, 0.6f);
    [Tooltip("Cast realtime shadows (costly).")]
    public bool flashShadows = false;

    [Header("Ownership")]
    [Tooltip("Defaults to Player if not set at runtime.")]
    public GameObject owner;

    private Collider myCol;

    // ------------- IBullet implementation -------------
    public void Initialize(GameObject newOwner)
    {
        owner = newOwner;
    }

    // ---------------------------------------------------
    void Start()
    {
        // Default owner to Player if not set
        if (owner == null)
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                owner = playerObj;
                Debug.Log($"[ExplodingBullet] No owner provided. Defaulting to Player.", this);
            }
            else
            {
                Debug.LogWarning($"[ExplodingBullet] No owner found and no Player in scene!", this);
            }
        }

        myCol = GetComponent<Collider>();
        myCol.isTrigger = true;

        Destroy(gameObject, lifetime);

        // Ignore collisions with owner
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
        if (owner && other.transform.IsChildOf(owner.transform)) return;

        Vector3 hitPoint = other.ClosestPoint(transform.position);
        Vector3 hitNormal = Vector3.up;

        var dirToCenter = (other.bounds.center - hitPoint).normalized;
        if (dirToCenter.sqrMagnitude > 0.001f) hitNormal = dirToCenter;

        // Boss
        var boss = other.GetComponentInParent<BossEnemy>();
        if (boss)
        {
            boss.ApplyDamageFrom((damage, owner));
            Explode(hitPoint, hitNormal);
            Destroy(gameObject);
            return;
        }

        // Enemy
        if (other.CompareTag("Enemy"))
        {
            var enemy = other.GetComponent<EnemyController>();
            if (enemy) enemy.TakeDamage(damage);
            Explode(hitPoint, hitNormal);
            Destroy(gameObject);
            return;
        }

        // Friendly / Player (no damage, just explode)
        if (other.CompareTag("FriendlyAI") || other.CompareTag("Player"))
        {
            Explode(hitPoint, hitNormal);
            Destroy(gameObject);
            return;
        }

        // Solid world
        if (!other.isTrigger)
        {
            Explode(hitPoint, hitNormal);
            Destroy(gameObject);
        }
    }

    private void Explode(Vector3 impactPoint, Vector3 impactNormal)
    {
        // Impact flash (runs independently of this object's lifetime)
        if (enableImpactFlash)
        {
            Vector3 flashPos = impactPoint;
            flashPos.y += flashHeight;
            SpawnImpactFlash(flashPos, flashDuration, flashRange, flashPeakIntensity, flashColor, flashShadows);
        }

        // Spawn shards
        if (shardBulletPrefab == null || shardCount <= 0) return;

        float baseAngle = (randomRingOffset > 0f) ? Random.Range(0f, randomRingOffset) : 0f;
        float step = 360f / shardCount;

        Vector3 spawnPos = impactPoint + impactNormal * spawnLift;

        for (int i = 0; i < shardCount; i++)
        {
            float angle = baseAngle + step * i;
            Quaternion rot = Quaternion.AngleAxis(angle, Vector3.up);
            Vector3 dir = rot * Vector3.forward;

            var go = Instantiate(shardBulletPrefab, spawnPos, Quaternion.LookRotation(dir, Vector3.up));

            // Pass owner to shards
            if (go.TryGetComponent<Bullet>(out var bullet))
            {
                bullet.Initialize(owner);
                bullet.speed = shardSpeed;
                bullet.damage = shardDamage;
            }
        }
    }

    // --------- One-shot flash spawner (self-cleaning) ---------
    private static void SpawnImpactFlash(
        Vector3 position,
        float duration,
        float range,
        float peakIntensity,
        Color color,
        bool useShadows)
    {
        var host = new GameObject("Impact Flash (OneShot)");
        host.transform.position = position;

        var light = host.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = range;
        light.color = color;
        light.intensity = 0f;
        light.shadows = useShadows ? LightShadows.Soft : LightShadows.None;

        var runner = host.AddComponent<ImpactFlashRunner>();
        runner.Run(light, duration, peakIntensity);
    }

    /// <summary>
    /// Tiny helper that animates the light intensity and destroys itself afterward.
    /// </summary>
    private sealed class ImpactFlashRunner : MonoBehaviour
    {
        public void Run(Light light, float duration, float peak)
        {
            StartCoroutine(DoFlash(light, duration, peak));
        }

        private IEnumerator DoFlash(Light light, float duration, float peak)
        {
            // Snappy ease-out curve without allocating AnimationCurve instances each time.
            float t = 0f;
            // quick rise
            light.intensity = peak;
            while (t < duration)
            {
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / duration);
                // cubic ease-out-ish fade (fast at first, then gentle)
                float k = 1f - (u * u * (3f - 2f * u));
                light.intensity = peak * k;
                yield return null;
            }
            if (light) light.intensity = 0f;
            Destroy(gameObject);
        }
    }
}

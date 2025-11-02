using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SphereCollider))]
public class ShieldDome : MonoBehaviour
{
    [Header("Ownership")]
    public IronSentinelBoss owner;

    [Header("Shield Stats")]
    public float maxShieldHP = 120f;
    public float regenPerSecond = 0f;
    public float cooldownBeforeRegen = 2.5f;

    [Header("Filters")]
    public string[] blockedProjectileTags;

    private float _hp;
    private float _nextRegenAt;

    private void OnEnable()
    {
        _hp = maxShieldHP;
        _nextRegenAt = Time.time + cooldownBeforeRegen;
    }

    private void Update()
    {
        if (regenPerSecond > 0f && Time.time >= _nextRegenAt)
            _hp = Mathf.Min(maxShieldHP, _hp + regenPerSecond * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_hp <= 0f) return;

        // DO NOT block projectiles from our own owner/root
        if (owner && other && other.transform.root == owner.transform.root) return;

        bool shouldBlock = false;

        if (blockedProjectileTags != null && blockedProjectileTags.Length > 0)
        {
            for (int i = 0; i < blockedProjectileTags.Length; i++)
                if (other.CompareTag(blockedProjectileTags[i])) { shouldBlock = true; break; }
        }
        else
        {
            // Heuristic: block IBullets or names containing "Bullet"
            var c = other.GetComponent(typeof(IBullet));
            if (c != null || other.name.Contains("Bullet")) shouldBlock = true;
        }

        if (!shouldBlock) return;

        _hp -= 10f;
        _nextRegenAt = Time.time + cooldownBeforeRegen;

        if (other.attachedRigidbody) Destroy(other.attachedRigidbody.gameObject);
        else Destroy(other.gameObject);

        if (_hp <= 0f) Destroy(gameObject);
    }
}

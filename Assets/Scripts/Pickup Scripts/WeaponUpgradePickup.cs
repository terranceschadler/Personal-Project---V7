using UnityEngine;

public class WeaponUpgradePickup : PickupBase
{
    [Header("Bullet Damage Increase")]
    [Tooltip("Flat amount to add to bullet damage.")]
    public float flatIncrease = 5f;

    [Tooltip("Percent to add to current bullet damage (0.10 = +10%).")]
    [Range(0f, 5f)] public float percentIncrease = 0f;

    [Tooltip("If > 0, clamp the resulting bullet damage to this cap.")]
    public float bulletDamageCap = 0f;

    [Tooltip("If true, applies percent increase first, then flat. If false, adds flat then percent on the result.")]
    public bool percentThenFlat = true;

    protected override bool ApplyEffect(GameObject collector)
    {
        bool applied = false;

        // Try direct components first
        applied |= TryApplyToPlayer(collector);

        // If the collider belonged to a child, try the root as well
        if (!applied)
        {
            var root = collector.transform.root;
            if (root != null && root != collector.transform)
            {
                applied |= TryApplyToPlayer(root.gameObject);
            }
        }

        return applied;
    }

    private bool TryApplyToPlayer(GameObject go)
    {
        if (!go.TryGetComponent<PlayerController>(out var pc)) return false;

        float oldDamage = pc.bulletDamage;
        float newDamage = ComputeNewDamage(oldDamage);
        pc.bulletDamage = newDamage;

        Debug.Log($"[WeaponUpgradePickup] Bullet damage increased: {oldDamage:F1} -> {newDamage:F1}", this);

        return true;
    }

    private float ComputeNewDamage(float currentDamage)
    {
        float result;
        if (percentThenFlat)
        {
            result = currentDamage * (1f + Mathf.Max(0f, percentIncrease)) + Mathf.Max(0f, flatIncrease);
        }
        else
        {
            result = (currentDamage + Mathf.Max(0f, flatIncrease)) * (1f + Mathf.Max(0f, percentIncrease));
        }

        if (bulletDamageCap > 0f)
            result = Mathf.Min(result, bulletDamageCap);

        // Ensure it never goes below current damage in edge cases
        return Mathf.Max(result, currentDamage);
    }
}

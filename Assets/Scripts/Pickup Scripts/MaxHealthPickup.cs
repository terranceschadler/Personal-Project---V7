using UnityEngine;

public class MaxHealthPickup : PickupBase
{
    [Header("Max-Health Increase")]
    [Tooltip("Flat amount to add to max health.")]
    public float flatIncrease = 25f;

    [Tooltip("Percent to add to current max health (0.10 = +10%).")]
    [Range(0f, 5f)] public float percentIncrease = 0f;

    [Tooltip("If > 0, clamp the resulting max health to this cap.")]
    public float maxHealthCap = 0f;

    [Tooltip("If true, applies percent increase first, then flat. If false, adds flat then percent on the result.")]
    public bool percentThenFlat = true;

    [Header("Full Heal")]
    [Tooltip("Always fully restore health after increasing the max.")]
    public bool fullHeal = true;

    protected override bool ApplyEffect(GameObject collector)
    {
        bool applied = false;

        // Try direct components first
        applied |= TryApplyToPlayer(collector);
        applied |= TryApplyToFriendly(collector);

        // If the collider belonged to a child, try the root as well
        if (!applied)
        {
            var root = collector.transform.root;
            if (root != null && root != collector.transform)
            {
                applied |= TryApplyToPlayer(root.gameObject);
                applied |= TryApplyToFriendly(root.gameObject);
            }
        }

        return applied;
    }

    private bool TryApplyToPlayer(GameObject go)
    {
        if (!go.TryGetComponent<PlayerController>(out var pc)) return false;

        float newMax = ComputeNewMax(pc.maxHealth);
        pc.maxHealth = newMax;

        if (fullHeal)
        {
            // Assume Heal(...) clamps to max internally
            pc.Heal(1_000_000f);
        }

        return true;
    }

    private bool TryApplyToFriendly(GameObject go)
    {
        if (!go.TryGetComponent<FriendlyAI>(out var fa)) return false;

        float newMax = ComputeNewMax(fa.maxHealth);
        fa.maxHealth = newMax;

        if (fullHeal)
        {
            // Heal clamps to max in FriendlyAI
            fa.Heal(1_000_000f);
        }

        return true;
    }

    private float ComputeNewMax(float currentMax)
    {
        float result;
        if (percentThenFlat)
        {
            result = currentMax * (1f + Mathf.Max(0f, percentIncrease)) + Mathf.Max(0f, flatIncrease);
        }
        else
        {
            result = (currentMax + Mathf.Max(0f, flatIncrease)) * (1f + Mathf.Max(0f, percentIncrease));
        }

        if (maxHealthCap > 0f)
            result = Mathf.Min(result, maxHealthCap);

        // Ensure it never goes below current max in edge cases
        return Mathf.Max(result, currentMax);
    }
}

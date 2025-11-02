using UnityEngine;

public class HealthPickup : PickupBase
{
    [Header("Health")]
    [Tooltip("How much health to restore when collected.")]
    public float healAmount = 25f;

    protected override bool ApplyEffect(GameObject collector)
    {
        // Try Player
        if (collector.TryGetComponent<PlayerController>(out var pc))
        {
            pc.Heal(healAmount);
            return true;
        }

        // Try Friendly AI
        if (collector.TryGetComponent<FriendlyAI>(out var fa))
        {
            fa.Heal(healAmount);
            return true;
        }

        // (Optional) If collector is a child, try the root
        var root = collector.transform.root;
        if (root != null && root != collector.transform)
        {
            if (root.TryGetComponent<PlayerController>(out var pc2))
            {
                pc2.Heal(healAmount);
                return true;
            }
            if (root.TryGetComponent<FriendlyAI>(out var fa2))
            {
                fa2.Heal(healAmount);
                return true;
            }
        }

        return false; // effect not applied; base will still collect
    }
}

using UnityEngine;

/// <summary>
/// Weapon upgrade pickup that increases player's weapon stats.
/// Can boost damage, fire rate, and bullet speed.
/// </summary>
public class WeaponUpgradePickup : PickupBase
{
    [Header("Upgrade Bonuses")]
    [Tooltip("Damage increase (additive). 0 = no change.")]
    public float damageBonus = 5f;

    [Tooltip("Fire rate multiplier (1.0 = no change, 1.2 = 20% faster). Applied multiplicatively.")]
    public float fireRateMultiplier = 1.1f;

    [Tooltip("Bullet speed increase (additive). 0 = no change.")]
    public float bulletSpeedBonus = 2f;

    [Header("Score Reward")]
    [Tooltip("Score points awarded when collected.")]
    public int scoreOnCollect = 500;

    protected override bool ApplyEffect(GameObject collector)
    {
        var player = collector.GetComponent<PlayerController>();
        if (player == null) return false;

        // Increase base bullet damage
        if (damageBonus != 0f)
        {
            player.bulletDamage += damageBonus;
            Debug.Log($"[WeaponUpgrade] Damage increased by {damageBonus} to {player.bulletDamage}");
        }

        // Award score
        if (scoreOnCollect > 0)
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.AddScore(scoreOnCollect);
        }

        // Note: Fire rate and bullet speed are per-weapon (ScriptableObject) properties,
        // which are shared assets. To upgrade those, we'd need runtime weapon instances.
        // For now, we only upgrade the player's bulletDamage which affects all shots.
        // Future enhancement: track per-player multipliers for fire rate/speed.

        return true;
    }
}

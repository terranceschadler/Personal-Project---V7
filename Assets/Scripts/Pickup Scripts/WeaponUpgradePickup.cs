using UnityEngine;

/// <summary>
/// Weapon upgrade pickup that increases player's weapon stats.
/// Can boost damage, fire rate, and bullet speed.
/// Available in different quality tiers: Common, Rare, Epic.
/// </summary>
public class WeaponUpgradePickup : PickupBase
{
    public enum UpgradeQuality
    {
        Common,  // Basic upgrade from regular enemies
        Rare,    // Better upgrade from mini bosses
        Epic     // Best upgrade from full bosses
    }

    [Header("Upgrade Quality")]
    [Tooltip("Quality tier of this upgrade. Higher tiers give better bonuses.")]
    public UpgradeQuality quality = UpgradeQuality.Common;

    [Header("Upgrade Bonuses (Common Tier)")]
    [Tooltip("Damage increase for Common quality. Rare=2x, Epic=3x.")]
    public float baseDamageBonus = 3f;

    [Tooltip("Fire rate multiplier for Common (1.0 = no change, 1.05 = 5% faster). Rare=1.5x multiplier, Epic=2x multiplier.")]
    public float baseFireRateMultiplier = 1.05f;

    [Tooltip("Bullet speed increase for Common. Rare=2x, Epic=3x.")]
    public float baseBulletSpeedBonus = 1f;

    [Header("Score Reward")]
    [Tooltip("Base score for Common. Rare=2x, Epic=4x.")]
    public int baseScoreReward = 250;

    [Header("Visual Feedback")]
    [Tooltip("Show upgrade quality in console when collected.")]
    public bool showQualityInLog = true;

    protected override bool ApplyEffect(GameObject collector)
    {
        var player = collector.GetComponent<PlayerController>();
        if (player == null) return false;

        // Calculate actual bonuses based on quality
        float qualityMultiplier = GetQualityMultiplier();
        float damageBonus = baseDamageBonus * qualityMultiplier;
        int scoreReward = Mathf.RoundToInt(baseScoreReward * GetScoreMultiplier());

        // Increase base bullet damage
        if (damageBonus > 0f)
        {
            player.bulletDamage += damageBonus;

            if (showQualityInLog)
            {
                string colorCode = GetQualityColor();
                Debug.Log($"<color={colorCode}>[{quality} Weapon Upgrade]</color> Damage +{damageBonus:F1} (now {player.bulletDamage:F1})");
            }
        }

        // Award score
        if (scoreReward > 0)
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.AddScore(scoreReward);
        }

        // Note: Fire rate and bullet speed are per-weapon (ScriptableObject) properties,
        // which are shared assets. To upgrade those, we'd need runtime weapon instances.
        // For now, we only upgrade the player's bulletDamage which affects all shots.
        // Future enhancement: track per-player multipliers for fire rate/speed.

        return true;
    }

    private float GetQualityMultiplier()
    {
        switch (quality)
        {
            case UpgradeQuality.Common: return 1f;
            case UpgradeQuality.Rare: return 2f;
            case UpgradeQuality.Epic: return 3.5f;
            default: return 1f;
        }
    }

    private float GetScoreMultiplier()
    {
        switch (quality)
        {
            case UpgradeQuality.Common: return 1f;
            case UpgradeQuality.Rare: return 2.5f;
            case UpgradeQuality.Epic: return 5f;
            default: return 1f;
        }
    }

    private string GetQualityColor()
    {
        switch (quality)
        {
            case UpgradeQuality.Common: return "white";
            case UpgradeQuality.Rare: return "#4A9EFF";  // Blue
            case UpgradeQuality.Epic: return "#A335EE";   // Purple
            default: return "white";
        }
    }
}

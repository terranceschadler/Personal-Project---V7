using UnityEngine;
using UnityEngine.UI;
using TMPro; // TextMeshPro support

/// <summary>
/// Custom UI display for weapon stats.
/// Attach to a Canvas with Text/TextMeshPro components.
/// Supports both Unity UI Text and TextMeshPro!
/// </summary>
public class WeaponStatsDisplay : MonoBehaviour
{
    [Header("References")]
    public PlayerWeaponController weaponController;
    
    [Header("Text Component (Use ONE)")]
    [Tooltip("For Unity UI Text")]
    public Text statsText;
    [Tooltip("For TextMeshPro")]
    public TextMeshProUGUI statsTMP;
    
    [Header("Settings")]
    public bool showAllStats = true;
    public bool updateEveryFrame = false;
    public float updateInterval = 0.5f;
    
    private float nextUpdateTime = 0f;
    
    void Start()
    {
        // Auto-find weapon controller if not assigned
        if (weaponController == null)
        {
            weaponController = FindObjectOfType<PlayerWeaponController>();
        }
        
        if (weaponController == null)
        {
            Debug.LogWarning("[WeaponStatsDisplay] No PlayerWeaponController found!");
            enabled = false;
            return;
        }
        
        // Subscribe to upgrade events
        weaponController.OnUpgradeApplied += OnUpgradeApplied;
        weaponController.OnAmmoChanged += OnAmmoChanged;
        
        UpdateDisplay();
    }
    
    void Update()
    {
        if (updateEveryFrame)
        {
            UpdateDisplay();
        }
        else if (Time.time >= nextUpdateTime)
        {
            UpdateDisplay();
            nextUpdateTime = Time.time + updateInterval;
        }
    }
    
    void OnUpgradeApplied(UpgradePickup upgrade)
    {
        // Update immediately when upgrade is picked up
        UpdateDisplay();
        
        // Optional: Flash or highlight the changed stat
        Debug.Log($"<color=green>★ UPGRADE: {upgrade.pickupName}</color>");
    }
    
    void OnAmmoChanged(int current, int max)
    {
        // Update ammo display
        if (!updateEveryFrame)
        {
            UpdateDisplay();
        }
    }
    
    void UpdateDisplay()
    {
        if (weaponController == null) return;
        if (statsText == null && statsTMP == null)
        {
            Debug.LogWarning("[WeaponStatsDisplay] No text component assigned!");
            return;
        }
        
        WeaponStats stats = weaponController.GetCurrentStats();
        int currentAmmo = weaponController.GetCurrentAmmo();
        int maxAmmo = weaponController.GetMaxAmmo();
        
        string display = "";
        
        // Always show ammo
        display += $"<b>AMMO:</b> {currentAmmo}/{maxAmmo}\n\n";
        
        if (showAllStats)
        {
            // Show all stats
            display += $"<b>═══ WEAPON STATS ═══</b>\n";
            display += $"<color=yellow>Damage:</color> {stats.GetTotalDamage():F1}\n";
            display += $"<color=yellow>Fire Rate:</color> {stats.GetTotalFireRate():F2}/s\n";
            display += $"<color=yellow>Reload:</color> {stats.GetTotalReloadTime():F2}s\n";
            display += $"<color=yellow>Bullets/Shot:</color> {stats.bulletsPerShot}\n";
            
            if (stats.bulletSpread > 0)
                display += $"<color=yellow>Spread:</color> {stats.bulletSpread:F1}°\n";
            
            display += $"<color=yellow>Velocity:</color> {stats.GetTotalVelocity():F1}\n";
            
            if (stats.critChance > 0)
                display += $"<color=orange>Crit Chance:</color> {(stats.critChance * 100):F1}%\n";
            
            if (stats.piercingCount > 0)
                display += $"<color=cyan>Piercing:</color> {stats.piercingCount}\n";
            
            if (stats.bounceCount > 0)
                display += $"<color=cyan>Bounces:</color> {stats.bounceCount}\n";
            
            // Special effects
            if (stats.isExplosive)
                display += $"\n<color=red>💥 EXPLOSIVE</color>\n  Radius: {stats.explosionRadius:F1}m\n  Damage: {stats.explosionDamage:F0}\n";
            
            if (stats.isHoming)
                display += $"\n<color=cyan>🎯 HOMING</color>\n  Strength: {stats.homingStrength:F1}\n";
            
            // Elemental effects
            if (stats.hasBurnEffect)
                display += $"\n<color=orange>🔥 BURN</color>\n  {stats.burnDamagePerSecond:F1} DPS for {stats.burnDuration:F1}s\n";
            
            if (stats.hasPoisonEffect)
                display += $"\n<color=green>☠️ POISON</color>\n  {stats.poisonDamagePerSecond:F1} DPS for {stats.poisonDuration:F1}s\n";
            
            if (stats.hasFreezeEffect)
                display += $"\n<color=cyan>❄️ FREEZE</color>\n  {(stats.freezeSlowPercent * 100):F0}% slow for {stats.freezeDuration:F1}s\n";
            
            if (stats.hasShockEffect)
                display += $"\n<color=yellow>⚡ SHOCK</color>\n  Chains {stats.shockChainCount}x for {stats.shockDamage:F0} dmg\n";
        }
        else
        {
            // Show only key stats
            display += $"<b>Damage:</b> {stats.GetTotalDamage():F1}\n";
            display += $"<b>Fire Rate:</b> {stats.GetTotalFireRate():F2}/s\n";
            display += $"<b>Magazine:</b> {stats.GetTotalMagazineSize()}\n";
        }
        
        // Set text on whichever component is assigned
        if (statsText != null)
        {
            statsText.text = display;
        }
        
        if (statsTMP != null)
        {
            statsTMP.text = display;
        }
    }
    
    void OnDestroy()
    {
        // Unsubscribe from events
        if (weaponController != null)
        {
            weaponController.OnUpgradeApplied -= OnUpgradeApplied;
            weaponController.OnAmmoChanged -= OnAmmoChanged;
        }
    }
}

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.SceneManagement;

/// <summary>
/// Enhanced weapon stats display with safeguards against double rendering.
/// Includes diagnostics and prevents duplicate updates.
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
    
    [Header("Display Format")]
    [Tooltip("Use standard formatting to avoid font rendering issues")]
    public bool useStandardFormatting = true;
    
    [Header("Debug")]
    [Tooltip("Show diagnostic info in console")]
    public bool showDebugInfo = false;
    [Tooltip("Track update frequency")]
    public bool trackUpdateCalls = false;
    
    private float nextUpdateTime = 0f;
    private bool isUpdating = false;
    private int updateCallCount = 0;
    private float lastReportTime = 0f;
    private static int instanceCount = 0;
    private static WeaponStatsDisplay primaryInstance = null;
    private int myInstanceId;
    
    void Awake()
    {
        // Track instances to detect duplicates
        instanceCount++;
        myInstanceId = instanceCount;
        
        if (primaryInstance == null)
        {
            // This is the first/primary instance
            primaryInstance = this;
            if (showDebugInfo)
            {
                Debug.Log($"[WeaponStatsDisplay] Primary instance #{myInstanceId} established on {gameObject.name}");
            }
        }
        else if (primaryInstance != this)
        {
            // This is a duplicate - destroy it!
            Debug.LogWarning($"[WeaponStatsDisplay] Duplicate instance #{myInstanceId} detected on {gameObject.name}! " +
                           $"Destroying to prevent double rendering. Primary is on {primaryInstance.gameObject.name}");
            Destroy(gameObject);
            return;
        }
    }
    
    void OnEnable()
    {
        // Subscribe to scene loaded to reinitialize references
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    void OnDisable()
    {
        // Unsubscribe
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }
    
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Re-initialize when scene reloads (this fixes the ammo counter after restart)
        if (showDebugInfo)
        {
            Debug.Log($"[WeaponStatsDisplay] Scene reloaded: {scene.name}, re-initializing...");
        }
        
        // Unsubscribe from old events if weaponController exists
        if (weaponController != null)
        {
            weaponController.OnUpgradeApplied -= OnUpgradeApplied;
            weaponController.OnAmmoChanged -= OnAmmoChanged;
        }
        
        // Clear reference so it will be re-found
        weaponController = null;
        
        // Re-initialize with delay
        StartCoroutine(DelayedInitialize());
    }
    
    void Start()
    {
        // Delay initialization to ensure all references are ready after scene load
        StartCoroutine(DelayedInitialize());
    }
    
    IEnumerator DelayedInitialize()
    {
        // Wait for end of frame to ensure everything is initialized
        yield return new WaitForEndOfFrame();
        
        // Validate setup
        if (statsText == null && statsTMP == null)
        {
            Debug.LogError("[WeaponStatsDisplay] No text component assigned! Assign either statsText or statsTMP in Inspector.");
            enabled = false;
            yield break;
        }
        
        if (statsText != null && statsTMP != null)
        {
            Debug.LogWarning("[WeaponStatsDisplay] Both Text and TMP components assigned! Will use TMP only to prevent double rendering.");
            statsText = null; // Clear to prevent double rendering
        }
        
        // Auto-find weapon controller if not assigned
        if (weaponController == null)
        {
            if (showDebugInfo) Debug.Log("[WeaponStatsDisplay] Searching for PlayerWeaponController...");
            weaponController = FindObjectOfType<PlayerWeaponController>();
        }
        
        // If still not found, wait a bit and try again
        if (weaponController == null)
        {
            if (showDebugInfo) Debug.Log("[WeaponStatsDisplay] Not found, waiting 0.5s and trying again...");
            yield return new WaitForSeconds(0.5f);
            weaponController = FindObjectOfType<PlayerWeaponController>();
        }
        
        // If STILL not found, wait even longer (scene might be loading)
        if (weaponController == null)
        {
            if (showDebugInfo) Debug.Log("[WeaponStatsDisplay] Still not found, waiting 1.0s more...");
            yield return new WaitForSeconds(1.0f);
            weaponController = FindObjectOfType<PlayerWeaponController>();
        }
        
        if (weaponController == null)
        {
            Debug.LogWarning("[WeaponStatsDisplay] No PlayerWeaponController found after multiple attempts!");
            enabled = false;
            yield break;
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"[WeaponStatsDisplay] Found PlayerWeaponController on {weaponController.gameObject.name}");
        }
        
        // Unsubscribe first to prevent duplicate subscriptions
        weaponController.OnUpgradeApplied -= OnUpgradeApplied;
        weaponController.OnAmmoChanged -= OnAmmoChanged;
        
        // Subscribe to events
        weaponController.OnUpgradeApplied += OnUpgradeApplied;
        weaponController.OnAmmoChanged += OnAmmoChanged;
        
        if (showDebugInfo)
        {
            Debug.Log($"[WeaponStatsDisplay #{myInstanceId}] Initialized successfully on {gameObject.name}");
        }
        
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
        
        if (showDebugInfo)
        {
            Debug.Log($"<color=green>★ UPGRADE: {upgrade.pickupName}</color>");
        }
    }
    
    void OnAmmoChanged(int current, int reserve)
    {
        // Update ammo display
        if (!updateEveryFrame)
        {
            UpdateDisplay();
        }
    }
    
    void UpdateDisplay()
    {
        // Prevent re-entrant calls
        if (isUpdating)
        {
            if (showDebugInfo)
            {
                Debug.LogWarning("[WeaponStatsDisplay] Blocked re-entrant UpdateDisplay call!");
            }
            return;
        }
        
        // Critical null checks
        if (weaponController == null)
        {
            if (showDebugInfo)
            {
                Debug.LogWarning("[WeaponStatsDisplay] weaponController is null, skipping update");
            }
            return;
        }
        
        if (statsText == null && statsTMP == null)
        {
            Debug.LogWarning("[WeaponStatsDisplay] No text component assigned!");
            return;
        }
        
        // Track update frequency for diagnostics
        if (trackUpdateCalls)
        {
            updateCallCount++;
            if (Time.time - lastReportTime >= 1f)
            {
                Debug.Log($"[WeaponStatsDisplay #{myInstanceId}] Updates per second: {updateCallCount}");
                updateCallCount = 0;
                lastReportTime = Time.time;
            }
        }
        
        isUpdating = true;
        
        try
        {
            WeaponStats stats = weaponController.GetCurrentStats();
            
            // Additional null check for stats
            if (stats == null)
            {
                Debug.LogWarning("[WeaponStatsDisplay] GetCurrentStats returned null!");
                isUpdating = false;
                return;
            }
            
            int currentAmmo = weaponController.GetCurrentAmmo();
            int maxAmmo = weaponController.GetMaxAmmo();
            int reserveAmmo = weaponController.GetReserveAmmo();
            int maxReserve = weaponController.GetMaxReserveAmmo();
            
            string display = BuildDisplayString(stats, currentAmmo, maxAmmo, reserveAmmo, maxReserve);
            
            // CRITICAL: Set text on ONLY ONE component to prevent double rendering
            if (statsTMP != null)
            {
                statsTMP.text = display;
            }
            else if (statsText != null)
            {
                statsText.text = display;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[WeaponStatsDisplay] Error in UpdateDisplay: {ex.Message}");
        }
        finally
        {
            isUpdating = false;
        }
    }
    
    private string BuildDisplayString(WeaponStats stats, int currentAmmo, int maxAmmo, int reserveAmmo, int maxReserve)
    {
        string display = "";
        
        // FIXED: Use string.Format to prevent font ligatures and ensure clean rendering
        if (useStandardFormatting)
        {
            display += string.Format("<b>AMMO:</b> {0}/{1}\n", currentAmmo, maxAmmo);
            display += string.Format("<b>RESERVE:</b> {0}/{1}\n\n", reserveAmmo, maxReserve);
        }
        else
        {
            // Original formatting (may have font issues)
            display += $"<b>AMMO:</b> {currentAmmo}/{maxAmmo}\n";
            display += $"<b>RESERVE:</b> {reserveAmmo}/{maxReserve}\n\n";
        }
        
        if (showAllStats)
        {
            // Show all stats
            display += "<b>═══ WEAPON STATS ═══</b>\n";
            display += string.Format("<color=#FFFF00>Damage:</color> {0:F1}\n", stats.GetTotalDamage());
            display += string.Format("<color=#FFFF00>Fire Rate:</color> {0:F2}/s\n", stats.GetTotalFireRate());
            display += string.Format("<color=#FFFF00>Reload:</color> {0:F2}s\n", stats.GetTotalReloadTime());
            display += string.Format("<color=#FFFF00>Bullets/Shot:</color> {0}\n", stats.bulletsPerShot);
            
            if (stats.bulletSpread > 0)
                display += string.Format("<color=#FFFF00>Spread:</color> {0:F1}°\n", stats.bulletSpread);
            
            display += string.Format("<color=#FFFF00>Velocity:</color> {0:F1}\n", stats.GetTotalVelocity());
            
            if (stats.critChance > 0)
                display += string.Format("<color=#FFA500>Crit Chance:</color> {0:F1}%\n", stats.critChance * 100);
            
            if (stats.piercingCount > 0)
                display += string.Format("<color=#00FFFF>Piercing:</color> {0}\n", stats.piercingCount);
            
            if (stats.bounceCount > 0)
                display += string.Format("<color=#00FFFF>Bounces:</color> {0}\n", stats.bounceCount);
            
            // Special effects
            if (stats.isExplosive)
                display += string.Format("\n<color=#FF0000>💥 EXPLOSIVE</color>\n  Radius: {0:F1}m\n  Damage: {1:F0}\n", 
                    stats.explosionRadius, stats.explosionDamage);
            
            if (stats.isHoming)
                display += string.Format("\n<color=#00FFFF>🎯 HOMING</color>\n  Strength: {0:F1}\n", stats.homingStrength);
            
            // Elemental effects
            if (stats.hasBurnEffect)
                display += string.Format("\n<color=#FFA500>🔥 BURN</color>\n  {0:F1} DPS for {1:F1}s\n", 
                    stats.burnDamagePerSecond, stats.burnDuration);
            
            if (stats.hasPoisonEffect)
                display += string.Format("\n<color=#00FF00>☠️ POISON</color>\n  {0:F1} DPS for {1:F1}s\n", 
                    stats.poisonDamagePerSecond, stats.poisonDuration);
            
            if (stats.hasFreezeEffect)
                display += string.Format("\n<color=#00FFFF>❄️ FREEZE</color>\n  {0:F0}% slow for {1:F1}s\n", 
                    stats.freezeSlowPercent * 100, stats.freezeDuration);
            
            if (stats.hasShockEffect)
                display += string.Format("\n<color=#FFFF00>⚡ SHOCK</color>\n  Chains {0}x for {1:F0} dmg\n", 
                    stats.shockChainCount, stats.shockDamage);
        }
        else
        {
            // Show only key stats
            display += string.Format("<b>Damage:</b> {0:F1}\n", stats.GetTotalDamage());
            display += string.Format("<b>Fire Rate:</b> {0:F2}/s\n", stats.GetTotalFireRate());
            display += string.Format("<b>Magazine:</b> {0}\n", stats.GetTotalMagazineSize());
        }
        
        return display;
    }
    
    void OnDestroy()
    {
        // Unsubscribe from events
        if (weaponController != null)
        {
            weaponController.OnUpgradeApplied -= OnUpgradeApplied;
            weaponController.OnAmmoChanged -= OnAmmoChanged;
        }
        
        // Reset primary instance if this was it
        if (primaryInstance == this)
        {
            primaryInstance = null;
        }
        
        instanceCount--;
        
        if (showDebugInfo)
        {
            Debug.Log($"[WeaponStatsDisplay #{myInstanceId}] Destroyed. Remaining instances: {instanceCount}");
        }
    }
    
    // Diagnostic helper
    [ContextMenu("Check for Duplicates")]
    void CheckForDuplicates()
    {
        WeaponStatsDisplay[] displays = FindObjectsOfType<WeaponStatsDisplay>();
        Debug.Log($"=== WeaponStatsDisplay Diagnostic ===");
        Debug.Log($"Total instances found: {displays.Length}");
        
        if (displays.Length > 1)
        {
            Debug.LogWarning("⚠️ MULTIPLE INSTANCES DETECTED! This will cause double rendering.");
            foreach (var display in displays)
            {
                Debug.Log($"  - Instance on: {display.gameObject.name} (Enabled: {display.enabled})");
            }
        }
        else
        {
            Debug.Log("✓ Only one instance found - good!");
        }
        
        // Check text components
        if (statsTMP != null && statsText != null)
        {
            Debug.LogWarning("⚠️ Both Text and TMP assigned! This may cause double rendering.");
        }
        else if (statsTMP != null)
        {
            Debug.Log($"✓ Using TextMeshPro on: {statsTMP.gameObject.name}");
        }
        else if (statsText != null)
        {
            Debug.Log($"✓ Using Text on: {statsText.gameObject.name}");
        }
        else
        {
            Debug.LogError("❌ No text component assigned!");
        }
    }
}

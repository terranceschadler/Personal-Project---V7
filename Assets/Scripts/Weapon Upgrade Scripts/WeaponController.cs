using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Main weapon controller that handles firing, reloading, and applying upgrades.
/// Attach this to the player GameObject.
/// </summary>
public class WeaponController : MonoBehaviour
{
    [Header("Weapon Configuration")]
    [Tooltip("The weapon data asset to use")]
    public WeaponData weaponData;
    
    [Header("Fire Points")]
    [Tooltip("Transform where bullets spawn")]
    public Transform firePoint;
    
    [Header("Input")]
    [Tooltip("Fire input button name")]
    public string fireButton = "Fire1";
    
    [Tooltip("Reload input button name")]
    public string reloadButton = "Reload";
    
    [Header("Audio")]
    public AudioSource audioSource;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    // Runtime stats
    private WeaponStats currentStats;
    private int currentAmmo;
    private float nextFireTime;
    private bool isReloading;
    private List<UpgradePickup> appliedUpgrades = new List<UpgradePickup>();
    
    // Events (optional - for UI updates)
    public System.Action<int, int> OnAmmoChanged; // current, max
    public System.Action<UpgradePickup> OnUpgradeApplied;
    public System.Action<float> OnReloadStarted; // reload time
    
    private void Start()
    {
        if (weaponData == null)
        {
            Debug.LogError("[WeaponController] No weapon data assigned!");
            enabled = false;
            return;
        }
        
        if (firePoint == null)
        {
            Debug.LogWarning("[WeaponController] No fire point assigned. Using this transform.");
            firePoint = transform;
        }
        
        // Initialize weapon stats
        currentStats = weaponData.CreateRuntimeStats();
        currentAmmo = currentStats.GetTotalMagazineSize();
        
        OnAmmoChanged?.Invoke(currentAmmo, currentStats.GetTotalMagazineSize());
    }
    
    private void Update()
    {
        if (isReloading) return;
        
        // Check for reload input
        if (Input.GetButtonDown(reloadButton) && currentAmmo < currentStats.GetTotalMagazineSize())
        {
            StartReload();
            return;
        }
        
        // Check for fire input
        if (Input.GetButton(fireButton) && Time.time >= nextFireTime && currentAmmo > 0)
        {
            Fire();
        }
        
        // Auto-reload when empty
        if (currentAmmo <= 0 && !isReloading)
        {
            StartReload();
        }
    }
    
    private void Fire()
    {
        currentAmmo--;
        nextFireTime = Time.time + (1f / currentStats.GetTotalFireRate());
        
        // Play sound
        if (audioSource != null && weaponData.fireSound != null)
        {
            audioSource.PlayOneShot(weaponData.fireSound);
        }
        
        // Spawn muzzle flash
        if (weaponData.muzzleFlashPrefab != null)
        {
            Instantiate(weaponData.muzzleFlashPrefab, firePoint.position, firePoint.rotation);
        }
        
        // Fire bullets
        for (int i = 0; i < currentStats.bulletsPerShot; i++)
        {
            FireProjectile(i);
        }
        
        OnAmmoChanged?.Invoke(currentAmmo, currentStats.GetTotalMagazineSize());
    }
    
    private void FireProjectile(int pelletIndex)
    {
        // Calculate spread
        float spreadAngle = 0f;
        if (currentStats.bulletsPerShot > 1)
        {
            // Distribute pellets evenly across spread angle
            float spreadRange = currentStats.bulletSpread;
            float step = spreadRange / (currentStats.bulletsPerShot - 1);
            spreadAngle = -spreadRange / 2f + (step * pelletIndex);
        }
        else if (currentStats.bulletSpread > 0)
        {
            // Random spread for single shot
            spreadAngle = Random.Range(-currentStats.bulletSpread / 2f, currentStats.bulletSpread / 2f);
        }
        
        // Calculate direction
        Quaternion spreadRotation = Quaternion.Euler(0, spreadAngle, 0);
        Quaternion finalRotation = firePoint.rotation * spreadRotation;
        
        // Instantiate bullet
        GameObject bulletObj = Instantiate(weaponData.bulletPrefab, firePoint.position, finalRotation);
        
        // Initialize bullet with stats
        EnhancedBullet bullet = bulletObj.GetComponent<EnhancedBullet>();
        if (bullet != null)
        {
            bullet.InitializeWithStats(gameObject, currentStats);
        }
        else
        {
            // Fallback to basic bullet interface
            IBullet basicBullet = bulletObj.GetComponent<IBullet>();
            if (basicBullet != null)
            {
                basicBullet.Initialize(gameObject);
            }
        }
    }
    
    private void StartReload()
    {
        isReloading = true;
        float reloadTime = currentStats.GetTotalReloadTime();
        
        OnReloadStarted?.Invoke(reloadTime);
        
        // Play sound
        if (audioSource != null && weaponData.reloadSound != null)
        {
            audioSource.PlayOneShot(weaponData.reloadSound);
        }
        
        StartCoroutine(ReloadCoroutine(reloadTime));
    }
    
    private IEnumerator ReloadCoroutine(float reloadTime)
    {
        yield return new WaitForSeconds(reloadTime);
        
        currentAmmo = currentStats.GetTotalMagazineSize();
        isReloading = false;
        
        OnAmmoChanged?.Invoke(currentAmmo, currentStats.GetTotalMagazineSize());
    }
    
    /// <summary>
    /// Apply an upgrade pickup to this weapon
    /// </summary>
    public void ApplyUpgrade(UpgradePickup upgrade)
    {
        if (upgrade == null) return;
        
        // Apply all effects from the upgrade
        upgrade.ApplyUpgrade(currentStats);
        
        // Track applied upgrades
        appliedUpgrades.Add(upgrade);
        
        // Refill ammo to new magazine size if it increased
        currentAmmo = Mathf.Min(currentAmmo, currentStats.GetTotalMagazineSize());
        
        OnUpgradeApplied?.Invoke(upgrade);
        OnAmmoChanged?.Invoke(currentAmmo, currentStats.GetTotalMagazineSize());
        
        if (showDebugInfo)
        {
            Debug.Log($"[WeaponController] Applied upgrade: {upgrade.pickupName}\n" +
                     $"Damage: {currentStats.GetTotalDamage():F1}\n" +
                     $"Fire Rate: {currentStats.GetTotalFireRate():F2}/s\n" +
                     $"Magazine: {currentStats.GetTotalMagazineSize()}\n" +
                     $"Bullets Per Shot: {currentStats.bulletsPerShot}");
        }
    }
    
    /// <summary>
    /// Get all applied upgrades
    /// </summary>
    public List<UpgradePickup> GetAppliedUpgrades()
    {
        return new List<UpgradePickup>(appliedUpgrades);
    }
    
    /// <summary>
    /// Get current weapon stats (for UI display)
    /// </summary>
    public WeaponStats GetCurrentStats()
    {
        return currentStats;
    }
    
    /// <summary>
    /// Get current ammo count
    /// </summary>
    public int GetCurrentAmmo()
    {
        return currentAmmo;
    }
    
    private void OnGUI()
    {
        if (!showDebugInfo) return;
        
        GUILayout.BeginArea(new Rect(10, 10, 300, 400));
        GUILayout.Label($"<b>Weapon Stats</b>", new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true });
        GUILayout.Label($"Ammo: {currentAmmo}/{currentStats.GetTotalMagazineSize()}");
        GUILayout.Label($"Damage: {currentStats.GetTotalDamage():F1}");
        GUILayout.Label($"Fire Rate: {currentStats.GetTotalFireRate():F2}/s");
        GUILayout.Label($"Reload Time: {currentStats.GetTotalReloadTime():F2}s");
        GUILayout.Label($"Bullets/Shot: {currentStats.bulletsPerShot}");
        GUILayout.Label($"Spread: {currentStats.bulletSpread:F1}°");
        GUILayout.Label($"Velocity: {currentStats.GetTotalVelocity():F1}");
        GUILayout.Label($"Crit Chance: {(currentStats.critChance * 100):F1}%");
        GUILayout.Label($"Piercing: {currentStats.piercingCount}");
        GUILayout.Label($"Bounces: {currentStats.bounceCount}");
        
        if (currentStats.isExplosive)
        {
            GUILayout.Label($"<color=orange>Explosive: {currentStats.explosionDamage:F0} dmg, {currentStats.explosionRadius:F1}m</color>", new GUIStyle(GUI.skin.label) { richText = true });
        }
        
        if (currentStats.isHoming)
        {
            GUILayout.Label($"<color=cyan>Homing: {currentStats.homingStrength:F1}</color>", new GUIStyle(GUI.skin.label) { richText = true });
        }
        
        if (currentStats.hasBurnEffect)
        {
            GUILayout.Label($"<color=red>Burn: {currentStats.burnDamagePerSecond:F1} DPS for {currentStats.burnDuration:F1}s</color>", new GUIStyle(GUI.skin.label) { richText = true });
        }
        
        GUILayout.EndArea();
    }
}

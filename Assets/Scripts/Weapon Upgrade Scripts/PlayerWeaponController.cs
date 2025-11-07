using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Weapon controller designed to integrate with existing PlayerController.
/// Can be called by external scripts (like PlayerController) to handle firing.
/// </summary>
public class PlayerWeaponController : MonoBehaviour
{
    [Header("Weapon Configuration")]
    [Tooltip("The weapon data asset to use")]
    public WeaponData weaponData;
    
    [Header("Fire Points")]
    [Tooltip("Transform where bullets spawn - can be same as PlayerController's firePoint")]
    public Transform firePoint;
    
    [Header("Audio")]
    public AudioSource audioSource;
    
    [Header("Debug")]
    public bool showDebugInfo = false;
    
    // Runtime stats
    private WeaponStats currentStats;
    private int currentAmmo;
    private bool isReloading;
    private List<UpgradePickup> appliedUpgrades = new List<UpgradePickup>();
    
    // Events (optional - for UI updates)
    public System.Action<int, int> OnAmmoChanged; // current, max
    public System.Action<UpgradePickup> OnUpgradeApplied;
    public System.Action<float> OnReloadStarted; // reload time
    public System.Action OnWeaponFired;
    
    private void Start()
    {
        if (weaponData == null)
        {
            Debug.LogError("[PlayerWeaponController] No weapon data assigned!");
            enabled = false;
            return;
        }
        
        if (firePoint == null)
        {
            // Try to find PlayerController's firePoint
            PlayerController player = GetComponent<PlayerController>();
            if (player != null && player.firePoint != null)
            {
                firePoint = player.firePoint;
                Debug.Log("[PlayerWeaponController] Using PlayerController's firePoint");
            }
            else
            {
                Debug.LogWarning("[PlayerWeaponController] No fire point assigned. Using this transform.");
                firePoint = transform;
            }
        }
        
        // Initialize weapon stats
        currentStats = weaponData.CreateRuntimeStats();
        currentAmmo = currentStats.GetTotalMagazineSize();
        
        OnAmmoChanged?.Invoke(currentAmmo, currentStats.GetTotalMagazineSize());
    }
    
    /// <summary>
    /// Check if weapon can fire (called by PlayerController before firing)
    /// </summary>
    public bool CanFire()
    {
        return !isReloading && currentAmmo > 0;
    }
    
    /// <summary>
    /// Get the fire rate for timing (called by PlayerController)
    /// </summary>
    public float GetFireRate()
    {
        return currentStats.GetTotalFireRate();
    }
    
    /// <summary>
    /// Trigger a shot (called by PlayerController when it's time to fire)
    /// </summary>
    public void Fire()
    {
        if (!CanFire())
        {
            // Try auto-reload if out of ammo
            if (!isReloading && currentAmmo <= 0)
            {
                StartReload();
            }
            return;
        }
        
        currentAmmo--;
        
        // Play sound
        if (audioSource != null && weaponData.fireSound != null)
        {
            audioSource.PlayOneShot(weaponData.fireSound);
        }
        
        // Spawn muzzle flash
        if (weaponData.muzzleFlashPrefab != null)
        {
            GameObject flash = Instantiate(weaponData.muzzleFlashPrefab, firePoint.position, firePoint.rotation);
            Destroy(flash, 0.5f);
        }
        
        // Fire bullets
        for (int i = 0; i < currentStats.bulletsPerShot; i++)
        {
            FireProjectile(i);
        }
        
        OnWeaponFired?.Invoke();
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
        EnhancedBullet enhancedBullet = bulletObj.GetComponent<EnhancedBullet>();
        if (enhancedBullet != null)
        {
            enhancedBullet.InitializeWithStats(gameObject, currentStats);
        }
        else
        {
            // Try legacy bullet
            Bullet legacyBullet = bulletObj.GetComponent<Bullet>();
            if (legacyBullet != null)
            {
                legacyBullet.Initialize(gameObject);
                // Apply basic stats to legacy bullet
                legacyBullet.damage = currentStats.GetTotalDamage();
                legacyBullet.speed = currentStats.GetTotalVelocity();
                legacyBullet.lifetime = currentStats.GetTotalLifetime();
            }
            else
            {
                // Fallback to IBullet interface
                IBullet basicBullet = bulletObj.GetComponent<IBullet>();
                if (basicBullet != null)
                {
                    basicBullet.Initialize(gameObject);
                }
            }
        }
    }
    
    /// <summary>
    /// Start reloading (can be called manually or automatically)
    /// </summary>
    public void StartReload()
    {
        if (isReloading || currentAmmo >= currentStats.GetTotalMagazineSize())
            return;
            
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
        
        // Adjust current ammo if magazine size changed
        int newMagSize = currentStats.GetTotalMagazineSize();
        if (currentAmmo > newMagSize)
        {
            currentAmmo = newMagSize;
        }
        
        OnUpgradeApplied?.Invoke(upgrade);
        OnAmmoChanged?.Invoke(currentAmmo, currentStats.GetTotalMagazineSize());
        
        if (showDebugInfo)
        {
            Debug.Log($"[PlayerWeaponController] ★ UPGRADE APPLIED: {upgrade.pickupName}\n" +
                     $"Effects: {upgrade.GetFullDescription()}\n" +
                     $"Damage: {currentStats.GetTotalDamage():F1} | Fire Rate: {currentStats.GetTotalFireRate():F2}/s | " +
                     $"Magazine: {currentStats.GetTotalMagazineSize()} | Bullets/Shot: {currentStats.bulletsPerShot}");
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
    
    /// <summary>
    /// Get max ammo (magazine size)
    /// </summary>
    public int GetMaxAmmo()
    {
        return currentStats.GetTotalMagazineSize();
    }
    
    /// <summary>
    /// Check if currently reloading
    /// </summary>
    public bool IsReloading()
    {
        return isReloading;
    }
}

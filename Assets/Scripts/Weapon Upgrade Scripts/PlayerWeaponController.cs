using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Weapon controller designed to integrate with PlayerController.
/// Handles firing, upgrades, ammo reserves, and reliable reloading.
/// </summary>
public class PlayerWeaponController : MonoBehaviour
{
    [Header("Weapon Configuration")]
    [Tooltip("Weapon data asset that defines base stats.")]
    public WeaponData weaponData;

    [Header("Fire Points")]
    [Tooltip("Transform where bullets spawn (usually from PlayerController).")]
    public Transform firePoint;

    [Header("Audio")]
    public AudioSource audioSource;

    [Header("Debug")]
    public bool showDebugInfo = false;

    // ---------------- Runtime stats ----------------
    private WeaponStats currentStats;

    // Magazine (clip) ammo
    private int currentAmmo;

    // Reload state
    private bool isReloading;

    // Upgrades record
    private List<UpgradePickup> appliedUpgrades = new List<UpgradePickup>();

    // Damage bonus from weapon upgrade pickups (separate from upgrade system)
    private float damageBonus = 0f;

    // ---------------- Reserve Ammo System ----------------
    [Header("Ammo Reserve")]
    [Tooltip("Maximum reserve ammo the player can hold (outside of the magazine).")]
    [SerializeField] private int maxReserveAmmo = 120;

    [Tooltip("Starting reserve ammo (outside of the magazine).")]
    [SerializeField] private int reserveAmmo = 60;

    // ---------------- Events ----------------
    public System.Action<int, int> OnAmmoChanged;          // (currentClip, reserveAmmo)
    public System.Action<UpgradePickup> OnUpgradeApplied;
    public System.Action<float> OnReloadStarted;           // reload time
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
            PlayerController player = GetComponent<PlayerController>();
            if (player != null && player.firePoint != null)
            {
                firePoint = player.firePoint;
                if (showDebugInfo) Debug.Log("[PlayerWeaponController] Using PlayerController's firePoint");
            }
            else
            {
                if (showDebugInfo) Debug.LogWarning("[PlayerWeaponController] No fire point assigned. Using this transform.");
                firePoint = transform;
            }
        }

        currentStats = weaponData.CreateRuntimeStats();
        currentAmmo = currentStats.GetTotalMagazineSize();

        OnAmmoChanged?.Invoke(currentAmmo, reserveAmmo);
    }

    // ---------------- FIRE ----------------
    public bool CanFire()
    {
        return !isReloading && currentAmmo > 0;
    }

    public float GetFireRate()
    {
        return currentStats.GetTotalFireRate();
    }

    public void Fire()
    {
        if (!CanFire())
        {
            // Try auto-reload if out of ammo but have reserve
            if (!isReloading && currentAmmo <= 0 && reserveAmmo > 0)
            {
                StartReload();
            }
            return;
        }

        currentAmmo--;

        if (audioSource != null && weaponData.fireSound != null)
            audioSource.PlayOneShot(weaponData.fireSound);

        if (weaponData.muzzleFlashPrefab != null)
        {
            GameObject flash = Instantiate(weaponData.muzzleFlashPrefab, firePoint.position, firePoint.rotation);
            Destroy(flash, 0.5f);
        }

        for (int i = 0; i < currentStats.bulletsPerShot; i++)
            FireProjectile(i);

        OnWeaponFired?.Invoke();
        OnAmmoChanged?.Invoke(currentAmmo, reserveAmmo);

        if (currentAmmo <= 0 && reserveAmmo > 0 && !isReloading)
            StartReload();
    }

    private void FireProjectile(int pelletIndex)
    {
        float spreadAngle = 0f;
        if (currentStats.bulletsPerShot > 1)
        {
            float spreadRange = currentStats.bulletSpread;
            float step = spreadRange / (currentStats.bulletsPerShot - 1);
            spreadAngle = -spreadRange / 2f + (step * pelletIndex);
        }
        else if (currentStats.bulletSpread > 0)
        {
            spreadAngle = Random.Range(-currentStats.bulletSpread / 2f, currentStats.bulletSpread / 2f);
        }

        Quaternion spreadRotation = Quaternion.Euler(0, spreadAngle, 0);
        Quaternion finalRotation = firePoint.rotation * spreadRotation;

        GameObject bulletObj = Instantiate(weaponData.bulletPrefab, firePoint.position, finalRotation);

        EnhancedBullet enhancedBullet = bulletObj.GetComponent<EnhancedBullet>();
        if (enhancedBullet != null)
        {
            // If we have a damage bonus, we need to apply it to the stats
            // Since EnhancedBullet.damage is private, we modify the stats before passing them
            if (damageBonus > 0f)
            {
                // Temporarily add bonus to base damage for this bullet
                float originalBaseDamage = currentStats.baseDamage;
                currentStats.baseDamage += damageBonus;
                enhancedBullet.InitializeWithStats(gameObject, currentStats);
                currentStats.baseDamage = originalBaseDamage; // Restore immediately
            }
            else
            {
                enhancedBullet.InitializeWithStats(gameObject, currentStats);
            }
        }
        else
        {
            Bullet legacyBullet = bulletObj.GetComponent<Bullet>();
            if (legacyBullet != null)
            {
                legacyBullet.Initialize(gameObject);
                // Apply base damage + bonus from weapon upgrade pickups
                legacyBullet.damage = currentStats.GetTotalDamage() + damageBonus;
                legacyBullet.speed = currentStats.GetTotalVelocity();
                legacyBullet.lifetime = currentStats.GetTotalLifetime();
            }
            else
            {
                IBullet basicBullet = bulletObj.GetComponent<IBullet>();
                if (basicBullet != null) basicBullet.Initialize(gameObject);
            }
        }
    }

    // ---------------- RELOAD ----------------
    public void TryReload()
    {
        if (!isReloading) StartReload();
    }

    public void StartReload()
    {
        if (isReloading)
            return;

        int magSize = currentStats.GetTotalMagazineSize();

        // Safer checks for valid reload conditions
        if (reserveAmmo <= 0)
        {
            if (showDebugInfo) Debug.Log("[WeaponController] Reload blocked: no reserve ammo available.");
            OnAmmoChanged?.Invoke(currentAmmo, reserveAmmo);
            return;
        }

        if (currentAmmo >= magSize)
        {
            if (showDebugInfo) Debug.Log("[WeaponController] Reload skipped: magazine already full.");
            OnAmmoChanged?.Invoke(currentAmmo, reserveAmmo);
            return;
        }

        isReloading = true;
        float reloadTime = currentStats.GetTotalReloadTime();
        OnReloadStarted?.Invoke(reloadTime);

        if (audioSource != null && weaponData.reloadSound != null)
            audioSource.PlayOneShot(weaponData.reloadSound);

        StartCoroutine(ReloadCoroutineSafe(reloadTime));
    }

    private IEnumerator ReloadCoroutineSafe(float reloadTime)
    {
        float elapsed = 0f;
        while (elapsed < reloadTime)
        {
            if (Time.timeScale > 0f)
                elapsed += Time.deltaTime;
            yield return null;
        }

        int magSize = currentStats.GetTotalMagazineSize();
        int needed = magSize - currentAmmo;
        int toLoad = Mathf.Min(needed, reserveAmmo);

        currentAmmo += toLoad;
        reserveAmmo -= toLoad;
        isReloading = false;

        if (showDebugInfo)
            Debug.Log($"[WeaponController] Reload complete: +{toLoad} → clip={currentAmmo}, reserve={reserveAmmo}");

        OnAmmoChanged?.Invoke(currentAmmo, reserveAmmo);
    }

    // ---------------- AMMO PICKUPS ----------------
    public bool AddAmmo(int amount)
    {
        if (amount <= 0) return false;

        int oldReserve = reserveAmmo;
        reserveAmmo = Mathf.Min(reserveAmmo + amount, maxReserveAmmo);
        int added = reserveAmmo - oldReserve;

        if (showDebugInfo)
            Debug.Log($"[PlayerWeaponController] Added {added} reserve ammo. Reserve: {reserveAmmo}/{maxReserveAmmo}");

        OnAmmoChanged?.Invoke(currentAmmo, reserveAmmo);

        // Auto-trigger reload if clip empty and now have reserve ammo
        if (added > 0 && currentAmmo <= 0 && !isReloading)
        {
            if (showDebugInfo) Debug.Log("[PlayerWeaponController] Auto-reloading after ammo pickup...");
            StartReload();
        }

        return added > 0;
    }

    // ---------------- UPGRADES ----------------
    public void ApplyUpgrade(UpgradePickup upgrade)
    {
        if (upgrade == null) return;

        upgrade.ApplyUpgrade(currentStats);
        appliedUpgrades.Add(upgrade);

        int newMagSize = currentStats.GetTotalMagazineSize();
        if (currentAmmo > newMagSize)
            currentAmmo = newMagSize;

        OnUpgradeApplied?.Invoke(upgrade);
        OnAmmoChanged?.Invoke(currentAmmo, reserveAmmo);

        if (showDebugInfo)
        {
            Debug.Log($"[PlayerWeaponController] ★ UPGRADE APPLIED: {upgrade.pickupName}\n" +
                      $"Damage: {currentStats.GetTotalDamage():F1} | FireRate: {currentStats.GetTotalFireRate():F2}/s | " +
                      $"Mag: {currentStats.GetTotalMagazineSize()} | Bullets/Shot: {currentStats.bulletsPerShot}");
        }
    }

    // ---------------- WEAPON UPGRADE PICKUPS ----------------
    public void AddDamageBonus(float bonus)
    {
        damageBonus += bonus;
        if (showDebugInfo)
            Debug.Log($"[PlayerWeaponController] Damage bonus increased by {bonus:F1}. Total bonus: {damageBonus:F1}");
    }

    // ---------------- GETTERS ----------------
    public List<UpgradePickup> GetAppliedUpgrades() => new List<UpgradePickup>(appliedUpgrades);
    public WeaponStats GetCurrentStats() => currentStats;
    public int GetCurrentAmmo() => currentAmmo;
    public int GetMaxAmmo() => currentStats.GetTotalMagazineSize();
    public int GetReserveAmmo() => reserveAmmo;
    public int GetMaxReserveAmmo() => maxReserveAmmo;
    public bool IsReloading() => isReloading;
    public float GetDamageBonus() => damageBonus;
    public float GetTotalDamage() => currentStats.GetTotalDamage() + damageBonus;
}

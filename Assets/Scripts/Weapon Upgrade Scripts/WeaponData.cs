using UnityEngine;

/// <summary>
/// Enhanced Weapon ScriptableObject that integrates with the upgrade system.
/// Defines base weapon properties that can be modified by upgrades.
/// </summary>
[CreateAssetMenu(fileName = "NewWeaponData", menuName = "Weapons/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Identity")]
    public string weaponName;
    public Sprite weaponIcon;
    
    [Header("Prefabs")]
    [Tooltip("The bullet prefab to instantiate")]
    public GameObject bulletPrefab;
    
    [Header("Base Stats")]
    [Tooltip("Base weapon statistics - these will be modified by upgrades")]
    public WeaponStats baseStats = new WeaponStats();
    
    [Header("Audio")]
    public AudioClip fireSound;
    public AudioClip reloadSound;
    public AudioClip emptySound;
    
    [Header("Visual Effects")]
    public GameObject muzzleFlashPrefab;
    public GameObject impactEffectPrefab;
    
    /// <summary>
    /// Creates a runtime instance of weapon stats that can be modified
    /// </summary>
    public WeaponStats CreateRuntimeStats()
    {
        return baseStats.Clone();
    }
}

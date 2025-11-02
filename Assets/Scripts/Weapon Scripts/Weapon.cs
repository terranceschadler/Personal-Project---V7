using UnityEngine;

[CreateAssetMenu(fileName = "NewWeapon", menuName = "Weapons/Weapon")]
public class Weapon : ScriptableObject
{
    public string weaponName;
    public GameObject bulletPrefab;
    public float fireRate = 0.5f;
    public float bulletSpeed = 20f;
    public int bulletsPerShot = 1; // e.g. 1 = normal gun, >1 = shotgun
    public float spreadAngle = 0f; // e.g. 0 = rifle, 10 = shotgun
}

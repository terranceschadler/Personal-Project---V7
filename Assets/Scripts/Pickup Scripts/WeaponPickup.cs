using UnityEngine;

public class WeaponPickup : MonoBehaviour
{
    public Weapon weapon;
    public float rotateSpeed = 90f;   // just to make it spin

    private void Update()
    {
        // Rotate for visual effect
        transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);
    }


    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerController player = other.GetComponent<PlayerController>();
            if (player != null)
            {
                player.EquipWeapon(weapon);
            }
            Destroy(gameObject); // Remove pickup after collection
        }
    }
}

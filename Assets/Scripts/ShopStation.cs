using UnityEngine;

public class ShopStation : MonoBehaviour
{
    [Header("Costs")]
    public int healthCost = 5;
    public int weaponCost = 10;
    public float healAmount = 50f;

    private bool playerInRange = false;
    private PlayerController player;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            player = other.GetComponent<PlayerController>();
            Debug.Log("Press [H] to buy health, [W] to buy weapon.");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            player = null;
            Debug.Log("Left shop area.");
        }
    }

    private void Update()
    {
        if (playerInRange && player != null)
        {
            // Buy Health
            if (Input.GetKeyDown(KeyCode.H))
            {
                if (GameManager.Instance.SpendCoins(healthCost))
                {
                    player.Heal(healAmount);
                    Debug.Log("Bought health!");
                }
            }

            // Buy Weapon
            if (Input.GetKeyDown(KeyCode.W))
            {
                if (GameManager.Instance.SpendCoins(weaponCost))
                {
                    player.GiveWeapon(); // You need to implement this in PlayerController
                    Debug.Log("Bought a weapon!");
                }
            }
        }
    }
}

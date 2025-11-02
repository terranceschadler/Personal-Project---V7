using UnityEngine;

public class BarricadeHealth : MonoBehaviour
{
    [Header("Health Settings")]
    public int maxHealth = 100;
    public GameObject destroyedPrefab; // Optional: debris / destroyed version

    private int currentHealth;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0) return;

        currentHealth -= amount;
        Debug.Log($"{gameObject.name} took {amount} damage, health = {currentHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        // Spawn destroyed version if assigned
        if (destroyedPrefab != null)
        {
            Instantiate(destroyedPrefab, transform.position, transform.rotation);
        }

        Destroy(gameObject);
    }
}

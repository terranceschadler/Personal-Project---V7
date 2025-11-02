using UnityEngine;
using System;  // Required for events

public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    private float currentHealth;

    // Event that is triggered when the health reaches zero
    public event Action OnDeath;

    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float damage)
    {
        currentHealth -= damage;
        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        // Trigger the OnDeath event (if there are any subscribers)
        OnDeath?.Invoke(); // Use the null-conditional operator to avoid errors

        // Optionally, destroy the GameObject immediately,
        // or perform other death-related actions
        Destroy(gameObject);  //Or disable the components

    }
}

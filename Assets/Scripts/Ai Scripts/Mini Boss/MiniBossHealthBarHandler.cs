// Assets/Scripts/Ai Scripts/Mini Boss/MiniBossHealthBarHandler.cs
using UnityEngine;

/// <summary>
/// Companion script that hooks into EnemyController damage events
/// to trigger mini boss health bar visibility when taking damage
/// </summary>
[RequireComponent(typeof(MiniBoss))]
[RequireComponent(typeof(EnemyController))]
public class MiniBossHealthBarHandler : MonoBehaviour
{
    private MiniBoss _miniBoss;
    private EnemyController _enemyController;
    private float _lastKnownHealth = -1f;

    void Awake()
    {
        _miniBoss = GetComponent<MiniBoss>();
        _enemyController = GetComponent<EnemyController>();
    }

    void Start()
    {
        if (_enemyController != null)
        {
            _lastKnownHealth = GetCurrentHealth();
        }
    }

    void Update()
    {
        // Check if health decreased (mini boss took damage)
        if (_enemyController != null && _miniBoss != null)
        {
            float currentHealth = GetCurrentHealth();

            if (currentHealth < _lastKnownHealth)
            {
                // Health decreased - mini boss took damage
                _miniBoss.OnMiniBossDamaged();
            }

            _lastKnownHealth = currentHealth;
        }
    }

    private float GetCurrentHealth()
    {
        // Use reflection to access protected currentHealth field
        var field = typeof(EnemyController).GetField("currentHealth",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);

        if (field != null)
            return (float)field.GetValue(_enemyController);

        return 0f;
    }
}

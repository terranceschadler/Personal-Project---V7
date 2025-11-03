// MiniBossDeathHandler.cs
// Add this script to any mini boss that has the MiniBoss component
// It ensures loot drops when the enemy dies
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(MiniBoss))]
public class MiniBossDeathHandler : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("The EnemyController on this object (auto-detected if left empty)")]
    public EnemyController enemyController;

    [Header("Debug")]
    [Tooltip("Enable verbose logging")]
    public bool debugLogs = false;

    [Header("Manual Trigger (Optional)")]
    [Tooltip("If your death system uses UnityEvents, hook this up in the Inspector")]
    public UnityEvent onManualDeathTrigger;

    private MiniBoss _miniBoss;
    private bool _lootTriggered = false;
    private bool _deathEventHooked = false;

    void Awake()
    {
        _miniBoss = GetComponent<MiniBoss>();
        
        if (_miniBoss == null)
        {
            Debug.LogError($"[MiniBossDeathHandler] No MiniBoss component found on '{gameObject.name}'!", gameObject);
            enabled = false;
            return;
        }

        if (enemyController == null)
        {
            enemyController = GetComponent<EnemyController>();
        }

        if (debugLogs)
            Debug.Log($"[MiniBossDeathHandler] Initialized on '{gameObject.name}'", gameObject);
        
        // Try to hook into the death event
        TryHookDeathEvent();
    }

    void Start()
    {
        // Second attempt to hook death event after all components are initialized
        if (!_deathEventHooked)
        {
            TryHookDeathEvent();
        }
    }

    private void TryHookDeathEvent()
    {
        if (_deathEventHooked) return;

        if (enemyController != null)
        {
            // Try to find and subscribe to the OnDeath event
            var type = enemyController.GetType();
            var onDeathField = type.GetField("OnDeath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            
            if (onDeathField != null && onDeathField.FieldType == typeof(UnityEvent))
            {
                UnityEvent deathEvent = (UnityEvent)onDeathField.GetValue(enemyController);
                if (deathEvent != null)
                {
                    deathEvent.AddListener(OnEnemyDeath);
                    _deathEventHooked = true;
                    if (debugLogs)
                        Debug.Log($"[MiniBossDeathHandler] Successfully hooked OnDeath event on '{gameObject.name}'", gameObject);
                    return;
                }
            }
        }

        // Silent fallback - this is fine, EnemyController.Die() will call it
    }

    void Update()
    {
        // Fallback: Check for death every frame if event hook failed
        if (_lootTriggered || _deathEventHooked) return;

        // Check if health component exists and has a getter method
        if (enemyController != null)
        {
            try
            {
                // Try to call GetCurrentHealth() if it exists
                var getCurrentHealth = enemyController.GetType().GetMethod("GetCurrentHealth");
                if (getCurrentHealth != null)
                {
                    float health = (float)getCurrentHealth.Invoke(enemyController, null);
                    if (health <= 0f)
                    {
                        TriggerLootDrop();
                        return;
                    }
                }
            }
            catch
            {
                // Method doesn't exist or failed, continue with other checks
            }

            // Check if enemy controller is disabled (but object is still active - means death)
            if (!enemyController.enabled && gameObject.activeInHierarchy)
            {
                TriggerLootDrop();
            }
        }
    }

    private void TriggerLootDrop()
    {
        if (_lootTriggered) return;
        
        // Safety check: Don't spawn loot during scene unload or in editor when not playing
        if (!Application.isPlaying) return;
        if (!gameObject.scene.isLoaded) return;
        
        _lootTriggered = true;

        if (_miniBoss == null) return;

        if (debugLogs)
            Debug.Log($"[MiniBossDeathHandler] ★★★ TRIGGERING LOOT DROP on '{gameObject.name}' ★★★", gameObject);
        _miniBoss.NotifyKilled();
    }

    // Public method that can be called from other scripts, UnityEvents, or SendMessage
    public void OnEnemyDeath()
    {
        if (debugLogs)
            Debug.Log($"[MiniBossDeathHandler] OnEnemyDeath() called on '{gameObject.name}'", gameObject);
        TriggerLootDrop();
    }

    // Alternative method names for compatibility
    public void OnDeath() { OnEnemyDeath(); }
    public void Died() { OnEnemyDeath(); }
    public void HandleDeath() { OnEnemyDeath(); }
}

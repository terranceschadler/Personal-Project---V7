using UnityEngine;
using System.Collections;

/// <summary>
/// Burn DoT effect component
/// </summary>
public class BurnEffect : MonoBehaviour
{
    private float damagePerSecond;
    private float remainingDuration;
    private GameObject source;
    private Coroutine burnCoroutine;
    
    public void ApplyBurn(float dps, float duration, GameObject damageSource)
    {
        damagePerSecond = Mathf.Max(damagePerSecond, dps); // Take highest DPS if multiple burns
        remainingDuration = Mathf.Max(remainingDuration, duration); // Extend duration
        source = damageSource;
        
        if (burnCoroutine == null)
        {
            burnCoroutine = StartCoroutine(BurnCoroutine());
        }
    }
    
    private IEnumerator BurnCoroutine()
    {
        while (remainingDuration > 0)
        {
            yield return new WaitForSeconds(1f);
            remainingDuration -= 1f;
            
            // Apply damage
            var enemy = GetComponent<EnemyController>();
            if (enemy != null)
            {
                enemy.TakeDamage(damagePerSecond);
            }
            
            var boss = GetComponent<BossEnemy>();
            if (boss != null)
            {
                boss.gameObject.SendMessage("ApplyDamageFrom", (damagePerSecond, source), SendMessageOptions.DontRequireReceiver);
            }
            
            // Visual feedback (add fire particles here)
        }
        
        burnCoroutine = null;
        Destroy(this);
    }
}

/// <summary>
/// Poison DoT effect component
/// </summary>
public class PoisonEffect : MonoBehaviour
{
    private float damagePerSecond;
    private float remainingDuration;
    private GameObject source;
    private Coroutine poisonCoroutine;
    
    public void ApplyPoison(float dps, float duration, GameObject damageSource)
    {
        damagePerSecond = Mathf.Max(damagePerSecond, dps);
        remainingDuration = Mathf.Max(remainingDuration, duration);
        source = damageSource;
        
        if (poisonCoroutine == null)
        {
            poisonCoroutine = StartCoroutine(PoisonCoroutine());
        }
    }
    
    private IEnumerator PoisonCoroutine()
    {
        while (remainingDuration > 0)
        {
            yield return new WaitForSeconds(1f);
            remainingDuration -= 1f;
            
            // Apply damage
            var enemy = GetComponent<EnemyController>();
            if (enemy != null)
            {
                enemy.TakeDamage(damagePerSecond);
            }
            
            var boss = GetComponent<BossEnemy>();
            if (boss != null)
            {
                boss.gameObject.SendMessage("ApplyDamageFrom", (damagePerSecond, source), SendMessageOptions.DontRequireReceiver);
            }
            
            // Visual feedback (add poison particles here)
        }
        
        poisonCoroutine = null;
        Destroy(this);
    }
}

/// <summary>
/// Freeze slow effect component
/// </summary>
public class FreezeEffect : MonoBehaviour
{
    private float slowPercent;
    private float remainingDuration;
    private float originalSpeed;
    private bool isActive;
    private Coroutine freezeCoroutine;
    
    public void ApplyFreeze(float slow, float duration)
    {
        slowPercent = Mathf.Max(slowPercent, slow);
        remainingDuration = Mathf.Max(remainingDuration, duration);
        
        if (freezeCoroutine == null)
        {
            freezeCoroutine = StartCoroutine(FreezeCoroutine());
        }
    }
    
    private IEnumerator FreezeCoroutine()
    {
        // Store original speed
        var enemy = GetComponent<EnemyController>();
        if (enemy != null)
        {
            // Assuming EnemyController has a speed field - adjust as needed
            // originalSpeed = enemy.moveSpeed;
            // enemy.moveSpeed *= (1f - slowPercent);
        }
        
        isActive = true;
        
        while (remainingDuration > 0)
        {
            yield return new WaitForSeconds(0.1f);
            remainingDuration -= 0.1f;
            
            // Visual feedback (add ice particles here)
        }
        
        // Restore original speed
        if (enemy != null)
        {
            // enemy.moveSpeed = originalSpeed;
        }
        
        isActive = false;
        freezeCoroutine = null;
        Destroy(this);
    }
}

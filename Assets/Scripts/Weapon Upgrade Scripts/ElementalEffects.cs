using UnityEngine;
using System.Collections;
using UnityEngine.AI;

/// <summary>
/// Burn DoT effect
/// </summary>
public class BurnEffect : MonoBehaviour
{
    private float damagePerSecond;
    private float remainingDuration;
    private GameObject source;
    private Coroutine burnCoroutine;

    public void ApplyBurn(float dps, float duration, GameObject damageSource)
    {
        damagePerSecond = Mathf.Max(damagePerSecond, dps);
        remainingDuration = Mathf.Max(remainingDuration, duration);
        source = damageSource;

        if (burnCoroutine == null)
            burnCoroutine = StartCoroutine(BurnCoroutine());
    }

    private IEnumerator BurnCoroutine()
    {
        while (remainingDuration > 0)
        {
            yield return new WaitForSeconds(1f);
            remainingDuration -= 1f;

            var enemy = GetComponent<EnemyController>();
            if (enemy != null)
                enemy.TakeDamage(damagePerSecond);

            var boss = GetComponent<BossEnemy>();
            if (boss != null)
                boss.gameObject.SendMessage("ApplyDamageFrom", (damagePerSecond, source), SendMessageOptions.DontRequireReceiver);
        }

        burnCoroutine = null;
        Destroy(this);
    }
}

/// <summary>
/// Poison DoT effect
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
            poisonCoroutine = StartCoroutine(PoisonCoroutine());
    }

    private IEnumerator PoisonCoroutine()
    {
        while (remainingDuration > 0)
        {
            yield return new WaitForSeconds(1f);
            remainingDuration -= 1f;

            var enemy = GetComponent<EnemyController>();
            if (enemy != null)
                enemy.TakeDamage(damagePerSecond);

            var boss = GetComponent<BossEnemy>();
            if (boss != null)
                boss.gameObject.SendMessage("ApplyDamageFrom", (damagePerSecond, source), SendMessageOptions.DontRequireReceiver);
        }

        poisonCoroutine = null;
        Destroy(this);
    }
}

/// <summary>
/// Freeze slow effect — uses NavMeshAgent (correct for your EnemyController)
/// </summary>
public class FreezeEffect : MonoBehaviour
{
    private float slowPercent;
    private float remainingDuration;

    private float originalSpeed;
    private float originalAccel;
    private float originalAngularSpeed;

    private bool isActive = false;
    private Coroutine freezeCoroutine;

    public bool IsActive => isActive;

    public void ApplyFreeze(float slow, float duration)
    {
        slowPercent = Mathf.Max(slowPercent, slow);
        remainingDuration = Mathf.Max(remainingDuration, duration);

        if (freezeCoroutine == null)
            freezeCoroutine = StartCoroutine(FreezeCoroutine());
    }

    private IEnumerator FreezeCoroutine()
    {
        NavMeshAgent agent = GetComponent<NavMeshAgent>();

        if (agent != null)
        {
            originalSpeed = agent.speed;
            originalAccel = agent.acceleration;
            originalAngularSpeed = agent.angularSpeed;

            float multiplier = (1f - slowPercent);

            agent.speed = originalSpeed * multiplier;
            agent.acceleration = originalAccel * multiplier;
            agent.angularSpeed = originalAngularSpeed * multiplier;
        }

        isActive = true;

        while (remainingDuration > 0)
        {
            yield return new WaitForSeconds(0.1f);
            remainingDuration -= 0.1f;
        }

        if (agent != null)
        {
            agent.speed = originalSpeed;
            agent.acceleration = originalAccel;
            agent.angularSpeed = originalAngularSpeed;
        }

        isActive = false;
        freezeCoroutine = null;
        Destroy(this);
    }
}

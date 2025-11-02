using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class ShockwaveBoss : BossEnemy
{
    [Header("Shockwave Settings")]
    [SerializeField] private int waves = 3;
    [SerializeField] private float waveInterval = 0.25f;
    [SerializeField] private float maxRadius = 12f;
    [SerializeField] private float baseDamage = 25f;
    [SerializeField] private float minDamage = 8f;
    [SerializeField] private float knockbackForce = 12f;
    [SerializeField] private float upwardsModifier = 0.5f;
    [SerializeField] private LayerMask affectedLayers = ~0;

    [Header("FX (optional)")]
    [SerializeField] private GameObject slamVfxPrefab;
    [SerializeField] private GameObject ringVfxPrefab;

    private readonly HashSet<Transform> _touchedThisWave = new HashSet<Transform>();

    /// <summary>
    /// Override: emits expanding shockwaves
    /// </summary>
    protected override void SpecialAttack()
    {
        if (slamVfxPrefab)
            Instantiate(slamVfxPrefab, transform.position, Quaternion.identity);

        StartCoroutine(EmitShockwaves());
    }

    private IEnumerator EmitShockwaves()
    {
        for (int i = 0; i < Mathf.Max(1, waves); i++)
        {
            float t = (i + 1) / (float)waves;
            float radius = Mathf.Lerp(maxRadius / waves, maxRadius, t);

            if (ringVfxPrefab)
            {
                var v = Instantiate(ringVfxPrefab, transform.position, Quaternion.identity);
                v.transform.localScale = new Vector3(radius * 2f, 1f, radius * 2f);
            }

            DoShockwaveRing(radius);

            if (i < waves - 1)
                yield return new WaitForSeconds(waveInterval);
        }
    }

    private void DoShockwaveRing(float radius)
    {
        _touchedThisWave.Clear();

        Collider[] hits = Physics.OverlapSphere(transform.position, radius, affectedLayers, QueryTriggerInteraction.Ignore);
        foreach (var col in hits)
        {
            Transform root = col.attachedRigidbody ? col.attachedRigidbody.transform : col.transform;
            if (root == transform) continue;
            if (_touchedThisWave.Contains(root)) continue;
            _touchedThisWave.Add(root);

            float dist = Vector3.Distance(transform.position, root.position);
            float falloff = 1f - Mathf.Clamp01(dist / radius);
            float dmg = Mathf.Lerp(minDamage, baseDamage, falloff);

            // Apply damage if target has matching method
            root.SendMessage("ApplyDamage", dmg, SendMessageOptions.DontRequireReceiver);

            if (root.TryGetComponent<Rigidbody>(out var rb))
                rb.AddExplosionForce(knockbackForce, transform.position, radius, upwardsModifier, ForceMode.Impulse);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ArtilleryBoss : BossEnemy
{
    public enum AimMode { RingAroundTarget, DirectAtTarget, PredictiveLead }

    [Header("Artillery Settings")]
    [SerializeField] private Transform firePoint;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private int shotsPerVolley = 3;
    [SerializeField] private float launchSpeed = 20f;
    [SerializeField] private float staggeredDelay = 0.1f;

    [Header("Aiming")]
    [SerializeField] private AimMode aimMode = AimMode.PredictiveLead;
    [Tooltip("Radius used only by RingAroundTarget mode.")]
    [SerializeField] private float targetRingRadius = 8f;
    [Tooltip("Cosmetic height of computed target point (BossBullet flies flat anyway).")]
    [SerializeField] private float targetRingHeight = 1f;
    [Tooltip("Small horizontal spread (deg) applied to each shot.")]
    [SerializeField] private float spreadDegrees = 2f;

    [Header("Artillery Audio")]
    [SerializeField] private AudioClip artilleryShootClip;
    [Range(0f, 1f)] [SerializeField] private float artilleryShootVolume = 0.9f;

    // non-serialized cache
    private AudioSource _cachedSfx;

    private AudioSource SfxSource
    {
        get
        {
            if (_cachedSfx != null) return _cachedSfx;
            _cachedSfx = GetComponent<AudioSource>();
            if (_cachedSfx == null)
                _cachedSfx = GetComponentInChildren<AudioSource>(true);
            return _cachedSfx;
        }
    }

    // Example coroutine to fire a volley
    public IEnumerator FireVolley(Transform target)
    {
        if (projectilePrefab == null || firePoint == null || target == null)
            yield break;

        for (int i = 0; i < shotsPerVolley; i++)
        {
            FireSingle(target);
            PlayArtilleryShootSfx();

            if (staggeredDelay > 0f)
                yield return new WaitForSeconds(staggeredDelay);
        }
    }

    private void FireSingle(Transform target)
    {
        Vector3 targetPos = ComputeAimPoint(target);
        Vector3 dir = (targetPos - firePoint.position).normalized;

        // Apply small random spread
        dir = Quaternion.Euler(0f, Random.Range(-spreadDegrees, spreadDegrees), 0f) * dir;

        GameObject proj = Instantiate(projectilePrefab, firePoint.position, Quaternion.LookRotation(dir, Vector3.up));
        Rigidbody rb = proj.GetComponent<Rigidbody>();
        if (rb != null)
            rb.linearVelocity = dir * launchSpeed;
    }

    private Vector3 ComputeAimPoint(Transform target)
    {
        Vector3 tgt = target.position;

        switch (aimMode)
        {
            case AimMode.RingAroundTarget:
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * targetRingRadius;
                tgt = target.position + offset + Vector3.up * targetRingHeight;
                break;

            case AimMode.DirectAtTarget:
                tgt = target.position;
                break;

            case AimMode.PredictiveLead:
                Rigidbody trgRb = target.GetComponent<Rigidbody>();
                if (trgRb != null)
                {
                    Vector3 toTarget = target.position - firePoint.position;
                    float t = toTarget.magnitude / launchSpeed;
                    tgt = target.position + trgRb.linearVelocity * t;
                }
                break;
        }

        return tgt;
    }

    private void PlayArtilleryShootSfx()
    {
        if (artilleryShootClip == null) return;
        var src = SfxSource;
        if (src != null)
            src.PlayOneShot(artilleryShootClip, artilleryShootVolume);
    }
}

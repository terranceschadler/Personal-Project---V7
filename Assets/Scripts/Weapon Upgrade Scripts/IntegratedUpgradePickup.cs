using UnityEngine;

/// <summary>
/// Integrated upgrade pickup that bridges the archetype system with the weapon system.
/// This pickup shows up in the game world and triggers the upgrade selection UI.
/// </summary>
[RequireComponent(typeof(Collider))]
public class IntegratedUpgradePickup : MonoBehaviour
{
    [Header("Pickup Configuration")]
    public UpgradeTier tier = UpgradeTier.Common;

    [Header("Visual Settings")]
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private ParticleSystem particles;
    [SerializeField] private Light glowLight;

    [Header("Movement")]
    [SerializeField] private float rotationSpeed = 90f;
    [SerializeField] private float bobSpeed = 2f;
    [SerializeField] private float bobHeight = 0.3f;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip pickupSound;

    private IntegratedUpgradeSystem upgradeSystem;
    private Vector3 startPosition;
    private bool collected = false;
    private Collider col;

    public void Initialize(IntegratedUpgradeSystem system, UpgradeTier upgradeTier)
    {
        upgradeSystem = system;
        tier = upgradeTier;

        startPosition = transform.position;

        col = GetComponent<Collider>();
        if (col == null)
            col = gameObject.AddComponent<SphereCollider>();

        col.isTrigger = true;

        ApplyTierVisuals();
    }

    private void Start()
    {
        startPosition = transform.position;

        col = GetComponent<Collider>();
        if (col != null)
            col.isTrigger = true;

        // Auto-find via Unity 6 API
        if (upgradeSystem == null)
        {
            upgradeSystem = Object.FindFirstObjectByType<IntegratedUpgradeSystem>();

            if (upgradeSystem != null)
                Debug.Log("[IntegratedUpgradePickup] Auto-found IntegratedUpgradeSystem");
            else
                Debug.LogWarning("[IntegratedUpgradePickup] No IntegratedUpgradeSystem found in scene! Pickup won't work.");
        }

        ApplyTierVisuals();
    }

    private void Update()
    {
        if (collected) return;

        // Rotate
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);

        // Bob
        float newY = startPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }

    private void ApplyTierVisuals()
    {
        Color tierColor = GetTierColor();

        if (meshRenderer != null)
        {
            meshRenderer.material.color = tierColor;

            // Add emissive glow for higher tiers
            if (tier >= UpgradeTier.Rare)
            {
                meshRenderer.material.EnableKeyword("_EMISSION");
                meshRenderer.material.SetColor("_EmissionColor", tierColor * 0.5f);
            }
        }

        if (particles != null)
        {
            var main = particles.main;
            main.startColor = tierColor;
        }

        if (glowLight != null)
        {
            glowLight.color = tierColor;
            glowLight.intensity = 1f + (int)tier * 0.5f;
            glowLight.range = 3f + (int)tier;
        }
    }

    private Color GetTierColor()
    {
        return tier switch
        {
            UpgradeTier.Common => new Color(0.8f, 0.8f, 0.8f),
            UpgradeTier.Rare => new Color(0.2f, 0.5f, 1f),
            UpgradeTier.Epic => new Color(0.6f, 0.2f, 0.9f),
            UpgradeTier.Legendary => new Color(1f, 0.7f, 0.1f),
            _ => Color.white
        };
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!collected && other.CompareTag("Player"))
            CollectPickup();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!collected && other.CompareTag("Player"))
            CollectPickup();
    }

    private void CollectPickup()
    {
        collected = true;

        if (upgradeSystem == null)
        {
            Debug.Log("[IntegratedUpgradePickup] Upgrade system missing, searching...");
            upgradeSystem = Object.FindFirstObjectByType<IntegratedUpgradeSystem>();
        }

        if (pickupSound != null)
        {
            if (audioSource != null)
                audioSource.PlayOneShot(pickupSound);
            else
                AudioSource.PlayClipAtPoint(pickupSound, transform.position);
        }

        if (upgradeSystem != null)
        {
            Debug.Log($"[IntegratedUpgradePickup] Collected {tier} upgrade ? triggering system");
            upgradeSystem.OnPickupCollected(this);
        }
        else
        {
            Debug.LogError("[IntegratedUpgradePickup] No upgrade system found! Cannot show upgrade UI.");
            collected = false;
            return;
        }

        if (particles != null)
        {
            particles.Stop();
            var burst = Instantiate(particles, transform.position, Quaternion.identity);
            burst.Play();
            Destroy(burst.gameObject, 2f);
        }

        if (meshRenderer != null) meshRenderer.enabled = false;
        if (glowLight != null) glowLight.enabled = false;

        Destroy(gameObject, 0.5f);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = GetTierColor();
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}

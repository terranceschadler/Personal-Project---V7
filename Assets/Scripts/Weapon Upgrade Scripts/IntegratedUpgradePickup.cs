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
        
        // Setup collider
        col = GetComponent<Collider>();
        if (col == null)
        {
            col = gameObject.AddComponent<SphereCollider>();
        }
        col.isTrigger = true;
        
        // Apply tier-based visuals
        ApplyTierVisuals();
    }
    
    private void Start()
    {
        startPosition = transform.position;
        
        if (col == null)
        {
            col = GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;
        }
        
        // Auto-find IntegratedUpgradeSystem if not assigned
        if (upgradeSystem == null)
        {
            upgradeSystem = FindObjectOfType<IntegratedUpgradeSystem>();
            
            if (upgradeSystem != null)
            {
                Debug.Log("[IntegratedUpgradePickup] Auto-found IntegratedUpgradeSystem");
            }
            else
            {
                Debug.LogWarning("[IntegratedUpgradePickup] No IntegratedUpgradeSystem found in scene! Pickup won't work.");
            }
        }
        
        ApplyTierVisuals();
    }
    
    private void Update()
    {
        if (collected) return;
        
        // Rotate
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        
        // Bob up and down
        float newY = startPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }
    
    private void ApplyTierVisuals()
    {
        Color tierColor = GetTierColor();
        
        // Apply color to mesh
        if (meshRenderer != null)
        {
            meshRenderer.material.color = tierColor;
            
            // Add emission for higher tiers
            if (tier >= UpgradeTier.Rare)
            {
                meshRenderer.material.EnableKeyword("_EMISSION");
                meshRenderer.material.SetColor("_EmissionColor", tierColor * 0.5f);
            }
        }
        
        // Configure particles
        if (particles != null)
        {
            var main = particles.main;
            main.startColor = tierColor;
        }
        
        // Configure glow light
        if (glowLight != null)
        {
            glowLight.color = tierColor;
            glowLight.intensity = 1f + (int)tier * 0.5f;
            glowLight.range = 3f + (int)tier;
        }
    }
    
    private Color GetTierColor()
    {
        switch (tier)
        {
            case UpgradeTier.Common:
                return new Color(0.8f, 0.8f, 0.8f); // Gray/White
            case UpgradeTier.Rare:
                return new Color(0.2f, 0.5f, 1f); // Blue
            case UpgradeTier.Epic:
                return new Color(0.6f, 0.2f, 0.9f); // Purple
            case UpgradeTier.Legendary:
                return new Color(1f, 0.7f, 0.1f); // Gold
            default:
                return Color.white;
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (collected) return;
        
        if (other.CompareTag("Player"))
        {
            CollectPickup();
        }
    }
    
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected) return;
        
        if (other.CompareTag("Player"))
        {
            CollectPickup();
        }
    }
    
    private void CollectPickup()
    {
        collected = true;
        
        // Try to find upgrade system if missing
        if (upgradeSystem == null)
        {
            Debug.Log("[IntegratedUpgradePickup] Upgrade system missing, searching...");
            upgradeSystem = FindObjectOfType<IntegratedUpgradeSystem>();
        }
        
        // Play sound
        if (pickupSound != null)
        {
            if (audioSource != null)
            {
                audioSource.PlayOneShot(pickupSound);
            }
            else
            {
                AudioSource.PlayClipAtPoint(pickupSound, transform.position);
            }
        }
        
        // Trigger upgrade system
        if (upgradeSystem != null)
        {
            Debug.Log($"[IntegratedUpgradePickup] Collected {tier} tier upgrade, triggering system");
            upgradeSystem.OnPickupCollected(this);
        }
        else
        {
            Debug.LogError("[IntegratedUpgradePickup] No IntegratedUpgradeSystem found in scene! Cannot show upgrade options. " +
                          "Make sure IntegratedUpgradeSystem component exists in your scene.");
            // Don't destroy yet, maybe player can find another way to collect
            collected = false;
            return;
        }
        
        // Visual feedback
        if (particles != null)
        {
            particles.Stop();
            ParticleSystem burstParticles = Instantiate(particles, transform.position, Quaternion.identity);
            burstParticles.Play();
            Destroy(burstParticles.gameObject, 2f);
        }
        
        // Hide visual
        if (meshRenderer != null)
            meshRenderer.enabled = false;
        
        if (glowLight != null)
            glowLight.enabled = false;
        
        // Destroy after short delay (allow sound to play)
        Destroy(gameObject, 0.5f);
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.color = GetTierColor();
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}

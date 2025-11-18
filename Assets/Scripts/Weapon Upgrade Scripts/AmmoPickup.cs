using UnityEngine;

/// <summary>
/// Reserve-aware ammo pickup with extensive debugging
/// Adds to RESERVE ammo via PlayerWeaponController.AddAmmo()
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class AmmoPickup : MonoBehaviour
{
    [Header("Ammo")]
    public int ammoAmount = 10;
    public bool usePercentage = false;
    [Range(0f, 1f)] public float ammoPercentage = 0.5f;

    [Header("Lifetime")]
    public float lifetime = 30f;

    [Header("Pickup Trigger")]
    public Collider pickupTrigger;

    [Header("Physics")]
    public bool freezeRotation = true;

    [Header("Debug")]
    public bool showDebugLogs = true;
    public bool showScreenDebug = true;

    private bool collected;
    private static int totalCollected = 0;

    void Awake()
    {
        // Rigidbody config
        var rb = GetComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        if (freezeRotation) rb.constraints = RigidbodyConstraints.FreezeRotation;

        // Solid collider for physics
        var solid = GetComponent<Collider>();
        solid.isTrigger = false;

        // Ensure trigger for pickup
        if (pickupTrigger == null)
        {
            var trigger = gameObject.AddComponent<SphereCollider>();
            var sc = (SphereCollider)trigger;
            sc.radius = Mathf.Max(sc.radius, 0.6f);
            trigger.isTrigger = true;
            pickupTrigger = trigger;

            if (showDebugLogs)
                Debug.Log("[AmmoPickup] Created trigger collider");
        }

        if (showDebugLogs)
            Debug.Log($"[AmmoPickup] Awake complete. Trigger: {pickupTrigger != null}");
    }

    void Start()
    {
        if (lifetime > 0f)
        {
            Destroy(gameObject, lifetime);
            if (showDebugLogs)
                Debug.Log($"[AmmoPickup] Will despawn in {lifetime}s");
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (collected)
        {
            if (showDebugLogs)
                Debug.Log("[AmmoPickup] Already collected, ignoring");
            return;
        }

        if (showDebugLogs)
            Debug.Log($"[AmmoPickup] ═══ TRIGGER ENTER ═══");
        
        if (showDebugLogs)
            Debug.Log($"[AmmoPickup] Other: {other.name} (tag: '{other.tag}')");

        // Find PlayerWeaponController
        PlayerWeaponController weapon = null;

        if (other.attachedRigidbody != null)
        {
            weapon = other.attachedRigidbody.GetComponentInParent<PlayerWeaponController>();
            if (showDebugLogs && weapon != null)
                Debug.Log($"[AmmoPickup] Found weapon via attachedRigidbody: {other.attachedRigidbody.name}");
        }

        if (weapon == null)
        {
            weapon = other.GetComponentInParent<PlayerWeaponController>();
            if (showDebugLogs && weapon != null)
                Debug.Log($"[AmmoPickup] Found weapon via GetComponentInParent");
        }

        if (weapon == null)
        {
            if (showDebugLogs)
                Debug.Log($"[AmmoPickup] ✗ No PlayerWeaponController found on {other.name}");
            return;
        }

        if (showDebugLogs)
            Debug.Log($"[AmmoPickup] ✓ PlayerWeaponController found!");

        Collect(weapon);
    }

    private void Collect(PlayerWeaponController weapon)
    {
        if (collected || weapon == null)
        {
            if (showDebugLogs)
                Debug.Log($"[AmmoPickup] Collect blocked: collected={collected}, weapon={weapon != null}");
            return;
        }

        // Check reserve capacity
        int reserve = weapon.GetReserveAmmo();
        int maxReserve = weapon.GetMaxReserveAmmo();

        if (showDebugLogs)
            Debug.Log($"[AmmoPickup] Reserve: {reserve}/{maxReserve}");

        if (reserve >= maxReserve)
        {
            if (showDebugLogs)
                Debug.Log("[AmmoPickup] ✗ Reserve full! Pickup stays in world.");
            return;
        }

        // Calculate amount to add
        int toAdd = usePercentage
            ? Mathf.CeilToInt(maxReserve * Mathf.Clamp01(ammoPercentage))
            : Mathf.Max(0, ammoAmount);

        if (toAdd <= 0)
        {
            if (showDebugLogs)
                Debug.LogWarning("[AmmoPickup] toAdd is 0 or less!");
            return;
        }

        if (showDebugLogs)
            Debug.Log($"[AmmoPickup] Attempting to add {toAdd} to reserve...");

        collected = true;

        // Hide visual immediately
        var renderer = GetComponentInChildren<Renderer>();
        if (renderer != null)
            renderer.enabled = false;

        // Add to reserve
        bool added = weapon.AddAmmo(toAdd);

        if (added)
        {
            totalCollected++;

            if (showDebugLogs)
                Debug.Log($"[AmmoPickup] ✓✓✓ SUCCESS! Added {toAdd} reserve ammo");

            Debug.Log($"<color=yellow>+{toAdd} RESERVE AMMO! (Total pickups: {totalCollected})</color>");

            // Destroy immediately
            Destroy(gameObject);
        }
        else
        {
            if (showDebugLogs)
                Debug.LogWarning("[AmmoPickup] ✗ AddAmmo returned false");

            // Show visual again
            if (renderer != null)
                renderer.enabled = true;

            collected = false;
        }
    }

    void OnGUI()
    {
        if (!showScreenDebug) return;

        // Show pickup status
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 screenPos = cam.WorldToScreenPoint(transform.position);

        if (screenPos.z > 0)
        {
            screenPos.y = Screen.height - screenPos.y;

            string status = collected ? "COLLECTED" : "READY";
            GUI.color = collected ? Color.red : Color.green;
            GUI.Label(new Rect(screenPos.x - 50, screenPos.y - 40, 100, 20), status);
            GUI.color = Color.white;
        }

        // Corner stats
        GUI.Label(new Rect(10, Screen.height - 60, 400, 20), 
            $"<color=yellow>Ammo Pickups Collected: {totalCollected}</color>");
        GUI.Label(new Rect(10, Screen.height - 40, 400, 20), 
            $"<color=cyan>Active Pickups: {FindObjectsOfType<AmmoPickup>().Length}</color>");
    }
}

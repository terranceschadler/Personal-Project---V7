// Assets/Scripts/AI/SpawnFriendlyAfterMapComplete.cs
using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class SpawnFriendlyAfterMapComplete : MonoBehaviour
{
    [Header("References")]
    public GameObject friendlyAIPrefab;   // Assign in inspector
    public Transform spawnPoint;          // Assign in inspector (or this.transform as fallback)

    [Header("Settings")]
    [Tooltip("Tag your player object with this tag.")]
    public string playerTag = "Player";

    private RandomMapGenerator rmg;

    // State
    private bool mapCompleted = false;
    private bool spawnerActive = false;   // Visible/touchable now?
    private GameObject currentFriendly;   // This spawner owns exactly one at a time

    // Cached components
    private Collider triggerCol;
    private Renderer[] renderers;
    private MapKeyArea mapArea;           // local identity for the map

    private enum SpawnerPhase { LockedUntilMapDone, ReadyForTouch, CoolingWhileFriendlyAlive }
    private SpawnerPhase phase = SpawnerPhase.LockedUntilMapDone;

    // Map registration state (localized)
    private bool mapIconRegistered = false;
    private bool lastVisibleToMap = false;
    private float visPollTimer = 0f;
    private const float VIS_POLL_INTERVAL = 0.25f;

    private void Awake()
    {
        triggerCol = GetComponent<Collider>();
        if (!triggerCol.isTrigger)
        {
            triggerCol.isTrigger = true;
            Debug.LogWarning($"{name}: Collider was not set as Trigger. Auto-set to Trigger.");
        }

        renderers = GetComponentsInChildren<Renderer>(true);
        if (spawnPoint == null) spawnPoint = transform; // fallback

        // Ensure we have a MapKeyArea so the controller can represent us
        mapArea = GetComponent<MapKeyArea>();
        if (mapArea == null) mapArea = gameObject.AddComponent<MapKeyArea>();
        if (string.IsNullOrEmpty(mapArea.displayName)) mapArea.displayName = gameObject.name;
        mapArea.areaType = MapKeyArea.AreaType.FriendlySpawn;
        if (mapArea.iconWorldAnchor == null) mapArea.iconWorldAnchor = transform;
    }

    private void Start()
    {
        var genObj = GameObject.FindGameObjectWithTag("MapGenerator");
        if (genObj != null) rmg = genObj.GetComponent<RandomMapGenerator>();
        else Debug.LogError($"{name}: No object tagged 'MapGenerator' found. Spawner will stay locked.");

        if (rmg != null)
        {
            rmg.OnMapCompleted += HandleMapCompleted;
            if (rmg.MapCompleted) HandleMapCompleted();
            else SetPhase(SpawnerPhase.LockedUntilMapDone);
        }
        else
        {
            SetPhase(SpawnerPhase.LockedUntilMapDone);
        }

        // Initial sync with current visuals
        lastVisibleToMap = IsVisibleToMap();
        SyncMapRegistration(lastVisibleToMap);
    }

    private void OnDestroy()
    {
        if (rmg != null) rmg.OnMapCompleted -= HandleMapCompleted;
        TryMapUnregister(); // safety
    }

    private void Update()
    {
        // Poll for external renderer toggles
        visPollTimer -= Time.unscaledDeltaTime;
        if (visPollTimer <= 0f)
        {
            visPollTimer = VIS_POLL_INTERVAL;
            bool vis = IsVisibleToMap();
            if (vis != lastVisibleToMap)
            {
                lastVisibleToMap = vis;
                SyncMapRegistration(vis);
            }
        }
    }

    private void HandleMapCompleted()
    {
        mapCompleted = true;
        if (currentFriendly == null) SetPhase(SpawnerPhase.ReadyForTouch);
        else SetPhase(SpawnerPhase.CoolingWhileFriendlyAlive);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (phase != SpawnerPhase.ReadyForTouch) return;
        if (!other.CompareTag(playerTag)) return;
        SpawnFriendlyNow();
    }

    private void SpawnFriendlyNow()
    {
        if (!friendlyAIPrefab)
        {
            Debug.LogError($"{name}: No Friendly AI Prefab assigned.");
            return;
        }

        if (currentFriendly != null)
        {
            // already have one alive from this spawner
            return;
        }

        currentFriendly = Instantiate(friendlyAIPrefab, spawnPoint.position, Quaternion.identity);
        SetPhase(SpawnerPhase.CoolingWhileFriendlyAlive);
        StartCoroutine(WaitUntilFriendlyDestroyed(currentFriendly));
    }

    private IEnumerator WaitUntilFriendlyDestroyed(GameObject tracked)
    {
        while (tracked != null) yield return null;

        currentFriendly = null;

        if (mapCompleted)
            SetPhase(SpawnerPhase.ReadyForTouch);
        else
            SetPhase(SpawnerPhase.LockedUntilMapDone);
    }

    // ---- Phase & Visual/Interactable toggles ----

    private void SetPhase(SpawnerPhase next)
    {
        phase = next;
        switch (phase)
        {
            case SpawnerPhase.LockedUntilMapDone:
                SetSpawnerActive(false);
                break;

            case SpawnerPhase.ReadyForTouch:
                SetSpawnerActive(true);
                break;

            case SpawnerPhase.CoolingWhileFriendlyAlive:
                SetSpawnerActive(false);
                break;
        }
    }

    private void SetSpawnerActive(bool active)
    {
        spawnerActive = active;

        if (triggerCol != null) triggerCol.enabled = active;

        if (renderers != null)
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r != null) r.enabled = active;
            }
        }

        lastVisibleToMap = IsVisibleToMap();
        SyncMapRegistration(lastVisibleToMap);
    }

    public void ForceReady()
    {
        currentFriendly = null;
        mapCompleted = (rmg != null && rmg.MapCompleted);
        SetPhase(mapCompleted ? SpawnerPhase.ReadyForTouch : SpawnerPhase.LockedUntilMapDone);
    }

    // ---------- Localized Map logic (no controller edits required) ----------

    private bool IsVisibleToMap()
    {
        if (renderers == null) return false;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r != null && r.enabled) return true;
        }
        return false;
    }

    private void SyncMapRegistration(bool shouldBeVisible)
    {
        if (shouldBeVisible) TryMapRegister();
        else TryMapUnregister();
    }

    private void TryMapRegister()
    {
        if (mapIconRegistered) return;
        var mmc = MapModeController.Instance;
        if (mmc == null || mapArea == null) return;

        // Controller API: RegisterArea(MapKeyArea)
        mmc.RegisterArea(mapArea);
        mapIconRegistered = true;
    }

    private void TryMapUnregister()
    {
        if (!mapIconRegistered) return;
        var mmc = MapModeController.Instance;
        if (mmc == null || mapArea == null) { mapIconRegistered = false; return; }

        // Controller API: UnregisterArea(MapKeyArea)
        mmc.UnregisterArea(mapArea);
        mapIconRegistered = false;
    }
}

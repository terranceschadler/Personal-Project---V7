// SafeBoot.cs
using UnityEngine;
using System.Collections;

/// <summary>
/// Starts Play with heavy systems disabled so you can toggle them on step-by-step
/// to find what’s crashing (MapGen, NavMesh bake, Spawners, Auto-built UIs).
///
/// Hotkeys (while in Play):
///   F1  - Toggle RandomMapGenerator components
///   F2  - Trigger NavMesh bake now (NavMeshRuntimeBaker.BakeAsyncSafely)
///   F3  - Toggle EnemySpawner components
///   F4  - Toggle auto-build flags on EscPauseUI / DeathUIController
///   F6  - Force resume gameplay (Time.timeScale=1, AudioListener.pause=false)
///   F9  - Toggle this on-screen help
/// </summary>
[DefaultExecutionOrder(-1000)] // run before almost everything else
public class SafeBoot : MonoBehaviour
{
    [Header("Start Play With These Disabled")]
    public bool disableMapGen = true;
    public bool disableBaker = true;
    public bool disableSpawners = true;
    public bool disableAutoUIs = true; // stops EscPauseUI/DeathUIController from auto-building

    [Header("Hotkeys")]
    public KeyCode toggleMapGenKey = KeyCode.F1;
    public KeyCode bakeNowKey = KeyCode.F2;
    public KeyCode toggleSpawnersKey = KeyCode.F3;
    public KeyCode toggleAutoUIsKey = KeyCode.F4;
    public KeyCode forceResumeKey = KeyCode.F6;
    public KeyCode toggleHelpKey = KeyCode.F9;

    [Header("Overlay")]
    public bool showHelpOverlay = true;

    // Caches
    private RandomMapGenerator[] _gens;
    private NavMeshRuntimeBaker[] _bakers;
    private EnemySpawner[] _spawners;
    private EscPauseUI[] _pauseUIs;
    private DeathUIController[] _deathUIs;

    private void Awake()
    {
        // Find everything up front (includes inactive objects)
        _gens = FindObjectsOfType<RandomMapGenerator>(true);
        _bakers = FindObjectsOfType<NavMeshRuntimeBaker>(true);
        _spawners = FindObjectsOfType<EnemySpawner>(true);
        _pauseUIs = FindObjectsOfType<EscPauseUI>(true);
        _deathUIs = FindObjectsOfType<DeathUIController>(true);

        if (disableMapGen) SetEnabled(_gens, false, "[SafeBoot] MapGen DISABLED");
        if (disableBaker) SetEnabled(_bakers, false, "[SafeBoot] Baker DISABLED");
        if (disableSpawners) SetEnabled(_spawners, false, "[SafeBoot] Spawners DISABLED");

        if (disableAutoUIs)
        {
            foreach (var ui in _pauseUIs) if (ui) ui.autoBuildIfMissing = false;
            foreach (var ui in _deathUIs) if (ui) ui.autoBuildIfMissing = false;
            Debug.Log("[SafeBoot] UI auto-builds DISABLED");
        }

        // Make sure we don't start paused from a previous scene/frame
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleMapGenKey))
        {
            bool enable = !IsAnyEnabled(_gens);
            SetEnabled(_gens, enable, enable ? "[SafeBoot] MapGen ENABLED" : "[SafeBoot] MapGen DISABLED");
        }

        if (Input.GetKeyDown(bakeNowKey))
        {
            RequestBakeOnAll();
        }

        if (Input.GetKeyDown(toggleSpawnersKey))
        {
            bool enable = !IsAnyEnabled(_spawners);
            SetEnabled(_spawners, enable, enable ? "[SafeBoot] Spawners ENABLED" : "[SafeBoot] Spawners DISABLED");
        }

        if (Input.GetKeyDown(toggleAutoUIsKey))
        {
            bool enable = (_pauseUIs.Length > 0 && !_pauseUIs[0].autoBuildIfMissing) ||
                          (_deathUIs.Length > 0 && !_deathUIs[0].autoBuildIfMissing);
            foreach (var ui in _pauseUIs) if (ui) ui.autoBuildIfMissing = enable;
            foreach (var ui in _deathUIs) if (ui) ui.autoBuildIfMissing = enable;
            Debug.Log(enable ? "[SafeBoot] UI auto-builds ENABLED" : "[SafeBoot] UI auto-builds DISABLED");
        }

        if (Input.GetKeyDown(forceResumeKey))
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Debug.Log("[SafeBoot] Forced resume (time/audio).");
        }

        if (Input.GetKeyDown(toggleHelpKey))
        {
            showHelpOverlay = !showHelpOverlay;
        }
    }

    private void RequestBakeOnAll()
    {
        foreach (var b in _bakers)
        {
            if (!b) continue;
            // Ensure baker can initialize its NavMeshData (OnEnable or EnsureDataReady in your baker)
            if (!b.enabled) b.enabled = true;
            if (!b.gameObject.activeSelf) b.gameObject.SetActive(true);
            StartCoroutine(b.BakeAsyncSafely());
        }
        Debug.Log("[SafeBoot] Requested NavMesh bake (async) on all bakers.");
    }

    private static void SetEnabled(Behaviour[] arr, bool enable, string log = null)
    {
        if (arr == null) return;
        int count = 0;
        foreach (var a in arr)
        {
            if (!a) continue;
            a.enabled = enable;
            count++;
        }
        if (!string.IsNullOrEmpty(log)) Debug.Log($"{log} ({count})");
    }

    private static bool IsAnyEnabled(Behaviour[] arr)
    {
        if (arr == null) return false;
        foreach (var a in arr) if (a && a.enabled) return true;
        return false;
    }

    // Tiny on-screen helper so you don't forget the keys
    private void OnGUI()
    {
        if (!showHelpOverlay) return;

        const int w = 380;
        GUILayout.BeginArea(new Rect(10, 10, w, Screen.height), GUI.skin.box);
        GUILayout.Label("<b>SafeBoot</b>  (F9 to hide)", rich());
        GUILayout.Space(4);

        row("F1", "Toggle MapGen", IsAnyEnabled(_gens));
        row("F2", "Bake NavMesh now", false);
        row("F3", "Toggle Spawners", IsAnyEnabled(_spawners));
        row("F4", "Toggle UI auto-builds", (_pauseUIs.Length > 0 && _pauseUIs[0].autoBuildIfMissing) ||
                                           (_deathUIs.Length > 0 && _deathUIs[0].autoBuildIfMissing));
        row("F6", "Force Resume (time/audio)", false);

        GUILayout.Space(6);
        GUILayout.Label("Tips:\n• Turn systems on one by one.\n• If it crashes on F1 ? MapGen is the culprit.\n• If on F2 ? NavMesh baking.\n• On F3 ? Spawners/UI logic.", small());
        GUILayout.EndArea();

        GUIStyle rich() { var s = new GUIStyle(GUI.skin.label); s.richText = true; s.fontSize = 14; return s; }
        GUIStyle small() { var s = new GUIStyle(GUI.skin.label); s.wordWrap = true; s.fontSize = 11; return s; }

        void row(string key, string what, bool on)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, GUILayout.Width(40));
            GUILayout.Label(what, GUILayout.Width(w - 140));
            GUILayout.Label(on ? "<color=lime>ON</color>" : "<color=orange>OFF</color>", rich(), GUILayout.Width(60));
            GUILayout.EndHorizontal();
        }
    }
}

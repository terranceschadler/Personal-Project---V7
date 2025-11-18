using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Reflection;

[DefaultExecutionOrder(5000)]
[DisallowMultipleComponent]
public class BootOrchestrator : MonoBehaviour
{
    [Header("Core (auto-find if empty)")]
    public MonoBehaviour mapGen;     // RandomMapGenerator
    public MonoBehaviour navBaker;   // NavMeshRuntimeBaker

    [Header("Enable After Bake")]
    public MonoBehaviour[] enemySpawners;
    public MonoBehaviour[] extrasAfterSpawners;

    [Header("Timing / Safety")]
    public float postSceneSettleDelay = 0.15f;
    public float mapCompleteTimeout = 30f;
    public float bakeTimeout = 30f;
    public float fallbackBakeGraceSeconds = 0.5f;

    public bool verboseLogs = true;
    public bool forceEnableMapGen = true;
    public bool safeMode = false;

    private static bool s_BootStarted;
    private Coroutine _bootRoutine;
    
    private void OnEnable()
    {
        // Subscribe to scene loaded to reset boot state on reload
        SceneManager.sceneLoaded += OnSceneReloaded;
    }
    
    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneReloaded;
    }
    
    private void OnSceneReloaded(Scene scene, LoadSceneMode mode)
    {
        // Reset boot state to allow rebaking on scene reload
        if (verboseLogs) Debug.Log($"[Boot] Scene reloaded: {scene.name}, resetting boot state");
        s_BootStarted = false;
    }

    private void Awake()
    {
        if (!Application.isPlaying) return;
        if (safeMode) { if (verboseLogs) Debug.Log("[Boot] Safe Mode ON"); enabled = false; return; }
        if (s_BootStarted) { if (verboseLogs) Debug.Log("[Boot] Duplicate boot ignored"); enabled = false; return; }
        s_BootStarted = true;

        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (!mapGen) mapGen = FindObjectOfTypeByName("RandomMapGenerator");
        if (!navBaker) navBaker = FindObjectOfTypeByName("NavMeshRuntimeBaker");

        SetEnabled(enemySpawners, false);
        SetEnabled(extrasAfterSpawners, false);

        if (mapGen && forceEnableMapGen && !mapGen.enabled) { mapGen.enabled = true; if (verboseLogs) Debug.Log("[Boot] MapGen enabled"); }

        // link baker?generator if both present
        LinkBakerAndGenerator(mapGen, navBaker);
    }

    private void Start()
    {
        if (!Application.isPlaying || safeMode) return;
        if (_bootRoutine == null) _bootRoutine = StartCoroutine(BootSequenceSafe());
    }


    private IEnumerator BootSequenceSafe()
    {
        var ie = BootSequence();
        while (true)
        {
            object cur;
            try
            {
                if (!ie.MoveNext()) break;
                cur = ie.Current;
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[Boot] Unhandled exception: " + ex);
                yield break;
            }
            yield return cur;
        }
    }

    private IEnumerator BootSequence()
    {
        // let scene settle
        yield return null;
        yield return new WaitForSeconds(postSceneSettleDelay);

        // 1) kick/await map gen
        if (mapGen)
        {
            if (forceEnableMapGen && !mapGen.enabled) { mapGen.enabled = true; yield return null; }
            KickMapGeneration(mapGen);

            if (HasMapCompleted(mapGen))
            {
                if (verboseLogs) Debug.Log("[Boot] Waiting for MapCompleted…");
                float t = 0f;
                while (!GetMapCompleted(mapGen) && t < mapCompleteTimeout)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (!GetMapCompleted(mapGen)) Debug.LogWarning($"[Boot] Map timeout ({mapCompleteTimeout}s).");
            }
        }

        // 2) bake navmesh — fallback only (avoid overlapping if baker is already working)
        if (navBaker)
        {
            LinkBakerAndGenerator(mapGen, navBaker);

            bool baked = GetBakeCompleted(navBaker);
            bool isBaking = GetIsBaking(navBaker);

            if (!baked && !isBaking)
            {
                bool expectAutoBake = IsBakerLinkedToGenerator(mapGen, navBaker) && GeneratorAutoBakes(mapGen);

                if (expectAutoBake && fallbackBakeGraceSeconds > 0f)
                {
                    float t = 0f;
                    while (!GetBakeCompleted(navBaker) && !GetIsBaking(navBaker) && t < fallbackBakeGraceSeconds)
                    {
                        t += Time.unscaledDeltaTime;
                        yield return null;
                    }
                }

                if (!GetBakeCompleted(navBaker) && !GetIsBaking(navBaker))
                {
                    if (verboseLogs) Debug.Log("[Boot] Fallback: starting runtime bake…");
                    yield return StartCoroutine(CallBakeAsyncSafely(navBaker));

                    float t = 0f;
                    while (!GetBakeCompleted(navBaker) && t < bakeTimeout)
                    {
                        t += Time.unscaledDeltaTime;
                        yield return null;
                    }
                }
            }
        }

        // 3) enable gameplay scripts
        SetEnabled(enemySpawners, true);
        SetEnabled(extrasAfterSpawners, true);
        if (verboseLogs) Debug.Log("[Boot] Startup finished.");
    }

    // --- helpers ---
    private void SetEnabled(MonoBehaviour[] list, bool state)
    {
        if (list == null) return;
        for (int i = 0; i < list.Length; i++)
        {
            var mb = list[i];
            if (!mb) continue;
            if (mapGen && ReferenceEquals(mb, mapGen) && !state) continue;
            mb.enabled = state;
        }
    }

    private void KickMapGeneration(MonoBehaviour gen)
    {
        if (!gen) return;
        if (InvokeIfExists(gen, "BeginGeneration")) return;
        if (InvokeIfExists(gen, "StartGeneration")) return;

        var f = SafeGetField(gen, "autoStart", typeof(bool));
        if (f != null) { try { f.SetValue(gen, true); } catch { } }
        try { gen.gameObject.SendMessage("BeginGeneration", SendMessageOptions.DontRequireReceiver); } catch { }
    }

    private bool HasMapCompleted(MonoBehaviour gen)
    {
        if (!gen) return false;
        var t = gen.GetType();
        var p = t.GetProperty("MapCompleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.PropertyType == typeof(bool)) return true;
        var f = t.GetField("_mapCompleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return f != null && f.FieldType == typeof(bool);
    }

    private bool GetMapCompleted(MonoBehaviour gen)
    {
        if (!gen) return true;
        var t = gen.GetType();
        var p = t.GetProperty("MapCompleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.PropertyType == typeof(bool)) { try { return (bool)p.GetValue(gen, null); } catch { } }
        var f = t.GetField("_mapCompleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null && f.FieldType == typeof(bool)) { try { return (bool)f.GetValue(gen); } catch { } }
        return true;
    }

    private bool GeneratorAutoBakes(MonoBehaviour gen)
    {
        if (!gen) return false;
        var f = SafeGetField(gen, "autoBakeOnComplete", typeof(bool));
        if (f != null) { try { return (bool)f.GetValue(gen); } catch { } }
        return false;
    }

    private IEnumerator CallBakeAsyncSafely(MonoBehaviour baker)
    {
        if (!baker) yield break;
        MethodInfo mi = null;
        try { mi = baker.GetType().GetMethod("BakeAsyncSafely", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); }
        catch { }
        if (mi == null) yield break;

        IEnumerator ie = null;
        try { ie = mi.Invoke(baker, null) as IEnumerator; }
        catch { }
        if (ie != null) yield return StartCoroutine(ie);
    }

    private bool GetBakeCompleted(MonoBehaviour baker)
    {
        if (!baker) return true;
        var t = baker.GetType();
        var p = t.GetProperty("BakeCompleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.PropertyType == typeof(bool)) { try { return (bool)p.GetValue(baker, null); } catch { return false; } }
        var f = t.GetField("BakeCompleted", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null && f.FieldType == typeof(bool)) { try { return (bool)f.GetValue(baker); } catch { return false; } }
        return false;
    }

    private bool GetIsBaking(MonoBehaviour baker)
    {
        if (!baker) return false;
        var t = baker.GetType();
        var p = t.GetProperty("IsBaking", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.PropertyType == typeof(bool)) { try { return (bool)p.GetValue(baker, null); } catch { return false; } }
        var f = t.GetField("_baking", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null && f.FieldType == typeof(bool)) { try { return (bool)f.GetValue(baker); } catch { return false; } }
        return false;
    }

    private bool InvokeIfExists(object target, string method)
    {
        try
        {
            var mi = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi == null || mi.GetParameters().Length != 0) return false;
            mi.Invoke(target, null);
            return true;
        }
        catch { return false; }
    }

    private FieldInfo SafeGetField(object target, string name, System.Type type)
    {
        try
        {
            var f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType == type) return f;
        }
        catch { }
        return null;
    }

    private bool IsBakerLinkedToGenerator(MonoBehaviour gen, MonoBehaviour baker)
    {
        if (!gen || !baker) return false;
        var bt = baker.GetType();
        try
        {
            var mgField = bt.GetField("mapGenerator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mgField != null)
            {
                var val = mgField.GetValue(baker) as MonoBehaviour;
                if (val == gen) return true;
            }
        }
        catch { }
        return false;
    }

    private void LinkBakerAndGenerator(MonoBehaviour gen, MonoBehaviour baker)
    {
        if (!gen || !baker) return;
        var bt = baker.GetType();

        try
        {
            var mgField = bt.GetField("mapGenerator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mgField != null && mgField.FieldType.IsAssignableFrom(gen.GetType()))
                mgField.SetValue(baker, gen);

            var waitField = bt.GetField("waitForGenerator", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (waitField != null && waitField.FieldType == typeof(bool))
                waitField.SetValue(baker, true);

            if (verboseLogs) Debug.Log("[Boot] Linked baker to generator (waitForGenerator = true).");
        }
        catch { }
    }

    private static MonoBehaviour FindObjectOfTypeByName(string typeName)
    {
        // Prefer new API (keep “include inactive” behavior)
#if UNITY_2023_1_OR_NEWER
        var all = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);

#elif UNITY_2022_2_OR_NEWER
        // No includeInactive overload in many 2022 streams: use Resources and filter to scene instances.
        var allAll = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
        var list = new System.Collections.Generic.List<MonoBehaviour>(allAll.Length);
        for (int i = 0; i < allAll.Length; i++)
        {
            var mb = allAll[i];
            if (!mb) continue;
            var go = mb.gameObject;
            if (go.scene.IsValid()) list.Add(mb); // exclude prefab/assets
        }
        var all = list.ToArray();

#else
        // Older API supports includeInactive bool directly
        var all = Object.FindObjectsOfType<MonoBehaviour>(true);
#endif

        for (int i = 0; i < all.Length; i++)
        {
            var mb = all[i];
            if (!mb) continue;
            if (mb.GetType().Name == typeName) return mb;
        }
        return null;
    }
}

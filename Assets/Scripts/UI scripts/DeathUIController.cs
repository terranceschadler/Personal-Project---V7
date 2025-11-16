using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Text;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;      // Keyboard.current
using UnityEngine.InputSystem.UI;   // InputSystemUIInputModule
#endif

public class DeathUIController : MonoBehaviour
{
    public static DeathUIController Instance { get; private set; }

    [Header("Overlay (auto-filled if autoBuildIfMissing is true)")]
    public CanvasGroup overlay;
    public Text titleText;         // "You Died"
    public Text statsText;         // multiline stats
    public Button restartButton;   // restart scene
    public Button quitButton;      // quit game

    [Header("Behavior")]
    public bool dontDestroyOnLoad = true;
    public bool autoBuildIfMissing = true;
    public bool deactivateRootOnHide = true;
    public string title = "You Died";

    [Header("UI Focus")]
    [Tooltip("Select this first when the overlay shows. If null, uses restart, then quit.")]
    public Selectable firstSelectedOverride;
    [Tooltip("Keep controller focus alive while visible (reselect if it becomes null).")]
    public bool keepFocusAlive = true;
    [Tooltip("Apply ColorTint + Selected/Highlighted colors to the buttons at runtime.")]
    public bool applyRuntimeButtonColors = true;
    public Color selectedColor = new Color(1f, 0.85f, 0.30f, 1f);
    public Color highlightedColor = new Color(1f, 1f, 1f, 0.92f);
    [Range(0.01f, 0.25f)] public float colorFadeDuration = 0.08f;

    [Header("Gamepad Shortcuts")]
    [Tooltip("Enable A=Restart and B=Quit shortcuts while the death UI is visible.")]
    public bool allowGamepadShortcuts = true;
    public bool aRestarts = true;
    public bool bQuits = true;

    private GameObject _root;
    private Selectable _lastSelected;
    private bool _builtThisInstance = false;    // per-instance (no static!)
    private bool _visible => IsVisible();

    // ---------------- Lifecycle ----------------
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);

        EnsureEventSystem();

        if (autoBuildIfMissing && MissingOverlayRefs() && !_builtThisInstance)
        {
            BuildDefaultOverlay(); // vertical layout prevents overlap
            _builtThisInstance = true;
        }

        CacheRoot();
        WireButtons();
        SetupSelectable(restartButton);
        SetupSelectable(quitButton);
        HideImmediate();

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        RehookAfterSceneLoad();
    }

    /// <summary>
    /// Reacquire scene-bound bits after a load: EventSystem, rebuild overlay if missing,
    /// rewire, hide, and reset time/audio.
    /// </summary>
    public void RehookAfterSceneLoad()
    {
        // Ensure not visible on new scene unless explicitly shown
        HideImmediate();

        // Safety: make sure gameplay is running after a reload
        Time.timeScale = 1f;
        AudioListener.pause = false;

        EnsureEventSystem();

        if (autoBuildIfMissing && MissingOverlayRefs())
        {
            Debug.Log("[DeathUIController] Overlay missing after scene load. Rebuilding.");
            BuildDefaultOverlay();
            _builtThisInstance = true;
        }

        CacheRoot();
        WireButtons();
        SetupSelectable(restartButton);
        SetupSelectable(quitButton);

        // Clear any stale selection
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void Update()
    {
        if (!_visible) return;

        // Controller shortcuts
        if (allowGamepadShortcuts)
        {
#if ENABLE_INPUT_SYSTEM
            bool aPressed = aRestarts && GamepadInput.A_Pressed;
            bool bPressed = bQuits && GamepadInput.B_Pressed;
#else
            bool aPressed = false, bPressed = false;
#endif
            if (aPressed && restartButton && restartButton.interactable)
                restartButton.onClick.Invoke();

            if (bPressed && quitButton && quitButton.interactable)
                quitButton.onClick.Invoke();
        }

        // Keep focus alive
        if (keepFocusAlive && EventSystem.current != null)
        {
            var cur = EventSystem.current.currentSelectedGameObject;
            if (cur == null || !cur.activeInHierarchy || !IsChildOfOverlay(cur.transform))
            {
                ReselectBest();
            }
            else
            {
                var sel = cur.GetComponent<Selectable>();
                if (sel != null && sel.IsActive() && sel.interactable)
                    _lastSelected = sel;
            }
        }
    }

    // ---------------- Public API ----------------
    /// <summary>Show the death screen, freeze gameplay, and display current run stats.</summary>
    public void ShowStats()
    {
        EnsureEventSystem();

        if (autoBuildIfMissing && MissingOverlayRefs())
        {
            BuildDefaultOverlay();
            _builtThisInstance = true;
        }

        CacheRoot();
        WireButtons();
        SetupSelectable(restartButton);
        SetupSelectable(quitButton);

        // Pause via GameManager so any watchdogs keep time frozen
        if (GameManager.Instance != null)
            GameManager.Instance.PauseGame();
        else
        {
            Time.timeScale = 0f;
            AudioListener.pause = true;
        }

        if (titleText != null) titleText.text = string.IsNullOrEmpty(title) ? "You Died" : title;
        if (statsText != null) statsText.text = ComposeStatsText();

        Show();
    }

    // ---------------- UI control ----------------
    private void Show()
    {
        if (overlay == null) return;
        if (_root != null && deactivateRootOnHide) _root.SetActive(true);
        overlay.alpha = 1f;
        overlay.blocksRaycasts = true;
        overlay.interactable = true;

        // Focus first selectable for controller users
        var first = firstSelectedOverride != null ? firstSelectedOverride
                   : (restartButton != null ? (Selectable)restartButton : quitButton);
        Select(first);
    }

    private void HideImmediate()
    {
        if (overlay == null) return;
        overlay.alpha = 0f;
        overlay.blocksRaycasts = false;
        overlay.interactable = false;
        if (_root != null && deactivateRootOnHide) _root.SetActive(false);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
        _lastSelected = null;
    }

    private bool IsVisible()
    {
        return overlay != null && overlay.interactable && overlay.blocksRaycasts && overlay.alpha > 0.99f;
    }

    // ---------------- Helpers ----------------
    private string ComposeStatsText()
    {
        var gm = GameManager.Instance;
        if (gm == null) return "No stats available.";

        var sb = new StringBuilder();

        // Time mm:ss
        var t = System.TimeSpan.FromSeconds(gm.RunSeconds);
        string mmss = t.ToString(@"mm\:ss");

        int partsCollected = gm.CollectedHelicopterParts != null ? gm.CollectedHelicopterParts.Count : 0;
        int targetParts = (gm.helicopterPartPrefabs != null && gm.helicopterPartPrefabs.Length > 0)
                            ? gm.helicopterPartPrefabs.Length : partsCollected;

        sb.AppendLine($"Score: {gm.Score}");
        sb.AppendLine($"Time Survived: {mmss}");
        sb.AppendLine($"Wave Reached: {gm.CurrentWave}");
        sb.AppendLine($"Enemies Killed: {gm.EnemiesKilled}");
        sb.AppendLine($"Bosses Killed: {gm.BossKillCount}");
        sb.AppendLine($"Coins: {gm.coins}");
        sb.AppendLine($"Materials: {gm.materials}");
        sb.AppendLine($"Helicopter Parts: {partsCollected}/{targetParts}");

        return sb.ToString();
    }

    private void WireButtons()
    {
        if (restartButton != null)
        {
            restartButton.onClick.RemoveAllListeners();
            restartButton.onClick.AddListener(() =>
            {
                HideImmediate();
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

                // Use GameManager's RestartLevel to properly reset stats
                var gm = GameManager.Instance;
                if (gm != null)
                {
                    gm.RestartLevel();
                }
                else
                {
                    // Fallback if no GameManager
                    Time.timeScale = 1f;
                    AudioListener.pause = false;
                    var current = SceneManager.GetActiveScene();
                    SceneManager.LoadScene(current.buildIndex);
                }
            });
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(() =>
            {
                // Unfreeze then quit cleanly
                if (GameManager.Instance != null)
                {
                    GameManager.Instance.HardResumeGameplay();
                    GameManager.Instance.QuitGame();
                }
                else
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
            });
        }
    }

    private bool MissingOverlayRefs()
    {
        return overlay == null || titleText == null || statsText == null || quitButton == null || restartButton == null;
    }

    private void CacheRoot() => _root = overlay ? overlay.gameObject : _root;

    private void Select(Selectable s)
    {
        if (EventSystem.current == null || s == null) return;
        if (!s.IsActive() || !s.interactable) return;

        EventSystem.current.SetSelectedGameObject(s.gameObject);
        s.Select();
        _lastSelected = s;
    }

    private void ReselectBest()
    {
        if (!_visible || EventSystem.current == null) return;

        if (_lastSelected != null && _lastSelected.IsActive() && _lastSelected.interactable)
        {
            EventSystem.current.SetSelectedGameObject(_lastSelected.gameObject);
            _lastSelected.Select();
            return;
        }

        Select(firstSelectedOverride ?? (restartButton ? (Selectable)restartButton : quitButton));
    }

    private bool IsChildOfOverlay(Transform t)
    {
        if (overlay == null) return false;
        var root = overlay.transform;
        while (t != null)
        {
            if (t == root) return true;
            t = t.parent;
        }
        return false;
    }

    // Ensures controller nav + nice colors on any button handed in
    private void SetupSelectable(Selectable s)
    {
        if (s == null) return;

        var nav = s.navigation;
        nav.mode = Navigation.Mode.Automatic;
        s.navigation = nav;

        if (applyRuntimeButtonColors)
        {
            var cb = s.colors;
            cb.fadeDuration = colorFadeDuration;
            cb.highlightedColor = highlightedColor;
            cb.selectedColor = selectedColor;
            s.transition = Selectable.Transition.ColorTint;
            s.colors = cb;
        }
    }

    // ---------------- Auto-build overlay (no duplicate RectTransforms) ----------------
    private void BuildDefaultOverlay()
    {
        EnsureEventSystem();

        // Root (Canvas + CanvasGroup)
        var root = new GameObject("DeathUI (Auto)",
                                  typeof(RectTransform),
                                  typeof(Canvas),
                                  typeof(CanvasScaler),
                                  typeof(GraphicRaycaster),
                                  typeof(CanvasGroup));
        if (dontDestroyOnLoad) DontDestroyOnLoad(root);

        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        overlay = root.GetComponent<CanvasGroup>();

        // Dim background
        var bg = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(root.transform, false);
        var bgImg = bg.GetComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.65f);
        var bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one; bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

        // Panel (center)
        var panel = new GameObject("Panel",
                                   typeof(RectTransform),
                                   typeof(Image),
                                   typeof(VerticalLayoutGroup));
        panel.transform.SetParent(root.transform, false);
        var pImg = panel.GetComponent<Image>();
        pImg.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);
        var pRT = panel.GetComponent<RectTransform>();
        pRT.sizeDelta = new Vector2(700, 440);
        pRT.anchorMin = pRT.anchorMax = new Vector2(0.5f, 0.5f);
        pRT.anchoredPosition = Vector2.zero;

        var vlg = panel.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 18f;
        vlg.padding = new RectOffset(28, 28, 28, 28);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        // Title
        var titleGO = new GameObject("Title",
                                     typeof(RectTransform),
                                     typeof(Text),
                                     typeof(LayoutElement));
        titleGO.transform.SetParent(panel.transform, false);
        titleText = titleGO.GetComponent<Text>();
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.fontSize = 40; titleText.color = Color.white;
        titleText.text = title;
        var titleLE = titleGO.GetComponent<LayoutElement>();
        titleLE.minHeight = 56; titleLE.preferredHeight = 56;

        // Stats
        var statsGO = new GameObject("Stats",
                                     typeof(RectTransform),
                                     typeof(Text),
                                     typeof(ContentSizeFitter),
                                     typeof(LayoutElement));
        statsGO.transform.SetParent(panel.transform, false);
        statsText = statsGO.GetComponent<Text>();
        statsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statsText.alignment = TextAnchor.UpperLeft;
        statsText.fontSize = 22; statsText.color = Color.white;
        statsText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statsText.verticalOverflow = VerticalWrapMode.Truncate;

        var statsFitter = statsGO.GetComponent<ContentSizeFitter>();
        statsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        statsFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var statsLE = statsGO.GetComponent<LayoutElement>();
        statsLE.flexibleHeight = 1;

        // Buttons row (RectTransform included upfront)
        var buttonsGO = new GameObject("Buttons",
                                       typeof(RectTransform),
                                       typeof(LayoutElement),
                                       typeof(HorizontalLayoutGroup));
        buttonsGO.transform.SetParent(panel.transform, false);
        var buttonsLE = buttonsGO.GetComponent<LayoutElement>();
        buttonsLE.minHeight = 72; buttonsLE.preferredHeight = 84;

        var hlg = buttonsGO.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 20;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = false;

        // Buttons
        restartButton = MakeButton(buttonsGO.transform, "Restart", () =>
        {
            HideImmediate();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            // Use GameManager's RestartLevel to properly reset stats
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.RestartLevel();
            }
            else
            {
                // Fallback if no GameManager
                Time.timeScale = 1f;
                AudioListener.pause = false;
                var cur = SceneManager.GetActiveScene();
                SceneManager.LoadScene(cur.buildIndex);
            }
        });

        quitButton = MakeButton(buttonsGO.transform, "Quit", () =>
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.HardResumeGameplay();
                GameManager.Instance.QuitGame();
            }
            else
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        });

        // Start hidden
        HideImmediate();
        _root = root;
    }

    private Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label,
                                typeof(RectTransform),
                                typeof(Image),
                                typeof(Button),
                                typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = new Color(0.22f, 0.22f, 0.22f, 1f);

        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 60;
        le.preferredHeight = 64;
        le.flexibleWidth = 1; // share row width evenly

        var btn = go.GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(onClick);

        var lbl = new GameObject("Text",
                                 typeof(RectTransform),
                                 typeof(Text));
        lbl.transform.SetParent(go.transform, false);
        var t = lbl.GetComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = label;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 14;
        t.resizeTextMaxSize = 36;

        var lrt = lbl.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        // Ensure controller nav + colors on auto buttons
        SetupSelectable(btn);

        return btn;
    }

    // ---------------- EventSystem ----------------
    private void EnsureEventSystem()
    {
        var es = FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            var esGO = new GameObject("EventSystem", typeof(RectTransform), typeof(EventSystem));
            es = esGO.GetComponent<EventSystem>();
        }

#if ENABLE_INPUT_SYSTEM
        bool newInput = Keyboard.current != null || Gamepad.all.Count > 0;
        var inputSys = es.GetComponent<InputSystemUIInputModule>();
        var legacy   = es.GetComponent<StandaloneInputModule>();

        if (newInput)
        {
            if (inputSys == null) es.gameObject.AddComponent<InputSystemUIInputModule>();
            if (legacy   != null) Destroy(legacy);
        }
        else
        {
            if (legacy == null) es.gameObject.AddComponent<StandaloneInputModule>();
            if (inputSys != null) Destroy(inputSys);
        }
#else
        if (es.GetComponent<StandaloneInputModule>() == null)
            es.gameObject.AddComponent<StandaloneInputModule>();
#endif
    }
}

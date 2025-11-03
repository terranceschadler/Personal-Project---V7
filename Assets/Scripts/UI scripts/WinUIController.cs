using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;      // Keyboard.current, Gamepad
using UnityEngine.InputSystem.UI;   // InputSystemUIInputModule
#endif

public class WinUIController : MonoBehaviour
{
    public static WinUIController Instance { get; private set; }

    [Header("Overlay (optional if auto-build)")]
    public CanvasGroup winGroup;
    public Text titleText;
    public Text statsText;        // body area for stats
    public Button restartButton;
    public Button quitButton;

    [Header("Behavior")]
    [Tooltip("Keep this controller across scene loads.")]
    public bool dontDestroyOnLoad = true;

    [Tooltip("Create a default overlay UI if references are missing.")]
    public bool autoBuildIfMissing = true;

    [Tooltip("Default win title if none is provided.")]
    public string defaultWinTitle = "You Escaped!";

    // --- UI Focus/Colors ---
    [Header("UI Focus")]
    [Tooltip("Select this first when the overlay shows. If null, uses Restart, then Quit.")]
    public Selectable firstSelectedOverride;
    [Tooltip("Keep controller focus alive while visible (reselect if it becomes null).")]
    public bool keepFocusAlive = true;

    [Header("Button Colors")]
    [Tooltip("Apply ColorTint + Selected/Highlighted colors to the buttons at runtime.")]
    public bool applyRuntimeButtonColors = true;
    public Color selectedColor = new Color(1f, 0.85f, 0.30f, 1f);
    public Color highlightedColor = new Color(1f, 1f, 1f, 0.92f);
    [Range(0.01f, 0.25f)] public float colorFadeDuration = 0.08f;

    // --- Gamepad shortcuts ---
    [Header("Gamepad Shortcuts")]
    [Tooltip("Enable A=Restart and B=Quit shortcuts while the win UI is visible.")]
    public bool allowGamepadShortcuts = true;
    public bool aRestarts = true;
    public bool bQuits = true;

    private GameObject _overlayRootGO;
    private bool _visible = false;
    private bool _subscribed = false;
    private Selectable _lastSelected;

    // Per-instance guard (no cross-scene static!)
    private bool _builtThisInstance = false;

    // ---------------- Lifecycle ----------------
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);

        EnsureEventSystem();

        if (autoBuildIfMissing && MissingOverlayRefs() && !_builtThisInstance)
        {
            BuildDefaultOverlay();
            _builtThisInstance = true;
        }

        CacheOverlayRoot();
        HideImmediate();

        WireButtons();
        SetupSelectable(restartButton);
        SetupSelectable(quitButton);

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnEnable() { TrySubscribeToGM(); }
    private void OnDisable() { UnsubscribeFromGM(); }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnsubscribeFromGM();
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
        // Normalize gameplay state
        if (Time.timeScale == 0f) Time.timeScale = 1f;
        AudioListener.pause = false;

        EnsureEventSystem();

        if (autoBuildIfMissing && MissingOverlayRefs())
        {
            Debug.Log("[WinUIController] Overlay missing after scene load. Rebuilding.");
            BuildDefaultOverlay();
            _builtThisInstance = true;
        }

        CacheOverlayRoot();
        HideImmediate();

        UnsubscribeFromGM();
        TrySubscribeToGM();

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void Update()
    {
        if (!_visible) return;

        // Controller shortcuts
        if (allowGamepadShortcuts)
        {
            bool aPressed = false, bPressed = false;

#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
            {
                aPressed = Gamepad.current.buttonSouth.wasPressedThisFrame; // A / Cross
                bPressed = Gamepad.current.buttonEast.wasPressedThisFrame;  // B / Circle
            }
#endif
            // Legacy fallback
            aPressed |= Input.GetKeyDown(KeyCode.JoystickButton0);
            bPressed |= Input.GetKeyDown(KeyCode.JoystickButton1);

            if (aRestarts && aPressed && restartButton && restartButton.interactable)
                restartButton.onClick.Invoke();

            if (bQuits && bPressed && quitButton && quitButton.interactable)
                quitButton.onClick.Invoke();
        }

        // Keep controller focus alive
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
    /// <summary>Show the win overlay, set title & stats, and freeze gameplay.</summary>
    public void ShowWin(string title = null, string statsBody = null)
    {
        EnsureEventSystem();

        if (autoBuildIfMissing && MissingOverlayRefs())
        {
            BuildDefaultOverlay();
            _builtThisInstance = true;
        }

        CacheOverlayRoot();
        WireButtons();
        SetupSelectable(restartButton);
        SetupSelectable(quitButton);

        if (titleText != null)
            titleText.text = string.IsNullOrEmpty(title) ? defaultWinTitle : title;

        if (statsText != null)
            statsText.text = string.IsNullOrWhiteSpace(statsBody) ? BuildStatsBodyFromGameManager() : statsBody;

        // Show overlay
        if (_overlayRootGO != null) _overlayRootGO.SetActive(true);
        if (winGroup != null)
        {
            winGroup.alpha = 1f;
            winGroup.blocksRaycasts = true;
            winGroup.interactable = true;
        }

        var gm = GameManager.Instance;
        if (gm != null) gm.PauseGame();
        else
        {
            if (Time.timeScale != 0f) Time.timeScale = 0f;
            AudioListener.pause = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        _visible = true;

        // Focus a button for keyboard/controller users
        var first = firstSelectedOverride != null ? firstSelectedOverride
                   : (restartButton != null ? (Selectable)restartButton : quitButton);
        Select(first);
    }

    /// <summary>Convenience: auto title + auto stats from GameManager.</summary>
    public void ShowWinAuto() => ShowWin(null, null);

    /// <summary>Hides the overlay and unfreezes gameplay.</summary>
    public void Resume()
    {
        HideImmediate();

        var gm = GameManager.Instance;
        if (gm != null) gm.ResumeGame();
        else
        {
            if (Time.timeScale == 0f) Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }

    // ---------------- Internal UI handlers ----------------
    private void OnRestartClicked()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;

        var gm = GameManager.Instance;
        if (gm != null) gm.ResetRun(true); // resetCurrencies: TRUE

        HideImmediate();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        var cur = SceneManager.GetActiveScene();
        SceneManager.LoadScene(cur.buildIndex);
    }

    private void OnQuitClicked()
    {
        var gm = GameManager.Instance;
        if (gm != null) gm.HardResumeGameplay();
        else { Time.timeScale = 1f; AudioListener.pause = false; }

        if (gm != null) gm.QuitGame();
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    // ---------------- Helpers ----------------
    private void HideImmediate()
    {
        _visible = false;

        if (winGroup != null)
        {
            winGroup.alpha = 0f;
            winGroup.blocksRaycasts = false;
            winGroup.interactable = false;
        }

        if (_overlayRootGO != null) _overlayRootGO.SetActive(false);

        // Clear selection so gameplay input resumes cleanly
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        _lastSelected = null;
    }

    private bool MissingOverlayRefs()
    {
        // titleText / statsText / buttons / group are all needed for a usable overlay
        return winGroup == null || restartButton == null || quitButton == null || titleText == null || statsText == null;
    }

    private void WireButtons()
    {
        if (restartButton != null)
        {
            restartButton.onClick.RemoveAllListeners();
            restartButton.onClick.AddListener(OnRestartClicked);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(OnQuitClicked);
        }
    }

    private void CacheOverlayRoot()
    {
        _overlayRootGO = winGroup ? winGroup.gameObject : _overlayRootGO;
    }

    private string BuildStatsBodyFromGameManager()
    {
        var gm = GameManager.Instance;
        if (gm == null) return "";

        var t = System.TimeSpan.FromSeconds(gm.RunSeconds);
        string mmss = string.Format("{0:00}:{1:00}", (int)t.TotalMinutes, t.Seconds);

        int partsCollected = gm.CollectedHelicopterParts != null ? gm.CollectedHelicopterParts.Count : 0;
        int targetParts = (gm.helicopterPartPrefabs != null && gm.helicopterPartPrefabs.Length > 0)
                            ? gm.helicopterPartPrefabs.Length : partsCollected;

        return
            $"Score: {gm.Score}\n" +
            $"Time Survived: {mmss}\n" +
            $"Wave Reached: {gm.CurrentWave}\n" +
            $"Enemies Killed: {gm.EnemiesKilled}\n" +
            $"Bosses Killed: {gm.BossKillCount}\n" +
            $"Friendlies Rescued: {gm.FriendliesRescued}\n" +
            $"Coins: {gm.coins}\n" +
            $"Materials: {gm.materials}\n" +
            $"Helicopter Parts: {partsCollected}/{targetParts}";
    }

    private void TrySubscribeToGM()
    {
        var gm = GameManager.Instance;
        if (gm == null || _subscribed) return;

        gm.OnScoreChanged += OnAnyStatChanged;
        gm.OnWaveChanged += OnAnyStatChanged;
        gm.OnEnemiesKilledChanged += OnAnyStatChanged;
        gm.OnBossKillsChanged += OnAnyStatChanged;
        gm.OnCoinsChanged += OnAnyStatChanged;
        gm.OnMaterialsChanged += OnAnyStatChanged;
        gm.OnHelicopterPartCollected += OnAnyHelicopterChanged;
        gm.OnFriendliesRescuedChanged += OnAnyStatChanged;

        _subscribed = true;
    }

    private void UnsubscribeFromGM()
    {
        var gm = GameManager.Instance;
        if (gm == null || !_subscribed) return;

        gm.OnScoreChanged -= OnAnyStatChanged;
        gm.OnWaveChanged -= OnAnyStatChanged;
        gm.OnEnemiesKilledChanged -= OnAnyStatChanged;
        gm.OnBossKillsChanged -= OnAnyStatChanged;
        gm.OnCoinsChanged -= OnAnyStatChanged;
        gm.OnMaterialsChanged -= OnAnyStatChanged;
        gm.OnHelicopterPartCollected -= OnAnyHelicopterChanged;
        gm.OnFriendliesRescuedChanged -= OnAnyStatChanged;

        _subscribed = false;
    }

    private void OnAnyStatChanged(int _)
    {
        if (_visible && statsText != null)
            statsText.text = BuildStatsBodyFromGameManager();
    }

    private void OnAnyHelicopterChanged(string _)
    {
        if (_visible && statsText != null)
            statsText.text = BuildStatsBodyFromGameManager();
    }

    // --- Selection helpers ---
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
        if (winGroup == null) return false;
        var root = winGroup.transform;
        while (t != null)
        {
            if (t == root) return true;
            t = t.parent;
        }
        return false;
    }

    private void SetupSelectable(Selectable s)
    {
        if (s == null) return;

        // Controller navigation
        var nav = s.navigation;
        nav.mode = Navigation.Mode.Automatic;
        s.navigation = nav;

        // Nice highlight/selected colors
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

    // ---------------- Overlay Auto-Build (STACKED layout) ----------------
    private void BuildDefaultOverlay()
    {
        EnsureEventSystem();

        // Root (create with RectTransform so we never add it later)
        var root = new GameObject("WinUI (Auto)",
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

        winGroup = root.GetComponent<CanvasGroup>();

        // Dim background
        var bg = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(root.transform, false);
        var bgImg = bg.GetComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.6f);
        var bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one; bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

        // Dialog panel with VerticalLayoutGroup
        var dialog = new GameObject("Dialog",
                                    typeof(RectTransform),
                                    typeof(Image),
                                    typeof(VerticalLayoutGroup));
        dialog.transform.SetParent(root.transform, false);
        var dlgImg = dialog.GetComponent<Image>();
        dlgImg.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);
        var dlgRT = dialog.GetComponent<RectTransform>();
        dlgRT.sizeDelta = new Vector2(720, 460);
        dlgRT.anchorMin = dlgRT.anchorMax = new Vector2(0.5f, 0.5f);
        dlgRT.anchoredPosition = Vector2.zero;

        var vlg = dialog.GetComponent<VerticalLayoutGroup>();
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
        titleGO.transform.SetParent(dialog.transform, false);
        titleText = titleGO.GetComponent<Text>();
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.fontSize = 36;
        titleText.color = Color.white;
        titleText.text = defaultWinTitle;
        var titleLE = titleGO.GetComponent<LayoutElement>();
        titleLE.minHeight = 56;
        titleLE.preferredHeight = 56;

        // Stats body
        var statsGO = new GameObject("Stats",
                                     typeof(RectTransform),
                                     typeof(Text),
                                     typeof(ContentSizeFitter),
                                     typeof(LayoutElement));
        statsGO.transform.SetParent(dialog.transform, false);
        statsText = statsGO.GetComponent<Text>();
        statsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statsText.alignment = TextAnchor.UpperLeft;
        statsText.fontSize = 22;
        statsText.color = Color.white;
        statsText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statsText.verticalOverflow = VerticalWrapMode.Truncate;

        var statsFitter = statsGO.GetComponent<ContentSizeFitter>();
        statsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        statsFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        var statsLE = statsGO.GetComponent<LayoutElement>();
        statsLE.flexibleHeight = 1;

        // Buttons container — RectTransform in ctor (avoid later AddComponent)
        var buttons = new GameObject("Buttons",
                                     typeof(RectTransform),
                                     typeof(LayoutElement),
                                     typeof(HorizontalLayoutGroup));
        buttons.transform.SetParent(dialog.transform, false);
        var buttonsLE = buttons.GetComponent<LayoutElement>();
        buttonsLE.minHeight = 72;
        buttonsLE.preferredHeight = 84;

        var hlg = buttons.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 20;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = false;

        // Buttons: Restart + Quit
        restartButton = MakeButton(buttons.transform, "Restart", OnRestartClicked);
        quitButton = MakeButton(buttons.transform, "Quit", OnQuitClicked);

        // Start hidden
        HideImmediate();

        _overlayRootGO = root;
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
        le.flexibleWidth = 1;

        var btn = go.GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(onClick);

        // Label
        var lbl = new GameObject("Text", typeof(RectTransform), typeof(Text));
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
        var es = FindFirstOrAnyObjectByType<EventSystem>();
        if (es == null)
        {
            var esGO = new GameObject("EventSystem",
                                      typeof(RectTransform),
                                      typeof(EventSystem));
            es = esGO.GetComponent<EventSystem>();
        }

#if ENABLE_INPUT_SYSTEM
        bool haveNewInput = Keyboard.current != null || Gamepad.all.Count > 0;
        var inputSys = es.GetComponent<InputSystemUIInputModule>();
        var legacy   = es.GetComponent<StandaloneInputModule>();

        if (haveNewInput)
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

    // -------- Version-safe finder (avoids obsolete warnings) --------
    private static T FindFirstOrAnyObjectByType<T>() where T : UnityEngine.Object
    {
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindFirstObjectByType<T>();
#elif UNITY_2022_2_OR_NEWER
        return UnityEngine.Object.FindFirstObjectByType<T>();
#else
        return UnityEngine.Object.FindObjectOfType<T>();
#endif
    }
}

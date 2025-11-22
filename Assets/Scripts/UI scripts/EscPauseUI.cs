using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class EscPauseUI : MonoBehaviour
{
    public static EscPauseUI Instance { get; private set; }

    [Header("Lifetime")] public bool dontDestroyOnLoad = true;

    [Header("Overlay (assign or auto-build)")]
    public CanvasGroup overlay;
    public Button resumeButton;
    public Button restartButton;
    public Button quitButton;

    [Header("Stats UI")]
    public Text statsText;

    [Header("Behavior")]
    public bool autoBuildIfMissing = true;
    public bool deactivateRootOnHide = true;
    public KeyCode pauseKey = KeyCode.Escape;

    [Header("Time/Audio Control")]
    public bool pauseTimeWhenVisible = true;
    public bool pauseAudioWhenVisible = true;

    [Header("Gamepad")]
    public bool enableGamepadPause = true;
    public bool bButtonAlsoResumes = true;

    [Header("UI Focus")]
    public Selectable firstSelectedOverride;
    public bool keepFocusAlive = true;
    public bool applyRuntimeButtonColors = true;
    public Color selectedColor = new Color(1f, 0.85f, 0.30f, 1f);
    public Color highlightedColor = new Color(1f, 1f, 1f, 0.92f);
    [Range(0.01f, 0.25f)] public float colorFadeDuration = 0.08f;

    private GameObject _root;
    private bool _visible = false;
    private bool _subscribed = false;
    private Selectable _lastSelected;

    private bool _builtThisInstance = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (dontDestroyOnLoad) DontDestroyOnLoad(transform.root.gameObject);

        EnsureEventSystem_ForceOld();

        if (autoBuildIfMissing && MissingOverlayRefs() && !_builtThisInstance)
        {
            BuildDefaultOverlay();
            _builtThisInstance = true;
        }

        CacheRoot();
        WireButtons();
        SetupSelectable(resumeButton);
        SetupSelectable(restartButton);
        SetupSelectable(quitButton);

        HideImmediate();

        Time.timeScale = 1f;
        if (pauseAudioWhenVisible) AudioListener.pause = false;

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

    public void RehookAfterSceneLoad()
    {
        Time.timeScale = 1f;
        if (pauseAudioWhenVisible) AudioListener.pause = false;

        EnsureEventSystem_ForceOld();

        if (MissingOverlayRefs() && autoBuildIfMissing)
        {
            Debug.Log("[EscPauseUI] Overlay missing after scene load. Rebuilding minimal overlay.");
            BuildDefaultOverlay();
            _builtThisInstance = true;
        }

        CacheRoot();
        WireButtons();
        HideImmediate();

        UnsubscribeFromGM();
        TrySubscribeToGM();

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void Update()
    {
        if (PressedPause())
        {
            if (_visible) Hide();
            else Show();
        }

        if (_visible && enableGamepadPause)
        {
            if (GamepadInput.A_Pressed || (bButtonAlsoResumes && GamepadInput.B_Pressed))
                Hide();
        }

        if (_visible && keepFocusAlive && EventSystem.current != null)
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

    private bool PressedPause()
    {
#if ENABLE_INPUT_SYSTEM
        // New Input System
        bool key = false;
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            // Check the configured pause key
            if (pauseKey == KeyCode.Escape && kb.escapeKey.wasPressedThisFrame) key = true;
            else if (pauseKey == KeyCode.P && kb.pKey.wasPressedThisFrame) key = true;
            // Add more key mappings as needed
        }
#else
        // Old Input Manager
        bool key = Input.GetKeyDown(pauseKey);
#endif
        if (enableGamepadPause)
            key |= GamepadInput.Start_Pressed;
        return key;
    }

    public void Show()
    {
        if (overlay == null) return;

        if (pauseTimeWhenVisible)
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.PauseGame();
            else
            {
                Time.timeScale = 0f;
                if (pauseAudioWhenVisible) AudioListener.pause = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        if (_root != null && deactivateRootOnHide) _root.SetActive(true);
        overlay.alpha = 1f; overlay.blocksRaycasts = true; overlay.interactable = true;

        RefreshStats();
        _visible = true;

        var first = firstSelectedOverride != null ? firstSelectedOverride :
                    resumeButton != null ? (Selectable)resumeButton : null;

        Select(first ?? restartButton ?? quitButton);
    }

    public void Hide()
    {
        if (overlay == null) return;

        if (pauseTimeWhenVisible)
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.ResumeGame();
            else
            {
                Time.timeScale = 1f;
                if (pauseAudioWhenVisible) AudioListener.pause = false;
            }
        }

        overlay.alpha = 0f; overlay.blocksRaycasts = false; overlay.interactable = false;
        if (_root != null && deactivateRootOnHide) _root.SetActive(false);

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        _visible = false;
    }

    public void HideImmediate() => Hide();

    public void RestartRun()
    {
        Time.timeScale = 1f;
        if (pauseAudioWhenVisible) AudioListener.pause = false;

        var gm = GameManager.Instance;
        if (gm != null) gm.ResetRun(true);

        Hide();

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        var current = SceneManager.GetActiveScene();
        SceneManager.LoadScene(current.buildIndex);
    }

    private void RefreshStats()
    {
        if (statsText == null) return;

        var gm = GameManager.Instance;
        if (gm == null) { statsText.text = "No GameManager found."; return; }

        var t = System.TimeSpan.FromSeconds(gm.RunSeconds);
        string mmss = t.ToString(@"mm\:ss");

        int partsCollected = gm.CollectedHelicopterParts != null ? gm.CollectedHelicopterParts.Count : 0;
        int targetParts = (gm.helicopterPartPrefabs != null && gm.helicopterPartPrefabs.Length > 0)
                            ? gm.helicopterPartPrefabs.Length : partsCollected;

        statsText.text =
            $"Score: {gm.Score}\n" +
            $"Time Survived: {mmss}\n" +
            $"Wave Reached: {gm.CurrentWave}\n" +
            $"Enemies Killed: {gm.EnemiesKilled}\n" +
            $"Bosses Killed: {gm.BossKillCount}\n" +
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

        _subscribed = false;
    }

    private void OnAnyStatChanged(int _) { if (_visible) RefreshStats(); }
    private void OnAnyHelicopterChanged(string _) { if (_visible) RefreshStats(); }

    private void WireButtons()
    {
        if (resumeButton != null)
        {
            resumeButton.onClick.RemoveAllListeners();
            resumeButton.onClick.AddListener(() => Hide());
            SetupSelectable(resumeButton);
        }

        if (restartButton != null)
        {
            restartButton.onClick.RemoveAllListeners();
            restartButton.onClick.AddListener(() => { RestartRun(); });
            SetupSelectable(restartButton);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveAllListeners();
            quitButton.onClick.AddListener(() =>
            {
                var gm = GameManager.Instance;
                if (gm != null) gm.QuitGame();
                else
                {
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }
            });
            SetupSelectable(quitButton);
        }
    }

    private void SetupSelectable(Selectable s)
    {
        if (s == null) return;
        var nav = s.navigation; nav.mode = Navigation.Mode.Automatic; s.navigation = nav;

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

    private bool MissingOverlayRefs()
    {
        return overlay == null || resumeButton == null || quitButton == null || restartButton == null;
    }

    private void CacheRoot() => _root = overlay ? overlay.gameObject : null;

    private void BuildDefaultOverlay()
    {
        var root = new GameObject("Pause Overlay (Auto)",
                                  typeof(RectTransform),
                                  typeof(Canvas),
                                  typeof(CanvasScaler),
                                  typeof(GraphicRaycaster),
                                  typeof(CanvasGroup));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        overlay = root.GetComponent<CanvasGroup>();

        var bg = new GameObject("Dim", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(root.transform, false);
        var bgImg = bg.GetComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.6f);
        var bgRT = bg.GetComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one; bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(root.transform, false);
        var pImg = panel.GetComponent<Image>();
        pImg.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);
        var pRT = panel.GetComponent<RectTransform>();
        pRT.sizeDelta = new Vector2(760, 460);
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

        var titleGO = new GameObject("Title", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        titleGO.transform.SetParent(panel.transform, false);
        var title = titleGO.GetComponent<Text>();
        title.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        title.text = "Paused";
        title.alignment = TextAnchor.MiddleCenter;
        title.fontSize = 36; title.color = Color.white;
        var titleLE = titleGO.GetComponent<LayoutElement>();
        titleLE.minHeight = 56; titleLE.preferredHeight = 56;

        var statsGO = new GameObject("Stats", typeof(RectTransform), typeof(Text), typeof(ContentSizeFitter), typeof(LayoutElement));
        statsGO.transform.SetParent(panel.transform, false);
        statsText = statsGO.GetComponent<Text>();
        statsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statsText.alignment = TextAnchor.UpperLeft;
        statsText.fontSize = 22;
        statsText.color = Color.white;
        statsText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statsText.verticalOverflow = VerticalWrapMode.Truncate;

        var statsFitter = statsGO.GetComponent<ContentSizeFitter>();
        statsFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var statsLE = statsGO.GetComponent<LayoutElement>();
        statsLE.flexibleHeight = 1;

        var buttonsGO = new GameObject("Buttons", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
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

        resumeButton = MakeButton(buttonsGO.transform, "Resume", () => Hide());
        restartButton = MakeButton(buttonsGO.transform, "Restart", () => RestartRun());
        quitButton = MakeButton(buttonsGO.transform, "Quit", () =>
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.QuitGame();
            else
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
        });

        CacheRoot();
    }

    private Button MakeButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);

        var img = go.GetComponent<Image>();
        img.color = new Color(0.22f, 0.22f, 0.22f, 1f);

        var le = go.GetComponent<LayoutElement>();
        le.minHeight = 60; le.preferredHeight = 64; le.flexibleWidth = 1;

        var btn = go.GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(onClick);

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

        SetupSelectable(btn);
        return btn;
    }

    private void Select(Selectable s)
    {
        if (EventSystem.current == null) return;
        if (s == null)
        {
            if (resumeButton != null) s = resumeButton;
            else if (restartButton != null) s = restartButton;
            else if (quitButton != null) s = quitButton;
        }
        if (s == null || !s.IsActive() || !s.interactable) return;

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

        Select(firstSelectedOverride ?? resumeButton ?? restartButton ?? quitButton);
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

    /// <summary>
    /// Force old Input Manager UI module; remove the new Input System UI module if present.
    /// </summary>
    private void EnsureEventSystem_ForceOld()
    {
        var es = FindFirstObjectByType<EventSystem>();
        if (es == null)
        {
            var esGO = new GameObject("EventSystem", typeof(EventSystem));
            es = esGO.GetComponent<EventSystem>();
        }

        // Ensure StandaloneInputModule exists
        if (es.GetComponent<StandaloneInputModule>() == null)
            es.gameObject.AddComponent<StandaloneInputModule>();

        // If the new input UI module is present, remove it to avoid conflicts
        var newUiType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if (newUiType != null)
        {
            var comp = es.GetComponent(newUiType) as Behaviour;
            if (comp != null)
            {
                Debug.Log("[EscPauseUI] Removing InputSystemUIInputModule to prevent input conflicts (forcing old UI input).");
                Destroy(comp);
            }
        }
    }
}

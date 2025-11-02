using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

#if TMP_PRESENT || TEXTMESHPRO || TMPRO
using TMPro;
#endif

[DisallowMultipleComponent]
public class StatsUIController : MonoBehaviour
{
    public enum DockMode { TopLeft, TopRight, UnderTarget }

    // ---------- Auto-Build ----------
    [Header("Auto-Build")]
    public bool autoBuildIfMissing = true;
    public Canvas targetCanvas;
    public int autoCanvasSortingOrder = 50;

    // ---------- Panel Style ----------
    [Header("Panel Style")]
    public Vector4 panelPadding = new Vector4(10, 10, 10, 10);
    public Color panelBg = new Color(0f, 0f, 0f, 0.35f);
    public float panelSpacing = 6f;

    [Tooltip("Used when DockMode is not UnderTarget.")]
    public bool anchorTopLeft = true;
    public Vector2 panelOffset = new Vector2(12, -12);

    // ---------- Docking ----------
    [Header("Docking")]
    public DockMode dockMode = DockMode.UnderTarget;
    public RectTransform targetToDockUnder;
    public float dockGapY = 8f;
    public bool dockAlignLeftEdge = true;
    public bool dockMatchTargetWidth = true;
    public float dockHorizontalInset = 0f;

    // ---------- UI References ----------
    [Header("UI References (optional)")]
    public RectTransform panelRoot;
    public Text scoreText, waveText, killsText, coinsText, friendliesText, materialsText, bossKillsText;
#if TMP_PRESENT || TEXTMESHPRO || TMPRO
    public TextMeshProUGUI scoreTMP, waveTMP, killsTMP, coinsTMP, friendliesTMP, materialsTMP, bossKillsTMP;
#endif

    // ---------- Layout Sizing / Bottom Pin ----------
    [Header("Bottom Pin (Helicopter Row)")]
    public bool pinHelicopterToBottom = true;
    public float panelMinHeight = 0f;
    public string bottomSpacerName = "Heli Bottom Spacer";

    // ---------- Behavior ----------
    [Header("Behavior")]
    public KeyCode legacyToggleKey = KeyCode.F1;

    // ---------- Diagnostics ----------
    [Header("Diagnostics")]
    public bool logVerbose = true;
    public bool forceVisibleOnEnable = true;

    // ---------- Integrations ----------
    [Header("Integrations")]
    public bool autoPlaceHelicopterUI = true;

    // ---------- Internals ----------
    private CanvasGroup _cg;
    private RectTransform _canvasRT;
    private LayoutElement _panelLayout;
    private VerticalLayoutGroup _vg;
    private RectTransform _bottomSpacerRT;

    private bool _subscribed = false;
    private float _retryHookTimer = 0f;
    private const float RetryHookInterval = 0.25f; // retry until GM exists

    // Cached values
    private int _vScore, _vWave, _vKills, _vCoins, _vFriendlies, _vMaterials, _vBossKills;

    // =====================================================================
    private void Awake()
    {
        if (autoBuildIfMissing)
        {
            EnsureCanvas();
            EnsurePanelAndTexts();
            if (logVerbose) Debug.Log("[StatsUI] Auto-built canvas/panel/texts.");
        }

        if (panelRoot != null && _cg == null)
            _cg = panelRoot.GetComponent<CanvasGroup>() ?? panelRoot.gameObject.AddComponent<CanvasGroup>();

        if (panelRoot != null)
            _panelLayout = panelRoot.GetComponent<LayoutElement>() ?? panelRoot.gameObject.AddComponent<LayoutElement>();

        if (dockMode != DockMode.UnderTarget && panelRoot != null)
            SetTopCornerAnchors(panelRoot, anchorTopLeft);

        ApplyPanelMinHeightSetting();

        if (autoPlaceHelicopterUI)
            MoveHelicopterRowToBottomAndPin();

        if (forceVisibleOnEnable) EnsureVisible();
    }

    private void OnEnable()
    {
        RefreshAll();
        RepositionDock();
        ApplyPanelMinHeightSetting();
        if (autoPlaceHelicopterUI) MoveHelicopterRowToBottomAndPin();

        // First attempt to hook immediately
        TryHookToGameManager();
        _retryHookTimer = 0f; // start retries if needed
    }

    private void OnDisable()
    {
        UnhookFromGameManager();
    }

    private void Update()
    {
#if !ENABLE_INPUT_SYSTEM
        if (legacyToggleKey != KeyCode.None && Input.GetKeyDown(legacyToggleKey))
            ToggleVisible();
#endif
        if (dockMode == DockMode.UnderTarget) RepositionDock();

        // Late subscription: keep trying until GM appears
        if (!_subscribed)
        {
            _retryHookTimer += Time.unscaledDeltaTime;
            if (_retryHookTimer >= RetryHookInterval)
            {
                _retryHookTimer = 0f;
                TryHookToGameManager();
            }
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (panelRoot != null)
        {
            var vg = panelRoot.GetComponent<VerticalLayoutGroup>();
            if (vg != null) vg.spacing = panelSpacing;
        }
        ApplyPanelMinHeightSetting();
    }
#endif

    // =====================================================================
    // GM wiring
    // =====================================================================
    private void TryHookToGameManager()
    {
        var gm = GameManager.Instance;
        if (gm == null)
        {
            if (logVerbose) Debug.Log("[StatsUI] Waiting for GameManager.Instance...");
            return;
        }
        if (_subscribed) return;

        gm.OnCoinsChanged += HandleCoins;
        gm.OnMaterialsChanged += HandleMaterials;
        gm.OnScoreChanged += HandleScore;
        gm.OnWaveChanged += HandleWave;
        gm.OnEnemiesKilledChanged += HandleKills;
        gm.OnBossKillsChanged += HandleBossKills;
        gm.OnFriendliesRescuedChanged += HandleFriendlies;

        _subscribed = true;
        if (logVerbose) Debug.Log("[StatsUI] Subscribed to GameManager events.");

        // Pull current values + ask for a rebroadcast
        _vScore = gm.Score;
        _vWave = gm.CurrentWave;
        _vKills = gm.EnemiesKilled;
        _vBossKills = gm.BossKillCount;
        _vFriendlies = gm.FriendliesRescued;
        _vCoins = gm.coins;
        _vMaterials = gm.materials;
        ApplyAll();
        gm.RebroadcastAllStats();
    }

    private void UnhookFromGameManager()
    {
        if (!_subscribed) return;
        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.OnCoinsChanged -= HandleCoins;
            gm.OnMaterialsChanged -= HandleMaterials;
            gm.OnScoreChanged -= HandleScore;
            gm.OnWaveChanged -= HandleWave;
            gm.OnEnemiesKilledChanged -= HandleKills;
            gm.OnBossKillsChanged -= HandleBossKills;
            gm.OnFriendliesRescuedChanged -= HandleFriendlies;
        }
        _subscribed = false;
    }

    // Event handlers
    private void HandleCoins(int v) { _vCoins = v; SetCoins(v); }
    private void HandleMaterials(int v) { _vMaterials = v; SetMaterials(v); }
    private void HandleScore(int v) { _vScore = v; SetScore(v); }
    private void HandleWave(int v) { _vWave = v; SetWave(v); }
    private void HandleKills(int v) { _vKills = v; SetKills(v); }
    private void HandleBossKills(int v) { _vBossKills = v; SetBossKills(v); }
    private void HandleFriendlies(int v) { _vFriendlies = v; SetFriendlies(v); }

    private void ApplyAll()
    {
        SetScore(_vScore);
        SetWave(_vWave);
        SetKills(_vKills);
        SetCoins(_vCoins);
        SetFriendlies(_vFriendlies);
        SetMaterials(_vMaterials);
        SetBossKills(_vBossKills);
    }

    // =====================================================================
    // UI building / layout
    // =====================================================================
    private void EnsureCanvas()
    {
        if (targetCanvas == null) targetCanvas = UnityEngine.Object.FindObjectOfType<Canvas>();
        if (targetCanvas == null)
        {
            var canvasGO = new GameObject("HUD Canvas (Auto)");
            targetCanvas = canvasGO.AddComponent<Canvas>();
            targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            targetCanvas.sortingOrder = autoCanvasSortingOrder;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            canvasGO.AddComponent<GraphicRaycaster>();

            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
        }
        _canvasRT = targetCanvas.transform as RectTransform;
    }

    private void EnsurePanelAndTexts()
    {
        if (targetCanvas == null) return;

        if (panelRoot == null)
        {
            var panelGO = new GameObject("Stats Panel");
            panelGO.transform.SetParent(targetCanvas.transform, false);
            panelRoot = panelGO.AddComponent<RectTransform>();

            var img = panelGO.AddComponent<Image>();
            img.color = panelBg;

            _vg = panelGO.AddComponent<VerticalLayoutGroup>();
            _vg.childControlWidth = true;
            _vg.childControlHeight = true;
            _vg.childForceExpandWidth = false;
            _vg.childForceExpandHeight = false;
            _vg.childAlignment = TextAnchor.UpperLeft;
            _vg.padding = new RectOffset((int)panelPadding.x, (int)panelPadding.z, (int)panelPadding.y, (int)panelPadding.w);
            _vg.spacing = panelSpacing;

            var fitter = panelGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = (panelMinHeight > 0f) ? ContentSizeFitter.FitMode.Unconstrained
                                                       : ContentSizeFitter.FitMode.PreferredSize;

            _panelLayout = panelGO.AddComponent<LayoutElement>();
        }
        else
        {
            _vg = panelRoot.GetComponent<VerticalLayoutGroup>();
            if (_vg != null)
            {
                _vg.spacing = panelSpacing;
                _vg.childForceExpandHeight = false;
            }

            var fitter = panelRoot.GetComponent<ContentSizeFitter>();
            if (fitter != null)
                fitter.verticalFit = (panelMinHeight > 0f) ? ContentSizeFitter.FitMode.Unconstrained
                                                           : ContentSizeFitter.FitMode.PreferredSize;
        }

        SetTopCornerAnchors(panelRoot, anchorTopLeft);

#if TMP_PRESENT || TEXTMESHPRO || TMPRO
        System.Func<string, TextMeshProUGUI> mkTMP = (label) =>
        {
            var go = new GameObject(label);
            go.transform.SetParent(panelRoot, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 20;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.alignment = TextAlignmentOptions.Left;
            return text;
        };
#endif

        System.Func<string, Text> mkUGUIText = (label) =>
        {
            var go = new GameObject(label);
            go.transform.SetParent(panelRoot, false);
            var text = go.AddComponent<Text>();
            text.text = label;
            text.fontSize = 18;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return text;
        };

#if TMP_PRESENT || TEXTMESHPRO || TMPRO
        if (scoreTMP == null && scoreText == null)           scoreTMP      = mkTMP("Score: 0");
        if (waveTMP == null && waveText == null)             waveTMP       = mkTMP("Wave: 1");
        if (killsTMP == null && killsText == null)           killsTMP      = mkTMP("Kills: 0");
        if (coinsTMP == null && coinsText == null)           coinsTMP      = mkTMP("Coins: 0");
        if (friendliesTMP == null && friendliesText == null) friendliesTMP = mkTMP("Friendlies: 0");
        if (materialsTMP == null && materialsText == null)   materialsTMP  = mkTMP("Materials: 0");
        if (bossKillsTMP == null && bossKillsText == null)   bossKillsTMP  = mkTMP("Boss Kills: 0");
#else
        if (scoreText == null) scoreText = mkUGUIText("Score: 0");
        if (waveText == null) waveText = mkUGUIText("Wave: 1");
        if (killsText == null) killsText = mkUGUIText("Kills: 0");
        if (coinsText == null) coinsText = mkUGUIText("Coins: 0");
        if (friendliesText == null) friendliesText = mkUGUIText("Friendlies: 0");
        if (materialsText == null) materialsText = mkUGUIText("Materials: 0");
        if (bossKillsText == null) bossKillsText = mkUGUIText("Boss Kills: 0");
#endif
    }

    private void ApplyPanelMinHeightSetting()
    {
        if (!panelRoot) return;

        var fitter = panelRoot.GetComponent<ContentSizeFitter>();
        if (fitter)
            fitter.verticalFit = (panelMinHeight > 0f) ? ContentSizeFitter.FitMode.Unconstrained
                                                       : ContentSizeFitter.FitMode.PreferredSize;

        _panelLayout = _panelLayout ?? panelRoot.GetComponent<LayoutElement>() ?? panelRoot.gameObject.AddComponent<LayoutElement>();
        _panelLayout.minHeight = (panelMinHeight > 0f) ? panelMinHeight : 0f;
    }

    private static void SetTopCornerAnchors(RectTransform rt, bool topLeft)
    {
        if (!rt) return;
        if (topLeft)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(12, -12);
        }
        else
        {
            rt.anchorMin = new Vector2(1, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-12, -12);
        }
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private void RepositionDock()
    {
        if (panelRoot == null || targetCanvas == null) return;

        bool canDock =
            dockMode == DockMode.UnderTarget &&
            targetToDockUnder != null &&
            targetToDockUnder.gameObject.activeInHierarchy;

        if (!canDock)
        {
            SetTopCornerAnchors(panelRoot, anchorTopLeft);
            panelRoot.anchoredPosition = panelOffset;
            return;
        }

        if (_canvasRT == null) _canvasRT = targetCanvas.transform as RectTransform;

        Vector3[] world = new Vector3[4];
        targetToDockUnder.GetWorldCorners(world); // 0=BL,1=TL,2=TR,3=BR

        Camera cam = (targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : targetCanvas.worldCamera;

        Vector2 tlScreen = RectTransformUtility.WorldToScreenPoint(cam, world[1]);
        Vector2 trScreen = RectTransformUtility.WorldToScreenPoint(cam, world[2]);
        Vector2 brScreen = RectTransformUtility.WorldToScreenPoint(cam, world[3]);

        Vector2 tlLocal, trLocal, brLocal;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, tlScreen, cam, out tlLocal);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, trScreen, cam, out trLocal);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, brScreen, cam, out brLocal);

        panelRoot.anchorMin = new Vector2(0, 1);
        panelRoot.anchorMax = new Vector2(0, 1);
        panelRoot.pivot = new Vector2(dockAlignLeftEdge ? 0f : 1f, 1f);

        Vector2 tlAnch = LocalCenterToTopLeftAnchored(tlLocal, _canvasRT);
        Vector2 trAnch = LocalCenterToTopLeftAnchored(trLocal, _canvasRT);
        Vector2 brAnch = LocalCenterToTopLeftAnchored(brLocal, _canvasRT);

        float targetWidth = Mathf.Abs(trAnch.x - tlAnch.x);
        float x = dockAlignLeftEdge ? tlAnch.x + dockHorizontalInset : trAnch.x - dockHorizontalInset;
        float y = brAnch.y - dockGapY;

        panelRoot.anchoredPosition = new Vector2(x, y);

        if (_panelLayout != null)
        {
            if (dockMatchTargetWidth)
            {
                float w = Mathf.Max(0f, targetWidth - (dockHorizontalInset * 2f));
                _panelLayout.minWidth = w;
                _panelLayout.preferredWidth = w;
            }
            else
            {
                _panelLayout.minWidth = -1;
                _panelLayout.preferredWidth = -1;
            }
        }

        panelRoot.localScale = Vector3.one;
        panelRoot.localRotation = Quaternion.identity;
    }

    private static Vector2 LocalCenterToTopLeftAnchored(Vector2 localCenter, RectTransform canvasRT)
    {
        var r = canvasRT.rect;
        return new Vector2(localCenter.x + (r.width * 0.5f),
                           localCenter.y - (r.height * 0.5f));
    }

    private void RefreshAll() { /* values now come from GameManager events */ }

    private void EnsureVisible()
    {
        if (_cg != null)
        {
            _cg.alpha = 1f;
            _cg.interactable = true;
            _cg.blocksRaycasts = false;
        }
        if (panelRoot != null) panelRoot.SetAsLastSibling();
        if (targetCanvas != null) targetCanvas.sortingOrder = autoCanvasSortingOrder;
    }

    private void ToggleVisible()
    {
        if (_cg == null) return;
        bool nowVisible = _cg.alpha < 0.5f;
        _cg.alpha = nowVisible ? 1f : 0f;
        _cg.interactable = nowVisible;
        _cg.blocksRaycasts = false;
    }

    private void MoveHelicopterRowToBottomAndPin()
    {
        if (!panelRoot) return;
        var heli = UnityEngine.Object.FindObjectOfType<HelicopterPartsUI>(true);
        if (!heli) return;

        var row = heli.GetComponent<RectTransform>();
        if (!row) return;

        if (row.parent != panelRoot) row.SetParent(panelRoot, false);
        row.SetAsLastSibling();

        var le = row.GetComponent<LayoutElement>() ?? row.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = false;
        if (le.minHeight < 20f) le.minHeight = 20f;

        row.localScale = Vector3.one;
        row.localRotation = Quaternion.identity;
        row.anchorMin = new Vector2(0f, 1f);
        row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(0f, 1f);
        row.anchoredPosition = Vector2.zero;
        row.offsetMin = Vector2.zero;
        row.offsetMax = Vector2.zero;

        if (pinHelicopterToBottom && panelMinHeight > 0f)
        {
            EnsureBottomSpacerExists();

            int heliIndex = row.GetSiblingIndex();
            if (_bottomSpacerRT != null)
            {
                if (_bottomSpacerRT.parent != panelRoot) _bottomSpacerRT.SetParent(panelRoot, false);
                int spacerIndex = Mathf.Max(0, heliIndex);
                _bottomSpacerRT.SetSiblingIndex(spacerIndex);
                row.SetAsLastSibling();
            }
        }
        else
        {
            RemoveBottomSpacerIfAny();
        }

        if (_vg == null) _vg = panelRoot.GetComponent<VerticalLayoutGroup>();
        if (_vg != null) _vg.spacing = panelSpacing;

        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRoot);
    }

    private void EnsureBottomSpacerExists()
    {
        if (!panelRoot) return;
        if (_bottomSpacerRT == null)
        {
            foreach (Transform c in panelRoot)
            {
                if (c.name == bottomSpacerName) { _bottomSpacerRT = c as RectTransform; break; }
            }
        }
        if (_bottomSpacerRT == null)
        {
            var go = new GameObject(bottomSpacerName);
            _bottomSpacerRT = go.AddComponent<RectTransform>();
            _bottomSpacerRT.SetParent(panelRoot, false);

            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = false;
            le.minHeight = 0f;
            le.preferredHeight = 0f;
            le.flexibleHeight = 1f;
        }
        else
        {
            var le = _bottomSpacerRT.GetComponent<LayoutElement>() ?? _bottomSpacerRT.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = false;
            le.minHeight = 0f;
            le.preferredHeight = 0f;
            le.flexibleHeight = 1f;
        }

        _bottomSpacerRT.localScale = Vector3.one;
        _bottomSpacerRT.localRotation = Quaternion.identity;
        _bottomSpacerRT.anchorMin = new Vector2(0f, 1f);
        _bottomSpacerRT.anchorMax = new Vector2(1f, 1f);
        _bottomSpacerRT.pivot = new Vector2(0f, 1f);
        _bottomSpacerRT.anchoredPosition = Vector2.zero;
        _bottomSpacerRT.offsetMin = Vector2.zero;
        _bottomSpacerRT.offsetMax = Vector2.zero;
        _bottomSpacerRT.sizeDelta = new Vector2(0f, 0f);
    }

    private void RemoveBottomSpacerIfAny()
    {
        if (_bottomSpacerRT == null || _bottomSpacerRT.parent != panelRoot) return;
        Destroy(_bottomSpacerRT.gameObject);
        _bottomSpacerRT = null;
    }

    // ============================ Text helpers (preprocessor-safe)
#if TMP_PRESENT || TEXTMESHPRO || TMPRO
    private static void SetTextPair(Text ugui, string value, TextMeshProUGUI tmp)
    {
        if (tmp != null) { tmp.text = value; return; }
        if (ugui != null) ugui.text = value;
    }
    private void SetScore(int v)      { SetTextPair(scoreText,      "Score: " + v,      scoreTMP); }
    private void SetWave(int v)       { SetTextPair(waveText,       "Wave: " + v,       waveTMP); }
    private void SetKills(int v)      { SetTextPair(killsText,      "Kills: " + v,      killsTMP); }
    private void SetCoins(int v)      { SetTextPair(coinsText,      "Coins: " + v,      coinsTMP); }
    private void SetFriendlies(int v) { SetTextPair(friendliesText, "Friendlies: " + v, friendliesTMP); }
    private void SetMaterials(int v)  { SetTextPair(materialsText,  "Materials: " + v,  materialsTMP); }
    private void SetBossKills(int v)  { SetTextPair(bossKillsText,  "Boss Kills: " + v, bossKillsTMP); }
#else
    private static void SetTextPair(Text ugui, string value)
    {
        if (ugui != null) ugui.text = value;
    }
    private void SetScore(int v) { SetTextPair(scoreText, "Score: " + v); }
    private void SetWave(int v) { SetTextPair(waveText, "Wave: " + v); }
    private void SetKills(int v) { SetTextPair(killsText, "Kills: " + v); }
    private void SetCoins(int v) { SetTextPair(coinsText, "Coins: " + v); }
    private void SetFriendlies(int v) { SetTextPair(friendliesText, "Friendlies: " + v); }
    private void SetMaterials(int v) { SetTextPair(materialsText, "Materials: " + v); }
    private void SetBossKills(int v) { SetTextPair(bossKillsText, "Boss Kills: " + v); }
#endif
}

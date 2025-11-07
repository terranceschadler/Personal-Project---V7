using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class UpgradeSelectionUI : MonoBehaviour
{
    [Header("UI Mode")]
    [SerializeField] private bool useFullUI = true;
    [SerializeField] private bool autoApplyIfNoUI = true;

    [Header("UI References (Optional - for Full UI Mode)")]
    [SerializeField] private GameObject selectionPanel;
    [SerializeField] private Transform upgradeOptionsContainer;
    [SerializeField] private GameObject upgradeOptionPrefab;
    [SerializeField] private TextMeshProUGUI titleText;

    [Header("UI Settings")]
    [SerializeField] private float optionSpacing = 30f;
    [SerializeField] private bool pauseGameOnSelection = true;
    [SerializeField] private KeyCode skipKey = KeyCode.Escape;

    private List<UpgradeData> currentUpgrades;
    private GameObject currentPlayer;
    private List<GameObject> spawnedOptions = new List<GameObject>();
    private IntegratedUpgradeSystem upgradeSystem;

    // --- New: track whether we hold a GM pause lock ---
    private bool _pauseLockHeld = false;

    private void Awake()
    {
        upgradeSystem = FindObjectOfType<IntegratedUpgradeSystem>();
        if (selectionPanel != null) selectionPanel.SetActive(false);
    }

    private void OnDisable()
    {
        // Safety: never leak a pause lock
        ReleasePauseLockIfHeld();
    }

    private void Update()
    {
        if (selectionPanel != null && selectionPanel.activeSelf)
        {
            if (Input.GetKeyDown(skipKey))
            {
                CloseSelection();
            }
        }
    }

    public void ShowUpgradeSelection(List<UpgradeData> upgrades, GameObject player)
    {
        if (upgrades == null || upgrades.Count == 0)
        {
            Debug.LogWarning("[UpgradeSelectionUI] No upgrades to show!");
            return;
        }

        currentUpgrades = upgrades;
        currentPlayer = player;

        bool canUseFullUI = useFullUI &&
                            selectionPanel != null &&
                            upgradeOptionsContainer != null &&
                            upgradeOptionPrefab != null;

        if (canUseFullUI)
        {
            ShowFullUI();
        }
        else if (autoApplyIfNoUI)
        {
            Debug.Log($"[UpgradeSelectionUI] Auto-applying: {upgrades[0].upgradeName}");
            OnUpgradeSelected(upgrades[0]);
        }
        else
        {
            Debug.Log("[UpgradeSelectionUI] Available upgrades:");
            for (int i = 0; i < upgrades.Count; i++)
                Debug.Log($"  {i + 1}. {upgrades[i].upgradeName} - {upgrades[i].archetype} ({upgrades[i].tier})");
        }
    }

    private void ShowFullUI()
    {
        if (selectionPanel != null)
        {
            selectionPanel.SetActive(true);

            // Ensure on top
            Canvas panelCanvas = selectionPanel.GetComponent<Canvas>();
            if (panelCanvas == null)
            {
                panelCanvas = selectionPanel.AddComponent<Canvas>();
                panelCanvas.overrideSorting = true;
            }
            panelCanvas.sortingOrder = 1000;

            if (selectionPanel.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                selectionPanel.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        // Use GameManager’s external pause so its watchdog keeps us paused
        if (pauseGameOnSelection)
            AcquirePauseLock();

        GenerateUpgradeOptions();
    }

    private void GenerateUpgradeOptions()
    {
        ClearOptions();

        if (upgradeOptionsContainer == null || upgradeOptionPrefab == null)
        {
            Debug.LogError("[UpgradeSelectionUI] UI references not set up properly!");
            return;
        }

        if (titleText != null) titleText.text = "Choose an Upgrade";

        for (int i = 0; i < currentUpgrades.Count; i++)
            CreateUpgradeOption(currentUpgrades[i], i);

        AdjustLayout();
    }

    private void CreateUpgradeOption(UpgradeData upgrade, int index)
    {
        GameObject option = Instantiate(upgradeOptionPrefab, upgradeOptionsContainer);
        spawnedOptions.Add(option);

        UpgradeOptionUI optionUI = option.GetComponent<UpgradeOptionUI>();
        if (optionUI == null) optionUI = option.AddComponent<UpgradeOptionUI>();

        optionUI.SetupOption(upgrade, () => OnUpgradeSelected(upgrade));
    }

    private void AdjustLayout()
    {
        if (upgradeOptionsContainer == null) return;

        RectTransform containerRect = upgradeOptionsContainer.GetComponent<RectTransform>();
        if (containerRect != null)
        {
            containerRect.anchorMin = new Vector2(0.5f, 0.5f);
            containerRect.anchorMax = new Vector2(0.5f, 0.5f);
            containerRect.pivot = new Vector2(0.5f, 0.5f);
            containerRect.anchoredPosition = Vector2.zero;

            float totalWidth = (700f * 3) + (30f * 2);
            containerRect.sizeDelta = new Vector2(totalWidth, 1000f);
        }

        HorizontalLayoutGroup layout = upgradeOptionsContainer.GetComponent<HorizontalLayoutGroup>();
        if (layout == null) layout = upgradeOptionsContainer.gameObject.AddComponent<HorizontalLayoutGroup>();

        layout.spacing = optionSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.childScaleWidth = false;
        layout.childScaleHeight = false;

        ContentSizeFitter fitter = upgradeOptionsContainer.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = upgradeOptionsContainer.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
    }

    private void OnUpgradeSelected(UpgradeData upgrade)
    {
        if (upgradeSystem != null)
        {
            upgradeSystem.ApplyUpgradeToWeapon(upgrade);
        }
        else
        {
            Debug.LogError("[UpgradeSelectionUI] No IntegratedUpgradeSystem found!");
        }

        CloseSelection();
    }

    private void CloseSelection()
    {
        ClearOptions();

        if (selectionPanel != null)
            selectionPanel.SetActive(false);

        // Release the pause we took when opening
        ReleasePauseLockIfHeld();

        currentUpgrades = null;
        currentPlayer = null;
    }

    private void ClearOptions()
    {
        foreach (GameObject option in spawnedOptions)
            if (option != null) Destroy(option);
        spawnedOptions.Clear();
    }

    // ---------- New helpers: pause via GameManager, with fallback ----------
    private void AcquirePauseLock()
    {
        if (_pauseLockHeld) return;

        var gm = GameManager.Instance; // requires GameManager in scene
        if (gm != null)
        {
            gm.PushExternalPause(); // GM will keep Time.timeScale = 0 while lock > 0
            _pauseLockHeld = true;
        }
        else
        {
            // Fallback if GM missing
            Time.timeScale = 0f;
            AudioListener.pause = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _pauseLockHeld = true;
        }
    }

    private void ReleasePauseLockIfHeld()
    {
        if (!_pauseLockHeld) return;

        var gm = GameManager.Instance;
        if (gm != null)
        {
            gm.PopExternalPause();
        }
        else
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        _pauseLockHeld = false;
    }
}

using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;

/// <summary>
/// Upgrade option card UI for displaying and selecting upgrades.
/// This version automatically generates its own UI elements.
/// </summary>
[RequireComponent(typeof(Button))]
public class UpgradeOptionUI : MonoBehaviour
{
    [Header("Auto-Generated Components")]
    private Image backgroundImage;
    private Image iconImage;
    private TextMeshProUGUI nameText;
    private TextMeshProUGUI descriptionText;
    private TextMeshProUGUI tierText;
    private TextMeshProUGUI archetypeText;
    private Image tierBorder;
    private Button button;
    
    [Header("Layout Settings")]
    [SerializeField] private Vector2 cardSize = new Vector2(700f, 980f); // 70% of 1000x1400 - fits 3 cards
    [SerializeField] private float padding = 35f; // 70% of 50
    [SerializeField] private float iconSize = 168f; // 70% of 240
    
    [Header("Font Sizes")]
    [SerializeField] private int tierFontSize = 44;      // Was 22, now doubled
    [SerializeField] private int nameFontSize = 52;      // Was 26, now doubled
    [SerializeField] private int descriptionFontSize = 40; // Was 20, now doubled
    [SerializeField] private int archetypeFontSize = 36;   // Was 18, now doubled
    
    private bool uiGenerated = false;
    
    private void Awake()
    {
        button = GetComponent<Button>();
        
        // Don't generate UI in Awake if this is a prefab
        // It will be generated when SetupOption is called
    }
    
    /// <summary>
    /// Sets up the upgrade option with data and click handler
    /// </summary>
    public void SetupOption(UpgradeData upgrade, UnityAction onClickAction)
    {
        if (upgrade == null)
        {
            Debug.LogError("[UpgradeOptionUI] Upgrade data is null!");
            return;
        }
        
        // Generate UI if not already done
        if (!uiGenerated)
        {
            GenerateUI();
            uiGenerated = true;
        }
        
        // Set tier border color
        if (tierBorder != null)
        {
            tierBorder.color = upgrade.tierColor;
            Outline outline = tierBorder.GetComponent<Outline>();
            if (outline != null)
                outline.effectColor = upgrade.tierColor;
        }
        
        // Set icon
        if (iconImage != null && upgrade.icon != null)
        {
            iconImage.sprite = upgrade.icon;
            iconImage.color = upgrade.tierColor;
        }
        else if (iconImage != null)
        {
            // Use colored square if no icon
            iconImage.color = upgrade.tierColor;
        }
        
        // Set tier text
        if (tierText != null)
        {
            tierText.text = upgrade.tier.ToString().ToUpper();
            tierText.color = Color.black; // Black text
        }
        
        // Set name
        if (nameText != null)
        {
            nameText.text = upgrade.upgradeName;
            nameText.color = Color.black; // Black text
        }
        
        // Set description
        if (descriptionText != null)
        {
            descriptionText.text = upgrade.GetFormattedDescription();
            descriptionText.color = Color.black; // Black text
        }
        
        // Set archetype
        if (archetypeText != null)
        {
            archetypeText.text = upgrade.archetype.ToString();
            archetypeText.color = new Color(0.2f, 0.2f, 0.2f); // Dark gray text
        }
        
        // Set button click action
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClickAction);
        }
        
        // Add hover effects
        AddHoverEffects(upgrade);
    }
    
    private void GenerateUI()
    {
        // Set card size
        RectTransform rectTransform = GetComponent<RectTransform>();
        if (rectTransform == null)
            rectTransform = gameObject.AddComponent<RectTransform>();
        rectTransform.sizeDelta = cardSize;
        
        // Create background
        backgroundImage = gameObject.GetComponent<Image>();
        if (backgroundImage == null)
            backgroundImage = gameObject.AddComponent<Image>();
        backgroundImage.color = new Color(0.95f, 0.95f, 0.95f, 1f); // Light background for black text
        
        // Create tier border (outline)
        GameObject borderObj = new GameObject("TierBorder");
        borderObj.transform.SetParent(transform);
        tierBorder = borderObj.AddComponent<Image>();
        tierBorder.color = Color.clear; // Will be set by upgrade data
        RectTransform borderRect = borderObj.GetComponent<RectTransform>();
        borderRect.anchorMin = Vector2.zero;
        borderRect.anchorMax = Vector2.one;
        borderRect.sizeDelta = Vector2.zero;
        borderRect.anchoredPosition = Vector2.zero;
        
        Outline outline = borderObj.AddComponent<Outline>();
        outline.effectDistance = new Vector2(4, -4);
        outline.useGraphicAlpha = false;
        
        // Create icon
        GameObject iconObj = new GameObject("Icon");
        iconObj.transform.SetParent(transform);
        iconImage = iconObj.AddComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.color = Color.white;
        RectTransform iconRect = iconObj.GetComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 1f);
        iconRect.anchorMax = new Vector2(0.5f, 1f);
        iconRect.pivot = new Vector2(0.5f, 1f);
        iconRect.anchoredPosition = new Vector2(0, -padding);
        iconRect.sizeDelta = new Vector2(iconSize, iconSize);
        
        // Create tier text
        GameObject tierObj = new GameObject("TierText");
        tierObj.transform.SetParent(transform);
        tierText = tierObj.AddComponent<TextMeshProUGUI>();
        tierText.alignment = TextAlignmentOptions.Center;
        tierText.fontSize = tierFontSize;
        tierText.fontStyle = FontStyles.Bold;
        tierText.color = Color.black;
        tierText.enableAutoSizing = true; // Auto-size to fit
        tierText.fontSizeMin = 20;
        tierText.fontSizeMax = tierFontSize;
        RectTransform tierRect = tierObj.GetComponent<RectTransform>();
        tierRect.anchorMin = new Vector2(0.5f, 1f);
        tierRect.anchorMax = new Vector2(0.5f, 1f);
        tierRect.pivot = new Vector2(0.5f, 1f);
        tierRect.anchoredPosition = new Vector2(0, -padding - iconSize - 20);
        tierRect.sizeDelta = new Vector2(cardSize.x - padding * 2, 70);
        
        // Create name text
        GameObject nameObj = new GameObject("NameText");
        nameObj.transform.SetParent(transform);
        nameText = nameObj.AddComponent<TextMeshProUGUI>();
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.fontSize = nameFontSize;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = Color.black;
        nameText.enableAutoSizing = true; // Auto-size to fit
        nameText.fontSizeMin = 28;
        nameText.fontSizeMax = nameFontSize;
        nameText.enableWordWrapping = false; // No wrapping - auto-size instead
        RectTransform nameRect = nameObj.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0.5f, 1f);
        nameRect.anchorMax = new Vector2(0.5f, 1f);
        nameRect.pivot = new Vector2(0.5f, 1f);
        nameRect.anchoredPosition = new Vector2(0, -padding - iconSize - 105);
        nameRect.sizeDelta = new Vector2(cardSize.x - padding * 2, 100);
        
        // Create description text - Auto-sizing to prevent wrapping
        GameObject descObj = new GameObject("DescriptionText");
        descObj.transform.SetParent(transform);
        descriptionText = descObj.AddComponent<TextMeshProUGUI>();
        descriptionText.alignment = TextAlignmentOptions.TopLeft;
        descriptionText.fontSize = descriptionFontSize;
        descriptionText.color = Color.black;
        descriptionText.enableAutoSizing = true; // Auto-size to fit
        descriptionText.fontSizeMin = 24; // Minimum readable size
        descriptionText.fontSizeMax = descriptionFontSize;
        descriptionText.enableWordWrapping = true; // Allow wrapping for long descriptions
        RectTransform descRect = descObj.GetComponent<RectTransform>();
        descRect.anchorMin = new Vector2(0.5f, 0f);
        descRect.anchorMax = new Vector2(0.5f, 1f);
        descRect.pivot = new Vector2(0.5f, 1f);
        descRect.anchoredPosition = new Vector2(0, -padding - iconSize - 220);
        descRect.sizeDelta = new Vector2(cardSize.x - padding * 2, 500);
        
        // Create archetype text
        GameObject archetypeObj = new GameObject("ArchetypeText");
        archetypeObj.transform.SetParent(transform);
        archetypeText = archetypeObj.AddComponent<TextMeshProUGUI>();
        archetypeText.alignment = TextAlignmentOptions.Center;
        archetypeText.fontSize = archetypeFontSize;
        archetypeText.fontStyle = FontStyles.Italic;
        archetypeText.color = new Color(0.2f, 0.2f, 0.2f);
        archetypeText.enableAutoSizing = true; // Auto-size to fit
        archetypeText.fontSizeMin = 18;
        archetypeText.fontSizeMax = archetypeFontSize;
        RectTransform archetypeRect = archetypeObj.GetComponent<RectTransform>();
        archetypeRect.anchorMin = new Vector2(0.5f, 0f);
        archetypeRect.anchorMax = new Vector2(0.5f, 0f);
        archetypeRect.pivot = new Vector2(0.5f, 0f);
        archetypeRect.anchoredPosition = new Vector2(0, padding);
        archetypeRect.sizeDelta = new Vector2(cardSize.x - padding * 2, 65);
    }
    
    private void AddHoverEffects(UpgradeData upgrade)
    {
        if (button == null) return;
        
        // Set button colors based on tier with light background
        var colors = button.colors;
        colors.normalColor = new Color(0.95f, 0.95f, 0.95f, 1f); // Light gray
        colors.highlightedColor = upgrade.tierColor * 0.8f; // Slightly darker tier color
        colors.pressedColor = upgrade.tierColor * 0.6f; // Even darker when pressed
        colors.selectedColor = upgrade.tierColor * 0.7f;
        colors.disabledColor = new Color(0.7f, 0.7f, 0.7f, 0.5f);
        button.colors = colors;
    }
}

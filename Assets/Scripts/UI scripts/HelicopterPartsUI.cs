using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class HelicopterPartsUI : MonoBehaviour
{
    [Header("UI (either Text or TMP)")]
    public Text partsText; // optional
#if TMP_PRESENT || TEXTMESHPRO || TMPRO
    public TMPro.TextMeshProUGUI partsTMP; // optional
#endif
    [Tooltip("Prefix for the counter.")]
    public string labelPrefix = "Helicopter Parts ";

    [Header("Required Parts Control")]
    [Min(0)] public int desiredRequiredParts = 3;
    public bool applyDesiredToGameManager = true;

    private GameManager gm;
    private bool subscribed;
    private bool _complete;

    private void Awake()
    {
        if (!partsText) partsText = GetComponentInChildren<Text>(true);
        if (partsText) partsText.alignment = TextAnchor.MiddleLeft;
#if TMP_PRESENT || TEXTMESHPRO || TMPRO
        if (!partsTMP) partsTMP = GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (partsTMP) partsTMP.alignment = TMPro.TextAlignmentOptions.Left;
#endif
    }

    private void OnEnable()
    {
        Hook();
        SceneManager.sceneLoaded += OnSceneLoaded;
        RefreshImmediate();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Unhook();
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        Hook();
        RefreshImmediate();
    }

    private void Hook()
    {
        if (gm != null) return;
        gm = GameManager.Instance ?? FindFirstObjectByType<GameManager>();
        if (!gm) return;

        if (applyDesiredToGameManager)
            gm.SetRequiredHelicopterParts(desiredRequiredParts);

        if (!subscribed)
        {
            gm.OnHelicopterPartCollected += _ => RefreshImmediate();
            gm.OnHelicopterProgressChanged += OnProgressChanged;
            subscribed = true;
        }
    }

    private void Unhook()
    {
        if (gm != null && subscribed)
        {
            gm.OnHelicopterPartCollected -= _ => RefreshImmediate();
            gm.OnHelicopterProgressChanged -= OnProgressChanged;
        }
        subscribed = false;
        gm = null;
    }

    private void OnProgressChanged(int collected, int required)
    {
        if (required <= 0) required = desiredRequiredParts;
        Render(collected, required);
    }

    public void RefreshImmediate()
    {
        if (!gm) { Render(0, desiredRequiredParts); return; }
        var (c, r) = gm.GetHelicopterProgress();
        if (r <= 0) r = desiredRequiredParts;
        Render(c, r);
    }

    private void Render(int collected, int required)
    {
        string s = $"{labelPrefix}{collected}/{required}";
#if TMP_PRESENT || TEXTMESHPRO || TMPRO
        if (partsTMP) partsTMP.text = s;
        else if (partsText) partsText.text = s;
#else
        if (partsText) partsText.text = s;
#endif
        bool done = (required > 0 && collected >= required);
        if (done != _complete) _complete = done;
    }
}

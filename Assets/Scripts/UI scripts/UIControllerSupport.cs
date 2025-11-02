using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

[DefaultExecutionOrder(-1000)]
public class UIControllerSupport : MonoBehaviour
{
    [Header("Focus")]
    public Selectable firstSelected;          // set this in Inspector (e.g., your top/left button)
    public bool keepFocusAlive = true;        // reselect if focus becomes null while navigating

    [Header("Input Module")]
    [Tooltip("If true, force legacy StandaloneInputModule (uses InputManager Horizontal/Vertical/Submit/Cancel).")]
    public bool forceLegacyStandalone = true; // safest when you already set up Input Manager axes

    void Awake() { EnsureEventSystem(); }
    void OnEnable() { SelectFirst(); }

    void Update()
    {
        if (!keepFocusAlive || EventSystem.current == null) return;

        if (EventSystem.current.currentSelectedGameObject == null && AnyNavOrSubmitPressed())
            SelectFirst();
    }

    void SelectFirst()
    {
        if (EventSystem.current == null || firstSelected == null) return;
        EventSystem.current.SetSelectedGameObject(firstSelected.gameObject);
        firstSelected.Select();
    }

    bool AnyNavOrSubmitPressed()
    {
        // Old Input Manager axes (left stick / DPad if mapped), plus A/B as Submit/Cancel
        float h = 0f, v = 0f;
        try { h = Input.GetAxisRaw("Horizontal"); v = Input.GetAxisRaw("Vertical"); } catch { }
        bool submit = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.JoystickButton0);
        bool cancel = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1);
        return Mathf.Abs(h) > 0.1f || Mathf.Abs(v) > 0.1f || submit || cancel;
    }

    void EnsureEventSystem()
    {
        var es = EventSystem.current;
        if (es == null)
        {
            var go = new GameObject("EventSystem");
            es = go.AddComponent<EventSystem>();
        }

#if ENABLE_INPUT_SYSTEM
        if (forceLegacyStandalone)
        {
            if (es.GetComponent<InputSystemUIInputModule>() != null)
                Destroy(es.GetComponent<InputSystemUIInputModule>());
            if (es.GetComponent<StandaloneInputModule>() == null)
                es.gameObject.AddComponent<StandaloneInputModule>(); // uses Horizontal/Vertical/Submit/Cancel
        }
        else
        {
            if (es.GetComponent<StandaloneInputModule>() != null)
                Destroy(es.GetComponent<StandaloneInputModule>());
            if (es.GetComponent<InputSystemUIInputModule>() == null)
                es.gameObject.AddComponent<InputSystemUIInputModule>(); // requires a proper UI actions setup
        }
#else
        if (es.GetComponent<StandaloneInputModule>() == null)
            es.gameObject.AddComponent<StandaloneInputModule>();
#endif
    }
}

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
        float h = 0f, v = 0f;
        bool submit = false;
        bool cancel = false;

#if ENABLE_INPUT_SYSTEM
        // New Input System
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            var key = UnityEngine.InputSystem.Keyboard.current;
            submit = key.enterKey.wasPressedThisFrame || key.numpadEnterKey.wasPressedThisFrame;
            cancel = key.escapeKey.wasPressedThisFrame;
        }
        if (UnityEngine.InputSystem.Gamepad.current != null)
        {
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad.buttonSouth.wasPressedThisFrame) submit = true; // A button
            if (pad.buttonEast.wasPressedThisFrame) cancel = true;  // B button
        }
#else
        // Old Input Manager axes (left stick / DPad if mapped), plus A/B as Submit/Cancel
        try { h = Input.GetAxisRaw("Horizontal"); v = Input.GetAxisRaw("Vertical"); } catch { }
        submit = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.JoystickButton0);
        cancel = Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1);
#endif
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

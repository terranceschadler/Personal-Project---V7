using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class UIBootstrapper : MonoBehaviour
{
    private static UIBootstrapper _instance;

    [Header("Optional Prefabs (if you want auto-spawn)")]
    [Tooltip("EscPauseUI root prefab (CanvasGroup + buttons). Can be empty if EscPauseUI is marked DontDestroy and already exists.")]
    public GameObject escPauseUIPrefab;
    [Tooltip("WinUIController root prefab.")]
    public GameObject winUIPrefab;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureCanvasAndEventSystem();

        // Make sure EscPauseUI exists or rebuilds
        if (EscPauseUI.Instance == null)
        {
            if (escPauseUIPrefab != null)
            {
                Debug.Log("[UIBootstrapper] Spawning EscPauseUI from prefab after scene load.");
                Instantiate(escPauseUIPrefab);
            }
        }
        else
        {
            EscPauseUI.Instance.RehookAfterSceneLoad();
        }

        // Make sure WinUIController exists or rebuilds
        if (WinUIController.Instance == null)
        {
            if (winUIPrefab != null)
            {
                Debug.Log("[UIBootstrapper] Spawning WinUIController from prefab after scene load.");
                Instantiate(winUIPrefab);
            }
        }
        else
        {
            WinUIController.Instance.RehookAfterSceneLoad();
        }
    }

    private static void EnsureCanvasAndEventSystem()
    {
        // Canvas
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.Log("[UIBootstrapper] No Canvas found. Creating a default Screen Space - Overlay Canvas.");
            GameObject canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Optional: Scale with screen size
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
        }

        // EventSystem
        EventSystem es = FindObjectOfType<EventSystem>();
        if (es == null)
        {
            Debug.Log("[UIBootstrapper] No EventSystem found. Creating one.");
            GameObject esGO = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            // StandaloneInputModule is fine for both kb/m and most pads (Legacy Input)
        }
    }
}

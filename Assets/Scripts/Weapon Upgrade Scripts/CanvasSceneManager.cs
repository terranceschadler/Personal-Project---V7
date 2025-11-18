using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Ensures Canvas-Game stays in the active scene and doesn't get moved to DontDestroyOnLoad.
/// Place this on Canvas-Game GameObject.
/// 
/// This fixes the UI duplication issue on level restart by ensuring the Canvas
/// is destroyed and recreated with each scene reload, rather than persisting.
/// </summary>
public class CanvasSceneManager : MonoBehaviour
{
    [Header("Reparenting Settings")]
    [Tooltip("Automatically move this canvas to the active scene if it ends up in DontDestroyOnLoad")]
    public bool autoReparentToActiveScene = true;
    
    [Tooltip("Check every frame (use if something keeps moving the canvas)")]
    public bool continuousCheck = true;
    
    [Header("Debug")]
    public bool showDebugInfo = true;
    
    private void Awake()
    {
        EnsureInActiveScene("Awake");
    }
    
    private void Start()
    {
        EnsureInActiveScene("Start");
        
        // Subscribe to scene loaded event to check after scene changes
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
    
    private void Update()
    {
        if (continuousCheck && autoReparentToActiveScene)
        {
            EnsureInActiveScene("Update");
        }
    }
    
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (showDebugInfo)
        {
            Debug.Log($"[CanvasSceneManager] Scene loaded: {scene.name}. Canvas is in: {gameObject.scene.name}");
        }
        
        EnsureInActiveScene("OnSceneLoaded");
    }
    
    private void EnsureInActiveScene(string context)
    {
        if (!autoReparentToActiveScene) return;
        
        string currentSceneName = gameObject.scene.name;
        
        // Check if we're in DontDestroyOnLoad
        if (currentSceneName == "DontDestroyOnLoad")
        {
            Scene activeScene = SceneManager.GetActiveScene();
            
            if (showDebugInfo)
            {
                Debug.LogWarning($"[CanvasSceneManager] ({context}) Canvas-Game is in DontDestroyOnLoad! " +
                               $"Moving to active scene: {activeScene.name}");
            }
            
            // Move to active scene
            SceneManager.MoveGameObjectToScene(gameObject, activeScene);
            
            if (showDebugInfo)
            {
                Debug.Log($"[CanvasSceneManager] Successfully moved Canvas-Game to: {gameObject.scene.name}");
            }
        }
    }
    
    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        
        if (showDebugInfo)
        {
            Debug.Log($"[CanvasSceneManager] Canvas-Game destroyed (scene: {gameObject.scene.name})");
        }
    }
    
    /// <summary>
    /// Manual check - call this from Inspector or code if needed
    /// </summary>
    [ContextMenu("Check Current Scene")]
    public void CheckCurrentScene()
    {
        Debug.Log("=== Canvas Scene Check ===");
        Debug.Log($"GameObject: {gameObject.name}");
        Debug.Log($"Current Scene: {gameObject.scene.name}");
        Debug.Log($"Active Scene: {SceneManager.GetActiveScene().name}");
        Debug.Log($"Is in DontDestroyOnLoad: {gameObject.scene.name == "DontDestroyOnLoad"}");
        
        if (transform.parent != null)
        {
            Debug.Log($"Parent: {transform.parent.name}");
            Debug.Log($"Parent Scene: {transform.parent.gameObject.scene.name}");
        }
        else
        {
            Debug.Log("Parent: None (root object)");
        }
    }
}

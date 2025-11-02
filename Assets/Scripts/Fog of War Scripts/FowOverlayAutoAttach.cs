// Assets/Scripts/Fog of War/FowOverlayAutoAttach.cs
using UnityEngine;

[DisallowMultipleComponent]
public class FowOverlayAutoAttach : MonoBehaviour
{
    public FogOfWarManager manager;        // If null, uses Instance
    public Camera mainCamera;
    public Camera minimapCamera;
    public Camera mapCamera;

    [Header("Overlay Appearance")]
    public Color fogColor = Color.black;
    [Range(0, 1)] public float mainOpacity = 1f;
    [Range(0, 1)] public float minimapOpacity = 1f;
    [Range(0, 1)] public float mapOpacity = 1f;

    void Start()
    {
        if (!manager) manager = FogOfWarManager.Instance;
        if (!manager)
        {
            Debug.LogWarning("[FOW] No FogOfWarManager available; skipping overlay attach.");
            return;
        }

        Attach(mainCamera, mainOpacity);
        Attach(minimapCamera, minimapOpacity);
        Attach(mapCamera, mapOpacity);
    }

    void Attach(Camera cam, float opacity)
    {
        if (!cam) return;
        var blit = cam.GetComponent<FowOverlayBlit>();
        if (!blit) blit = cam.gameObject.AddComponent<FowOverlayBlit>();
        blit.manager = manager;
        blit.fogColor = fogColor;
        blit.fogOpacity = opacity;
    }
}

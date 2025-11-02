using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class SceneDarkener : MonoBehaviour
{
    [Header("Ambient Settings")]
    [Tooltip("Overall base ambient color (black = full dark).")]
    public Color ambientColor = new Color(0.03f, 0.03f, 0.03f, 1f); // slightly brighter than before

    [Tooltip("Choose Flat for even darkness, Trilight for subtle tinting.")]
    public AmbientMode ambientMode = AmbientMode.Flat;

    [Header("Tiny Fill (Directional, no shadows)")]
    [Tooltip("Adds a VERY faint global fill so edges don't look like a hard circle.")]
    public bool addFaintDirectionalFill = true;
    [Range(0f, 0.2f)] public float fillIntensity = 0.05f;
    public Color fillColor = new Color(0.18f, 0.18f, 0.20f, 1f);
    public Vector3 fillDirection = new Vector3(0.25f, -1f, 0.3f);

    [Header("Reflections (optional)")]
    [Range(0f, 1f)] public float reflectionIntensity = 0.2f;

    [Header("Fog (optional)")]
    public bool enableFog = true;
    public Color fogColor = new Color(0.02f, 0.02f, 0.02f, 1f);
    [Range(0.001f, 0.2f)] public float fogDensity = 0.02f;

    // backup
    private AmbientMode _origAmbientMode;
    private Color _origAmbientLight;
    private float _origReflectionIntensity;
    private bool _origFog;
    private Color _origFogColor;
    private float _origFogDensity;

    private Light _fillLight;

    void OnEnable()
    {
        // backup
        _origAmbientMode = RenderSettings.ambientMode;
        _origAmbientLight = RenderSettings.ambientLight;
        _origReflectionIntensity = RenderSettings.reflectionIntensity;
        _origFog = RenderSettings.fog;
        _origFogColor = RenderSettings.fogColor;
        _origFogDensity = RenderSettings.fogDensity;

        // apply darkness
        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientLight = ambientColor;
        RenderSettings.reflectionIntensity = reflectionIntensity;

        RenderSettings.fog = enableFog;
        if (enableFog)
        {
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = fogDensity;
        }

        // faint global directional fill to soften hard falloffs
        if (addFaintDirectionalFill)
        {
            var go = new GameObject("FaintDirectionalFill");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.LookRotation(fillDirection.normalized, Vector3.up);

            _fillLight = go.AddComponent<Light>();
            _fillLight.type = LightType.Directional;
            _fillLight.color = fillColor;
            _fillLight.intensity = fillIntensity;
            _fillLight.shadows = LightShadows.None; // no shadows, just lift
        }
    }

    void OnDisable()
    {
        RenderSettings.ambientMode = _origAmbientMode;
        RenderSettings.ambientLight = _origAmbientLight;
        RenderSettings.reflectionIntensity = _origReflectionIntensity;

        RenderSettings.fog = _origFog;
        RenderSettings.fogColor = _origFogColor;
        RenderSettings.fogDensity = _origFogDensity;

        if (_fillLight != null)
        {
            if (Application.isPlaying) Destroy(_fillLight.gameObject);
            else DestroyImmediate(_fillLight.gameObject);
        }
    }
}

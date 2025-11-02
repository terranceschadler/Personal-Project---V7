using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(Transform))]
public class PlayerProximityLight : MonoBehaviour
{
    [Header("Main Light (Shadowed)")]
    public Light mainLight;                    // auto-created
    public float intensity = 3.2f;
    public float range = 11f;
    public float bounceIntensity = 0.0f;
    public LightShadows shadows = LightShadows.Soft;
    public Color color = new Color(1.0f, 0.95f, 0.85f);

    [Header("Placement")]
    [Tooltip("Local offset from the player (x,y,z). Put slightly forward & above.")]
    public Vector3 localOffset = new Vector3(0.0f, 1.9f, 0.5f);

    [Header("Inner Fill (Unshadowed)")]
    public bool useInnerFill = true;
    [Range(0f, 1f)] public float fillIntensityFactor = 0.55f; // portion of main intensity
    [Range(0.1f, 1f)] public float fillRangeFactor = 0.5f;     // portion of main range
    public Light innerFillLight;                                // auto-created

    [Header("Flicker (optional)")]
    public bool enableFlicker = true;
    [Range(0f, 0.5f)] public float flickerAmplitude = 0.08f;
    [Range(0.1f, 10f)] public float flickerSpeed = 3.0f;

    [Header("Self-Shadow Options")]
    [Tooltip("Turn off shadow casting on the player's renderers to avoid self-shadow donuts.")]
    public bool disablePlayerShadowCasting = false;

    [Tooltip("If the light is under low ceilings, it auto nudges down its intensity a bit to avoid harsh artifacts.")]
    public bool autoCeilingCheck = true;
    public float ceilingProbeDistance = 2.2f;   // above light
    public LayerMask ceilingMask = ~0;          // adjust if you want specific layers

    private float _baseIntensity;
    private Renderer[] _cachedPlayerRenderers;

    void Awake()
    {
        // MAIN (shadowed) light
        if (mainLight == null)
        {
            GameObject lightGO = new GameObject("PlayerProximityLight_Main");
            lightGO.transform.SetParent(transform, false);
            lightGO.transform.localPosition = localOffset;

            mainLight = lightGO.AddComponent<Light>();
            mainLight.type = LightType.Point;
        }

        mainLight.color = color;
        mainLight.intensity = intensity;
        mainLight.range = range;
        mainLight.bounceIntensity = bounceIntensity;
        mainLight.shadows = shadows;
        mainLight.renderMode = LightRenderMode.Auto;

        _baseIntensity = intensity;

        // INNER FILL (unshadowed)
        if (useInnerFill && innerFillLight == null)
        {
            GameObject fillGO = new GameObject("PlayerProximityLight_Fill");
            fillGO.transform.SetParent(transform, false);
            fillGO.transform.localPosition = localOffset * 0.85f; // a touch closer to feet

            innerFillLight = fillGO.AddComponent<Light>();
            innerFillLight.type = LightType.Point;
            innerFillLight.shadows = LightShadows.None; // critical to avoid ring
        }
        if (innerFillLight != null)
        {
            innerFillLight.color = color;
            innerFillLight.intensity = _baseIntensity * fillIntensityFactor;
            innerFillLight.range = range * fillRangeFactor;
            innerFillLight.renderMode = LightRenderMode.Auto;
        }

        // Optional: disable the player's own shadow casting
        if (disablePlayerShadowCasting)
        {
            _cachedPlayerRenderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in _cachedPlayerRenderers)
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }

    void LateUpdate()
    {
        // keep positions synced to offset
        if (mainLight != null) mainLight.transform.localPosition = localOffset;
        if (innerFillLight != null) innerFillLight.transform.localPosition = localOffset * 0.85f;

        // optional soft flicker
        if (enableFlicker && mainLight != null)
        {
            float t = Time.time * flickerSpeed;
            float jitter = (Mathf.PerlinNoise(t, 0.123f) - 0.5f) * 2f;
            mainLight.intensity = _baseIntensity * (1f + jitter * flickerAmplitude);
            if (innerFillLight) innerFillLight.intensity = mainLight.intensity * fillIntensityFactor;
        }

        // optional: if under a low ceiling, slightly reduce intensity to avoid harsh bright/dark seam
        if (autoCeilingCheck && mainLight != null)
        {
            Vector3 worldPos = mainLight.transform.position;
            if (Physics.Raycast(worldPos, Vector3.up, out var hit, ceilingProbeDistance, ceilingMask, QueryTriggerInteraction.Ignore))
            {
                // near ceiling => lightly compress dynamic range
                float proximity = Mathf.InverseLerp(ceilingProbeDistance, 0.2f, hit.distance);
                float damp = Mathf.Lerp(0.85f, 1f, proximity);
                mainLight.intensity = _baseIntensity * damp;
                if (innerFillLight) innerFillLight.intensity = mainLight.intensity * fillIntensityFactor;
            }
        }
    }

    // runtime controls
    public void SetIntensity(float target) { _baseIntensity = target; if (mainLight) mainLight.intensity = target; if (innerFillLight) innerFillLight.intensity = target * fillIntensityFactor; }
    public void SetRange(float target) { range = target; if (mainLight) mainLight.range = target; if (innerFillLight) innerFillLight.range = target * fillRangeFactor; }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (mainLight)
        {
            Gizmos.color = new Color(color.r, color.g, color.b, 0.15f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireSphere(localOffset, range);
        }
    }
#endif
}

using UnityEngine;

[CreateAssetMenu(fileName = "NewRicochetCarbine", menuName = "Weapons/Ricochet Carbine")]
public class RicochetCarbine : Weapon
{
    [Header("Ricochet")]
    [Min(0)] public int maxBounces = 3;
    [Range(0f, 0.95f)] public float speedLossPerBounce = 0.15f;
    [Range(0f, 0.95f)] public float damageLossPerBounce = 0.25f;

    [Header("Surfaces & Targets")]
    [Tooltip("World layers that can reflect (e.g., Default, Walls).")]
    public LayerMask ricochetSurfaces;
    [Tooltip("Layers considered enemies that can be damaged.")]
    public LayerMask enemyLayers;
    [Tooltip("Layers to ignore entirely (e.g., Pickups).")]
    public LayerMask ignoreLayers;

    [Header("Target Bias (Optional)")]
    public bool biasRicochetTowardTargets = true;
    [Range(0f, 45f)] public float ricochetAimCone = 12f;
    [Min(0f)] public float ricochetTargetSearchRadius = 12f;

    [Header("Lifetimes & Cutoffs")]
    [Min(0.01f)] public float maxLifeSeconds = 6f;
    [Min(0.5f)] public float minSpeedToContinue = 6f;

    [Header("FX (Optional)")]
    public GameObject bounceVfxPrefab;
    public AudioClip bounceSfx;
}

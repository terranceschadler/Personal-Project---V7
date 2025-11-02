using UnityEngine;


/// <summary>
/// Attach to any key area object to make it show up on the Map.
/// </summary>
[DisallowMultipleComponent]
public class MapKeyArea : MonoBehaviour
{
    public enum AreaType { Boss, Helicopter, FriendlySpawn, Shop, Other }


    [Header("Identity")]
    [Tooltip("Display name shown in the Map UI when selected.")]
    public string displayName = "Key Area";


    [Tooltip("Type drives icon sprite defaults and Goal category.")]
    public AreaType areaType = AreaType.Other;


    [Header("Icon")]
    [Tooltip("Override the icon sprite used in the Map UI (optional).")]
    public Sprite iconSprite;


    [Tooltip("World anchor used for the icon position (defaults to this transform).")]
    public Transform iconWorldAnchor;


    private void Reset()
    {
        iconWorldAnchor = transform;
        displayName = gameObject.name;
    }


    private void OnEnable()
    {
        if (!iconWorldAnchor) iconWorldAnchor = transform;
        if (MapModeController.Instance) MapModeController.Instance.RegisterArea(this);
    }


    private void OnDisable()
    {
        if (MapModeController.Instance) MapModeController.Instance.UnregisterArea(this);
    }
}
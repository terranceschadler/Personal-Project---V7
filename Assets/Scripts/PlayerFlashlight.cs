using UnityEngine;

[DisallowMultipleComponent]
public class PlayerFlashlight : MonoBehaviour
{
    public enum AimMode
    {
        CameraPlanarForward,   // camera forward projected to XZ
        MouseAimOnGround,      // raycast mouse to ground
        PlayerMoveDirection,   // WASD / Left Stick (now supported)
        GamepadRightStick,     // NEW: aim with right stick direction
        GamepadOrMouseHybrid   // NEW: use right stick when active, else mouse
    }

    [Header("Aim")]
    public AimMode aimMode = AimMode.GamepadRightStick; // set default to stick if desired
    [Tooltip("If empty, Camera.main is used.")]
    public Camera aimCamera;
    [Tooltip("Layers considered 'world' for mouse aiming and wall trim.")]
    public LayerMask worldMask = ~0;
    [Tooltip("Ground plane height used if MouseAimOnGround misses (e.g., no collider).")]
    public float fallbackGroundY = 0f;

    [Header("Gamepad Settings")]
    [Range(0f, 1f)] public float rightStickDeadzone = 0.18f;
    [Tooltip("If true, Left Stick movement can drive PlayerMoveDirection, not just WASD.")]
    public bool leftStickDrivesMoveDirection = true;

    [Header("Placement")]
    [Tooltip("Local offset of the spotlight relative to this transform.")]
    public Vector3 localOffset = new Vector3(0f, 1.9f, 0.25f);
    [Tooltip("Slerp speed of beam rotation.")]
    public float turnSpeed = 18f;

    [Header("Flashlight (Spot)")]
    public Light flashlight;                  // auto-created if missing
    public float intensity = 6f;              // 3–8 for Built-in LDR, higher if HDR
    public float range = 22f;
    [Range(10f, 120f)] public float spotAngle = 80f;
    public LightShadows shadows = LightShadows.Soft;
    [Tooltip("Optional cookie for beam shape; leave null if none.")]
    public Texture cookie;
    [Tooltip("Exclude this layer from lighting (e.g., your Player layer). -1 = include all.")]
    public int excludeLayer = -1;

    [Header("Foot Fill (anti-donut)")]
    public bool useFootFill = true;
    [Range(0f, 1f)] public float footFillIntensity = 0.18f;
    [Range(0.5f, 6f)] public float footFillRange = 2.0f;
    public Vector3 footFillLocalOffset = new Vector3(0f, 1.1f, 0f);

    [Header("Auto Range Trim")]
    public bool autoTrimRange = true;
    public float trimProbeDistance = 26f;
    public float trimBuffer = 0.25f;

    [Header("Quality")]
    public bool updateLate = true;

    // internals
    Light _footFill;
    float _currentRange;
    Vector3 _lastAimDir = Vector3.forward; // keeps heading when idle

    void Awake()
    {
        if (!aimCamera) aimCamera = Camera.main;

        // Create flashlight if missing
        if (!flashlight)
        {
            var go = new GameObject("Flashlight_Spot");
            go.transform.SetParent(transform, false);
            flashlight = go.AddComponent<Light>();
        }

        // Configure spotlight
        flashlight.type = LightType.Spot;
        flashlight.intensity = intensity;
        flashlight.range = range;
        flashlight.spotAngle = spotAngle;
        flashlight.bounceIntensity = 0f;
        flashlight.shadows = shadows;
        flashlight.renderMode = LightRenderMode.Auto;
        flashlight.cookie = cookie;
        flashlight.enabled = true;

        // Exclude layer if specified
        if (excludeLayer >= 0 && excludeLayer <= 31)
        {
            int mask = flashlight.cullingMask;
            mask &= ~(1 << excludeLayer);
            flashlight.cullingMask = mask;
        }

        // Foot fill
        if (useFootFill)
        {
            var fillGO = new GameObject("Flashlight_FootFill");
            fillGO.transform.SetParent(transform, false);
            _footFill = fillGO.AddComponent<Light>();
            _footFill.type = LightType.Point;
            _footFill.shadows = LightShadows.None;
            _footFill.intensity = footFillIntensity;
            _footFill.range = footFillRange;
            _footFill.renderMode = LightRenderMode.Auto;
        }

        _currentRange = range;
        ApplyOffsets();
        // sensible initial aim
        _lastAimDir = GetPlanarForwardFromCamera();
        if (_lastAimDir.sqrMagnitude < 0.001f) _lastAimDir = transform.forward.FlattenY().normalized;
    }

    void Update()
    {
        if (!updateLate) DriveFlashlight();
    }

    void LateUpdate()
    {
        if (updateLate) DriveFlashlight();
    }

    void DriveFlashlight()
    {
        if (!aimCamera) aimCamera = Camera.main;
        ApplyOffsets();

        Vector3 origin = flashlight.transform.position;
        Vector3 aimDir = _lastAimDir;

        switch (aimMode)
        {
            case AimMode.CameraPlanarForward:
                aimDir = GetPlanarForwardFromCamera();
                if (aimDir.sqrMagnitude < 1e-4f) aimDir = _lastAimDir;
                break;

            case AimMode.MouseAimOnGround:
                aimDir = GetMouseAimDirection(origin);
                if (aimDir.sqrMagnitude < 1e-4f) aimDir = GetPlanarForwardFromCamera();
                break;

            case AimMode.PlayerMoveDirection:
                aimDir = GetMoveDirectionWorld();            // now supports left stick if enabled
                if (aimDir.sqrMagnitude < 1e-4f) aimDir = _lastAimDir;
                break;

            case AimMode.GamepadRightStick:
                aimDir = GetRightStickAimDirection();
                if (aimDir.sqrMagnitude < 1e-4f) aimDir = _lastAimDir;
                break;

            case AimMode.GamepadOrMouseHybrid:
                {
                    Vector3 stickDir = GetRightStickAimDirection();
                    if (stickDir.sqrMagnitude >= 1e-4f)
                        aimDir = stickDir;
                    else
                    {
                        var mouseDir = GetMouseAimDirection(origin);
                        aimDir = (mouseDir.sqrMagnitude >= 1e-4f) ? mouseDir : GetPlanarForwardFromCamera();
                    }
                    break;
                }
        }

        // keep planar
        aimDir.y = 0f;
        if (aimDir.sqrMagnitude > 1e-6f) _lastAimDir = aimDir.normalized;

        // rotate beam
        Quaternion target = Quaternion.LookRotation(_lastAimDir, Vector3.up);
        flashlight.transform.rotation = Quaternion.Slerp(
            flashlight.transform.rotation,
            target,
            1f - Mathf.Exp(-turnSpeed * Time.deltaTime)
        );

        // auto trim range vs walls
        if (autoTrimRange)
        {
            float probe = Mathf.Max(trimProbeDistance, _currentRange);
            if (Physics.Raycast(origin, flashlight.transform.forward, out var hit, probe, worldMask, QueryTriggerInteraction.Ignore))
                _currentRange = Mathf.Lerp(_currentRange, Mathf.Max(1f, hit.distance - trimBuffer), 1f - Mathf.Exp(-12f * Time.deltaTime));
            else
                _currentRange = Mathf.Lerp(_currentRange, range, 1f - Mathf.Exp(-6f * Time.deltaTime));
            flashlight.range = _currentRange;
        }
        else
        {
            flashlight.range = range;
        }

        // maintain subtle foot fill
        if (_footFill)
        {
            _footFill.transform.localPosition = footFillLocalOffset;
            _footFill.intensity = footFillIntensity;
            _footFill.range = footFillRange;
        }
    }

    // --- Helpers -------------------------------------------------------------

    Vector3 GetPlanarForwardFromCamera()
    {
        if (!aimCamera) return transform.forward.FlattenY().normalized;
        Vector3 fwd = aimCamera.transform.forward;
        fwd.y = 0f;
        return fwd.normalized;
    }

    Vector3 GetMouseAimDirection(Vector3 origin)
    {
        if (!aimCamera) return Vector3.zero;

        Ray r = aimCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(r, out var hit, 5000f, worldMask, QueryTriggerInteraction.Ignore))
            return (hit.point - origin).FlattenY().normalized;

        // fallback to an infinite ground plane at Y = fallbackGroundY
        Plane ground = new Plane(Vector3.up, new Vector3(0f, fallbackGroundY, 0f));
        if (ground.Raycast(r, out float t))
        {
            Vector3 p = r.GetPoint(t);
            return (p - origin).FlattenY().normalized;
        }
        return Vector3.zero;
    }

    Vector3 GetMoveDirectionWorld()
    {
        // WASD
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        // Optional: Left stick can also drive this mode
        if (leftStickDrivesMoveDirection)
        {
            Vector2 ls = GamepadInput.LeftStick;
            if (ls.sqrMagnitude > 0.01f)
            {
                h = ls.x; v = ls.y;
            }
        }

        Vector3 dir = new Vector3(h, 0f, v);
        if (dir.sqrMagnitude < 1e-4f) return Vector3.zero;
        return dir.normalized;
    }

    Vector3 GetRightStickAimDirection()
    {
        Vector2 rs = GamepadInput.RightStick;
        if (rs.magnitude <= rightStickDeadzone) return Vector3.zero;
        Vector3 dir = new Vector3(rs.x, 0f, rs.y);
        return dir.normalized;
    }

    void ApplyOffsets()
    {
        if (flashlight)
        {
            flashlight.transform.SetParent(transform, false);
            flashlight.transform.localPosition = localOffset;
        }
        if (_footFill)
        {
            _footFill.transform.SetParent(transform, false);
            _footFill.transform.localPosition = footFillLocalOffset;
        }
    }

    // public runtime tweaks
    public void SetIntensity(float val) { intensity = val; if (flashlight) flashlight.intensity = val; }
    public void SetRange(float val) { range = val; if (!autoTrimRange && flashlight) flashlight.range = val; }
    public void SetSpotAngle(float angle) { spotAngle = Mathf.Clamp(angle, 10f, 120f); if (flashlight) flashlight.spotAngle = spotAngle; }
}

// small utility for planar projection
static class VecExt
{
    public static Vector3 FlattenY(this Vector3 v) => new Vector3(v.x, 0f, v.z);
}

using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Stats")]
    public float moveSpeed = 5f;
    public float maxHealth = 100f;

    [Header("Combat (New System)")]
    public Transform firePoint;
    public LayerMask groundLayer;
    public PlayerWeaponController weaponUpgradeController;

    [Header("Audio / VFX")]
    public AudioSource audioSource;
    public Light muzzleFlashLight;
    public float flashDuration = 0.05f;
    private float flashTimer = 0f;

    [Header("Health Bar")]
    public GameObject healthBarPrefab;
    public Vector3 healthBarOffset = new Vector3(0f, 2f, 0f);
    public Vector3 healthBarScale = Vector3.one;
    [Min(0f)] public float healthBarFollowSmoothing = 0f;

    [Header("Movement Physics")]
    public float gravity = -24f;
    public float groundCheckRadius = 0.25f;
    public float groundCheckDistance = 0.35f;
    public float spawnUnstickUp = 0.1f;
    public float unstickNudge = 0.02f;

    [Header("Top-Down Movement")]
    public bool forceWorldAxesWhenTopDown = true;
    [Range(0.0f, 1.0f)] public float topDownDotThreshold = 0.7f;

    [Header("Smoothing (glitch killers)")]
    public float acceleration = 22f;
    public float deceleration = 28f;
    [Range(0f, 1f)] public float airControl = 0.65f;
    public float groundSnapExtra = 0.15f;

    [Header("Controller tuning")]
    public float slopeLimit = 60f;
    public float stepOffset = 0.25f;

    [Header("Gamepad (Right Trigger Mapping)")]
    public string triggerAxisName = "Triggers";
    public bool invertTriggerAxis = false;
    public bool rtIsPositive = true;
    [Range(0f, 1f)] public float rtFireThreshold = 0.35f;

    [Header("Aim (Stick vs Mouse Priority)")]
    [Range(0f, 1f)] public float rightStickAimDeadzone = 0.2f;
    [Range(0.05f, 1.0f)] public float inputPriorityGrace = 0.35f;
    [Range(0.1f, 8f)] public float mouseMovePixelsThreshold = 1.0f;

    [Header("Stick Snap")]
    public bool snapStickWhenZero = true;
    [Range(0.0f, 0.6f)] public float stickSnapHoldDuration = 0.18f;

    [Header("Dash")]
    public bool dashEnabled = true;
    public float dashSpeedMultiplier = 3.0f;
    public float dashDuration = 0.22f;
    public float dashCooldown = 0.85f;
    public AnimationCurve dashSpeedCurve = AnimationCurve.EaseInOut(0, 1, 1, 1);
    public KeyCode dashKey = KeyCode.LeftShift;
    public float dashIFrames = 0.18f;
    public bool requireMoveForDash = true;
    public float minDashMoveSpeed = 0.15f;
    public bool faceDashDirection = false;
    public AudioClip dashClip;
    [Range(0f, 1f)] public float dashVolume = 0.8f;
    public ParticleSystem dashVfx;

    [Header("Debug / Diagnostics")]
    public bool debugLogs = false;

    private float currentHealth;
    private Camera mainCamera;
    private float nextFireTime = 0f;

    private Image healthFill;
    private Transform healthBarTransform;
    private CharacterController controller;

    // velocities
    private Vector3 velocity;
    private Vector3 planar;
    private bool grounded;
    private float groundedTimer;
    private const float groundedGrace = 0.1f;

    // input priority state
    private Vector3 _lastMousePos;
    private float _mousePriorityUntil;
    private float _stickPriorityUntil;

    // stick snap state
    private Vector3 _lastStickDir = Vector3.forward;
    private bool _stickWasActive = false;
    private float _stickSnapUntil = 0f;

    // Dash state
    private bool isDashing = false;
    private float dashEndTime = 0f;
    private float nextDashAllowedTime = 0f;
    private Vector3 dashDir = Vector3.zero;
    private bool isInvulnerable = false;
    private float invulnEndTime = 0f;

    // Public read-only for other systems
    public bool IsDashing => isDashing;

    // --- Input availability probes ---
    private bool triggerAxisAvailable = false;
    private bool dashButtonAvailable = false;

    // --- Grounding perf helpers (NonAlloc + throttling) ---
    private RaycastHit[] _groundHits = new RaycastHit[2];
    private int _groundProbeInterval = 2;     // probe every Nth frame
    private int _groundProbeCountdown = 0;
    private Vector3 _lastGroundPoint;
    private Vector3 _lastGroundNormal = Vector3.up;

    private void DLog(string msg) { if (debugLogs) Debug.Log(msg, this); }
    private void DLogFormat(string fmt, params object[] args) { if (debugLogs) Debug.LogFormat(this, fmt, args); }

    void Start()
    {
        controller = GetComponent<CharacterController>();
        mainCamera = Camera.main;
        currentHealth = maxHealth;

        // Unstick on spawn WITHOUT toggling the controller (avoids tiny stalls)
        if (spawnUnstickUp > 0f)
        {
            controller.Move(Vector3.up * spawnUnstickUp);
        }

        // Controller tuning
        controller.minMoveDistance = 0f;
        controller.skinWidth = 0.02f;
        controller.slopeLimit = slopeLimit;
        controller.stepOffset = stepOffset;

        // Health bar spawn
        if (healthBarPrefab != null)
        {
            GameObject hb = Instantiate(healthBarPrefab, transform.position + healthBarOffset, Quaternion.identity);
            healthBarTransform = hb.transform;

            if (healthBarScale != Vector3.one)
                healthBarTransform.localScale = healthBarScale;

            healthFill = FindHealthFill(hb.transform);
            if (healthFill != null) EnsureFillSetup(healthFill);
            UpdateHealthBar();
        }

        if (muzzleFlashLight != null) muzzleFlashLight.enabled = false;

        // New weapon system
        if (weaponUpgradeController == null)
            weaponUpgradeController = GetComponent<PlayerWeaponController>();

        if (weaponUpgradeController != null)
        {
            DLog("[PlayerController] New Weapon System active.");
        }
        else
        {
            Debug.LogError("[PlayerController] No PlayerWeaponController found!");
        }

        _lastMousePos = Input.mousePosition;
        _mousePriorityUntil = _stickPriorityUntil = 0f;
        _lastStickDir = transform.forward;

        // Probe optional inputs ONCE
        try { _ = Input.GetAxis(triggerAxisName); triggerAxisAvailable = true; } catch { triggerAxisAvailable = false; }
        try { _ = Input.GetButton("Dash"); dashButtonAvailable = true; } catch { dashButtonAvailable = false; }

        // Seed ground cache
        _lastGroundPoint = transform.position + Vector3.down * 0.5f;
        _lastGroundNormal = Vector3.up;
        _groundProbeCountdown = 0;
    }

    void Update()
    {
        HandleMovement(); // includes dash evaluation + application
        AimWithPriorityAndSnap();

        // Mouse (Fire1) OR Right Trigger
        bool wantsMouseFire = Input.GetButton("Fire1");
        bool wantsRT = ReadRightTrigger01() > rtFireThreshold;

        if ((wantsMouseFire || wantsRT) && Time.time >= nextFireTime && weaponUpgradeController != null)
        {
            if (weaponUpgradeController.CanFire())
            {
                weaponUpgradeController.Fire();
                nextFireTime = Time.time + (1f / weaponUpgradeController.GetFireRate());

                // Optional visual ping
                if (muzzleFlashLight != null)
                {
                    muzzleFlashLight.enabled = true;
                    flashTimer = flashDuration;
                }
            }
        }

        // Muzzle flash light timer
        if (muzzleFlashLight != null && muzzleFlashLight.enabled)
        {
            flashTimer -= Time.deltaTime;
            if (flashTimer <= 0f) muzzleFlashLight.enabled = false;
        }

        // Health bar follow + billboard
        if (healthBarTransform != null)
        {
            Vector3 targetPos = transform.position + healthBarOffset;
            if (healthBarFollowSmoothing > 0f)
            {
                float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.0001f, healthBarFollowSmoothing));
                healthBarTransform.position = Vector3.Lerp(healthBarTransform.position, targetPos, t);
            }
            else
            {
                healthBarTransform.position = targetPos;
            }

            if (mainCamera != null)
                healthBarTransform.rotation = Quaternion.LookRotation(healthBarTransform.position - mainCamera.transform.position);
        }

        // Decrement invulnerability if needed
        if (isInvulnerable && Time.time >= invulnEndTime)
            isInvulnerable = false;

        _lastMousePos = Input.mousePosition;
    }

    // ---------------- MOVEMENT / DASH ----------------
    float ReadRightTrigger01()
    {
        if (!triggerAxisAvailable) return 0f;

        float axisVal = Input.GetAxis(triggerAxisName);
        if (invertTriggerAxis) axisVal = -axisVal;

        float rt = rtIsPositive ? Mathf.Max(0f, axisVal) : Mathf.Max(0f, -axisVal);
        return Mathf.Clamp01(rt);
    }

    void HandleMovement()
    {
        if (Time.deltaTime <= 0f) return;

        int gMask = (groundLayer.value != 0) ? groundLayer.value : Physics.DefaultRaycastLayers;

        // ---------- Grounding (cheap first, probe when needed) ----------
        bool ccGrounded = controller.isGrounded;
        bool doProbe = (_groundProbeCountdown-- <= 0) || !ccGrounded;

        bool hitGround = false;
        if (doProbe)
        {
            _groundProbeCountdown = _groundProbeInterval;

            hitGround = CapsuleGroundNonAlloc(gMask, out _lastGroundPoint, out _lastGroundNormal);
            if (!hitGround)
            {
                // Thin ray fallback (still NonAlloc)
                Ray ray = new Ray(transform.position + Vector3.up * 0.05f, Vector3.down);
                int hitCount = Physics.RaycastNonAlloc(ray, _groundHits, groundCheckDistance + groundSnapExtra, gMask, QueryTriggerInteraction.Ignore);
                if (hitCount > 0)
                {
                    _lastGroundPoint = _groundHits[0].point;
                    _lastGroundNormal = _groundHits[0].normal;
                    hitGround = true;
                }
            }
        }

        float yErrProbe = transform.position.y - _lastGroundPoint.y;
        bool wasGroundedLastFrame = grounded;
        grounded = ccGrounded || (hitGround && (yErrProbe <= groundCheckDistance));

        if (grounded) groundedTimer = 0f; else groundedTimer += Time.deltaTime;
        bool groundedThisFrame = (groundedTimer < groundedGrace);

        float yErr = transform.position.y - _lastGroundPoint.y;
        Vector3 gPoint = _lastGroundPoint;
        Vector3 gNormal = _lastGroundNormal;
        // ---------------------------------------------------------------

        Vector2 mInput = GetMoveInput();
        Vector3 mDir = Vector3.zero;
        if (mInput.sqrMagnitude > 0.01f)
        {
            bool topDown = false;
            if (mainCamera != null)
            {
                float dot = Vector3.Dot(mainCamera.transform.forward, Vector3.down);
                topDown = (dot >= topDownDotThreshold);
            }

            if (topDown && forceWorldAxesWhenTopDown)
            {
                mDir = new Vector3(mInput.x, 0f, mInput.y).normalized;
            }
            else
            {
                if (mainCamera != null)
                {
                    Vector3 f = mainCamera.transform.forward;
                    Vector3 r = mainCamera.transform.right;
                    f.y = 0f; r.y = 0f;
                    f.Normalize(); r.Normalize();
                    mDir = (f * mInput.y + r * mInput.x).normalized;
                }
                else
                {
                    mDir = new Vector3(mInput.x, 0f, mInput.y).normalized;
                }
            }
        }

        bool justLanded = (!wasGroundedLastFrame && groundedThisFrame);
        bool tryUnstick = justLanded && (yErr > controller.stepOffset + 0.02f);
        if (tryUnstick)
        {
            // Use Move to nudge upward instead of toggling controller.enabled
            Vector3 unstickDelta = new Vector3(0f, (gPoint.y + unstickNudge) - transform.position.y, 0f);
            controller.Move(unstickDelta);
            DLog($"[PlayerController] Unstick on landing (Move): yErr={yErr:F3}");
        }

        if (isDashing)
        {
            if (Time.time >= dashEndTime)
            {
                isDashing = false;
                DLog("[PlayerController] Dash complete.");
            }
        }
        else
        {
            if (dashEnabled && Time.time >= nextDashAllowedTime)
            {
                // Exception-free Dash input
                bool tryDash = Input.GetKeyDown(dashKey);
                if (!tryDash && dashButtonAvailable)
                {
                    tryDash = Input.GetButtonDown("Dash");
                }

                if (tryDash)
                {
                    float planarSpeed = planar.magnitude;
                    bool moving = (planarSpeed >= minDashMoveSpeed);
                    if (!requireMoveForDash || moving)
                    {
                        StartDash(mDir.sqrMagnitude > 0.001f ? mDir : planar.normalized, planarSpeed);
                    }
                    else
                    {
                        DLog("[PlayerController] Dash require move but not moving enough.");
                    }
                }
            }
        }

        Vector3 desiredPlanar;
        if (isDashing)
        {
            float elapsed = Time.time - (dashEndTime - dashDuration);
            float norm = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, dashDuration));
            float mult = (dashSpeedCurve != null) ? dashSpeedCurve.Evaluate(norm) : 1f;
            desiredPlanar = dashDir * (moveSpeed * dashSpeedMultiplier * mult);
        }
        else
        {
            desiredPlanar = mDir * moveSpeed;
        }

        float dt = Time.deltaTime;
        float accel = groundedThisFrame ? acceleration : (acceleration * airControl);
        float decel = groundedThisFrame ? deceleration : (deceleration * airControl);

        if (desiredPlanar.sqrMagnitude > 0.01f)
        {
            planar = Vector3.MoveTowards(planar, desiredPlanar, accel * dt);
        }
        else
        {
            planar = Vector3.MoveTowards(planar, Vector3.zero, decel * dt);
        }

        if (groundedThisFrame)
        {
            velocity.y = -1f;
            float snapDist = groundSnapExtra;
            if (yErr > 0.001f && yErr <= snapDist)
            {
                controller.Move(Vector3.down * (yErr + 0.001f));
            }
        }
        else
        {
            velocity.y += gravity * dt;
        }

        Vector3 finalMove = planar * dt + new Vector3(0f, velocity.y * dt, 0f);
        if (controller.enabled)
            controller.Move(finalMove);
    }

    void StartDash(Vector3 direction, float planarSpeed)
    {
        if (direction.sqrMagnitude < 0.001f)
            direction = transform.forward;
        else
            direction.Normalize();

        dashDir = direction;
        isDashing = true;
        dashEndTime = Time.time + dashDuration;
        nextDashAllowedTime = dashEndTime + dashCooldown;

        if (faceDashDirection)
            transform.rotation = Quaternion.LookRotation(dashDir, Vector3.up);

        if (dashIFrames > 0f)
        {
            isInvulnerable = true;
            invulnEndTime = Time.time + dashIFrames;
        }

        if (dashClip != null)
        {
            if (audioSource != null) audioSource.PlayOneShot(dashClip, dashVolume);
            else AudioSource.PlayClipAtPoint(dashClip, transform.position, dashVolume);
        }
        if (dashVfx != null) dashVfx.Play(true);

        DLog($"[PlayerController] DASH dir={dashDir} speedRef={planarSpeed:F2}");
    }

    // -------- Ground probe helper (NonAlloc) --------
    bool CapsuleGroundNonAlloc(int mask, out Vector3 point, out Vector3 normal)
    {
        // Match the CharacterController capsule
        Vector3 worldCenter = transform.TransformPoint(controller.center);
        float radius = Mathf.Max(0.0001f, controller.radius - 0.01f);
        float halfHeight = Mathf.Max(radius, controller.height * 0.5f - radius);

        Vector3 top = worldCenter + Vector3.up * halfHeight;
        Vector3 bottom = worldCenter - Vector3.up * halfHeight;

        // Cast a short distance downward
        float dist = groundCheckDistance + groundSnapExtra;

        int hits = Physics.CapsuleCastNonAlloc(
            top, bottom, radius,
            Vector3.down, _groundHits, dist,
            mask, QueryTriggerInteraction.Ignore);

        if (hits > 0)
        {
            point = _groundHits[0].point;
            normal = _groundHits[0].normal;
            return true;
        }

        point = default;
        normal = Vector3.up;
        return false;
    }

    Vector2 GetMoveInput()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        if (Mathf.Approximately(h, 0f) && Mathf.Approximately(v, 0f))
        {
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v -= 1f;
        }

        Vector2 ls = GamepadInput.LeftStick;
        if (ls.sqrMagnitude > 0.01f)
        {
            h = ls.x;
            v = ls.y;
        }

        return new Vector2(Mathf.Clamp(h, -1f, 1f), Mathf.Clamp(v, -1f, 1f));
    }

    // ---------------- AIM WITH PRIORITY + SNAP ----------------
    void AimWithPriorityAndSnap()
    {
        float now = Time.unscaledTime;

        // Mouse movement detection
        Vector3 mp = Input.mousePosition;
        float mouseDeltaSq = (mp - _lastMousePos).sqrMagnitude;
        if (mouseDeltaSq >= mouseMovePixelsThreshold * mouseMovePixelsThreshold)
            _mousePriorityUntil = now + inputPriorityGrace;

        // Stick detection
        Vector2 rs = GamepadInput.RightStick;
        bool stickActive = rs.magnitude > rightStickAimDeadzone;

        if (stickActive)
        {
            Vector3 dir = new Vector3(rs.x, 0f, rs.y);
            if (dir.sqrMagnitude > 1e-6f) _lastStickDir = dir.normalized;
            _stickPriorityUntil = now + inputPriorityGrace;
            _stickWasActive = true;
        }
        else
        {
            if (_stickWasActive && snapStickWhenZero)
            {
                if (_lastStickDir.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.LookRotation(_lastStickDir, Vector3.up);
                _stickSnapUntil = now + stickSnapHoldDuration;
            }
            _stickWasActive = false;
        }

        if (now < _mousePriorityUntil)
        {
            AimAtMouse();
            return;
        }

        if (now < _stickSnapUntil)
        {
            if (_lastStickDir.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(_lastStickDir, Vector3.up);
            return;
        }

        if (now < _stickPriorityUntil || stickActive)
        {
            if (stickActive)
            {
                AimWithStick(rs);
            }
            else if (_lastStickDir.sqrMagnitude > 1e-6f)
            {
                transform.rotation = Quaternion.LookRotation(_lastStickDir, Vector3.up);
            }
            return;
        }

        AimAtMouse();
    }

    void AimWithStick(Vector2 rs)
    {
        if (rs.magnitude <= 1e-4f) return;
        Vector3 dir = new Vector3(rs.x, 0f, rs.y);
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
    }

    void AimAtMouse()
    {
        if (mainCamera == null) return;
        int mask = (groundLayer.value == 0) ? Physics.DefaultRaycastLayers : groundLayer.value;

        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, mask, QueryTriggerInteraction.Ignore))
        {
            Vector3 targetPosition = hit.point;
            targetPosition.y = transform.position.y;
            Vector3 direction = targetPosition - transform.position;
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }

    // ---------------- HEALTH ----------------
    void UpdateHealthBar()
    {
        if (healthFill == null) return;
        healthFill.fillAmount = Mathf.Clamp01(currentHealth / Mathf.Max(1f, maxHealth));
    }

    public void Heal(float amount)
    {
        float oldHealth = currentHealth;
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
        float actualHealed = currentHealth - oldHealth;

        Debug.Log($"[PlayerController] ★ PLAYER picked up HEALTH PICKUP: +{actualHealed:F1} HP (was {oldHealth:F1}, now {currentHealth:F1}/{maxHealth:F1})", this);

        UpdateHealthBar();
    }

    public void TakeDamage(float dmg)
    {
        if (isInvulnerable) return;
        currentHealth -= dmg;
        UpdateHealthBar();
        if (currentHealth <= 0) Die();
    }

    void Die()
    {
        DLog("[PlayerController] Player has died!");

        if (healthBarTransform != null) Destroy(healthBarTransform.gameObject);

        if (muzzleFlashLight != null) muzzleFlashLight.enabled = false;
        velocity = Vector3.zero;
        planar = Vector3.zero;

        if (DeathUIController.Instance != null)
        {
            DeathUIController.Instance.ShowStats();
        }
        else
        {
            if (GameManager.Instance != null) GameManager.Instance.PauseGame();
            else Time.timeScale = 0f;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (controller != null) controller.enabled = false;
        this.enabled = false;
    }

    // ---------------- HEALTH BAR HELPERS ----------------
    Image FindHealthFill(Transform root)
    {
        var fillT = root.Find("Fill");
        if (fillT != null && fillT.TryGetComponent<Image>(out var img))
            return img;

        var imgs = root.GetComponentsInChildren<Image>(true);
        foreach (var i in imgs)
            if (i.name.ToLower().Contains("fill"))
                return i;

        if (imgs.Length >= 2) return imgs[imgs.Length - 1];
        return null;
    }

    void EnsureFillSetup(Image img)
    {
        if (img.type != Image.Type.Filled)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
        }
    }
}

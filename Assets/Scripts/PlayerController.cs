using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Stats")]
    public float moveSpeed = 5f;
    public float maxHealth = 100f;

    [Header("Combat")]
    public Transform firePoint;
    public LayerMask groundLayer;        // should include your walkable floor
    public Weapon startingWeapon;

    [Header("Combat Stats")]
    [Tooltip("Base damage per bullet for weapons that don't define their own damage.")]
    public float bulletDamage = 10f;

    [Header("Audio / VFX")]
    public AudioClip shootClip;
    [Range(0f, 1f)] public float shootVolume = 0.8f;
    public AudioSource audioSource;
    public ParticleSystem muzzleFlash;
    public GameObject muzzleFlashPrefab;
    public float muzzleFlashLifetime = 0.25f;

    [Header("Muzzle Flash Light")]
    public Light muzzleFlashLight;
    public float flashDuration = 0.05f;
    private float flashTimer = 0f;

    [Header("Health Bar")]
    public GameObject healthBarPrefab;
    [Tooltip("World-space offset from the player pivot to place the health bar.")]
    public Vector3 healthBarOffset = new Vector3(0f, 2f, 0f);
    [Tooltip("Optional: scale applied to the spawned health bar root.")]
    public Vector3 healthBarScale = Vector3.one;
    [Tooltip("Optional: smooth the follow position for the health bar (seconds to reach ~63%). 0 = snap.")]
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
    [Tooltip("Name of the combined trigger axis in Input Manager.")]
    public string triggerAxisName = "Triggers";
    [Tooltip("Flip the sign of the combined trigger axis before processing.")]
    public bool invertTriggerAxis = false;
    [Tooltip("If true, RT is the POSITIVE side of the combined axis; if false, RT is the NEGATIVE side.")]
    public bool rtIsPositive = true;
    [Tooltip("Fire when RT (derived 0..1) exceeds this value.")]
    [Range(0f, 1f)] public float rtFireThreshold = 0.35f;

    [Header("Aim (Stick vs Mouse Priority)")]
    [Range(0f, 1f)] public float rightStickAimDeadzone = 0.2f;
    [Range(0.05f, 1.0f)] public float inputPriorityGrace = 0.35f;
    [Range(0.1f, 8f)] public float mouseMovePixelsThreshold = 1.0f;

    [Header("Stick Snap")]
    [Tooltip("When the stick returns to zero, snap/hold the last stick direction briefly.")]
    public bool snapStickWhenZero = true;
    [Range(0.0f, 0.6f)] public float stickSnapHoldDuration = 0.18f;

    // ??????????????? DASH ???????????????
    [Header("Dash")]
    public bool dashEnabled = true;
    [Tooltip("Multiplies base moveSpeed while dashing.")]
    public float dashSpeedMultiplier = 3.0f;
    [Tooltip("Seconds the dash persists.")]
    public float dashDuration = 0.22f;
    [Tooltip("Cooldown between dashes in seconds.")]
    public float dashCooldown = 0.85f;
    [Tooltip("Optional easing (0..1 normalized time). If null, flat speed.")]
    public AnimationCurve dashSpeedCurve = AnimationCurve.EaseInOut(0, 1, 1, 1);
    [Tooltip("Keyboard fallback if no 'Dash' input is set.")]
    public KeyCode dashKey = KeyCode.LeftShift;
    [Tooltip("Optional invincibility during dash (seconds). 0 = off.")]
    public float dashIFrames = 0.18f;

    [Header("Dash Direction Controls")]
    [Tooltip("Require some movement to dash. If true and you're not moving, dash won't fire.")]
    public bool requireMoveForDash = true;
    [Tooltip("Minimum movement speed (m/s) to consider the player 'moving' for dash direction.")]
    public float minDashMoveSpeed = 0.15f;
    [Tooltip("If true, rotate the player to face the dash direction at dash start.")]
    public bool faceDashDirection = false;

    [Header("Dash VFX/SFX (optional)")]
    public AudioClip dashClip;
    [Range(0f, 1f)] public float dashVolume = 0.8f;
    public ParticleSystem dashVfx;

    [Header("Debug / Diagnostics")]
    [Tooltip("Enable verbose info logs in Console (warnings/errors always show).")]
    public bool debugLogs = false;

    private float currentHealth;
    private Camera mainCamera;
    private float nextFireTime = 0f;
    private Weapon currentWeapon;

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

    // ---------- Debug helpers ----------
    private void DLog(string msg) { if (debugLogs) Debug.Log(msg, this); }
    private void DLogFormat(string fmt, params object[] args) { if (debugLogs) Debug.LogFormat(this, fmt, args); }

    void Start()
    {
        controller = GetComponent<CharacterController>();
        mainCamera = Camera.main;
        currentHealth = maxHealth;

        // Unstick on spawn
        controller.enabled = false;
        transform.position += Vector3.up * spawnUnstickUp;
        controller.enabled = true;

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

        EquipWeapon(startingWeapon);

        _lastMousePos = Input.mousePosition;
        _mousePriorityUntil = _stickPriorityUntil = 0f;
        _lastStickDir = transform.forward;
    }

    void Update()
    {
        HandleMovement(); // (includes dash evaluation + application)
        AimWithPriorityAndSnap();

        // Mouse (Fire1) OR Right Trigger
        bool wantsMouseFire = Input.GetButton("Fire1");
        bool wantsRT = ReadRightTrigger01() > rtFireThreshold;

        if ((wantsMouseFire || wantsRT) && Time.time >= nextFireTime && currentWeapon != null)
        {
            Shoot();
            nextFireTime = Time.time + 1f / currentWeapon.fireRate;
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
                healthBarTransform.rotation = mainCamera.transform.rotation;
        }

        // Invulnerability timeout
        if (isInvulnerable && Time.time >= invulnEndTime)
            isInvulnerable = false;

        _lastMousePos = Input.mousePosition;

        DLog($"[PlayerController] grounded={grounded} planMag={new Vector2(planar.x, planar.z).magnitude:F2} trigRaw={SafeGetAxis(triggerAxisName):F2} rt01={ReadRightTrigger01():F2} dash={(isDashing ? "YES" : "no")}");
    }

    // ---- Trigger reading (robust) ----
    float ReadRightTrigger01()
    {
        float t = SafeGetAxis(triggerAxisName); // -1..+1 combined
        if (invertTriggerAxis) t = -t;
        return rtIsPositive ? Mathf.Max(0f, t) : Mathf.Max(0f, -t);
    }

    float SafeGetAxis(string name)
    {
        try { return Input.GetAxis(name); }
        catch { return 0f; }
    }

    bool SafeGetButtonDown(string name)
    {
        try { return Input.GetButtonDown(name); }
        catch { return false; }
    }

    // ---------------- SHOOTING ----------------
    void Shoot()
    {
        if (currentWeapon == null || firePoint == null) return;

        TriggerShotEffects();

        for (int i = 0; i < currentWeapon.bulletsPerShot; i++)
        {
            // Spread
            Quaternion spreadRotation = firePoint.rotation;
            if (currentWeapon.spreadAngle > 0f)
            {
                float angle = Random.Range(-currentWeapon.spreadAngle, currentWeapon.spreadAngle);
                spreadRotation *= Quaternion.Euler(0f, angle, 0f);
            }

            Vector3 dir = spreadRotation * Vector3.forward;

            // --- Ricochet path ---
            if (currentWeapon is RicochetCarbine rc)
            {
                GameObject go = Instantiate(rc.bulletPrefab, firePoint.position, Quaternion.LookRotation(dir, Vector3.up));

                // Ensure the projectile has a RicochetBullet (add if prefab doesn't include it)
                if (!go.TryGetComponent<RicochetBullet>(out var rico))
                    rico = go.AddComponent<RicochetBullet>();

                // Tag ownership for your damage gating
                rico.Initialize(gameObject);

                // Feed config (maps 1:1 to RicochetCarbine fields)
                rico.InitializeRicochet(
                    owner: gameObject,
                    startDamage: bulletDamage,
                    startSpeed: currentWeapon.bulletSpeed,
                    dir: dir,
                    maxBounces: rc.maxBounces,
                    speedLossPerBounce: rc.speedLossPerBounce,
                    damageLossPerBounce: rc.damageLossPerBounce,
                    ricochetSurfaces: rc.ricochetSurfaces,
                    enemyLayers: rc.enemyLayers,
                    ignoreLayers: rc.ignoreLayers,
                    biasRicochetTowardTargets: rc.biasRicochetTowardTargets,
                    ricochetAimCone: rc.ricochetAimCone,
                    ricochetTargetSearchRadius: rc.ricochetTargetSearchRadius,
                    minSpeedToContinue: rc.minSpeedToContinue,
                    maxLifeSeconds: rc.maxLifeSeconds,
                    bounceVfxPrefab: rc.bounceVfxPrefab,
                    bounceSfx: rc.bounceSfx
                );

                // No Rigidbody path; ricochet script handles travel via raycasts
                continue;
            }

            // --- Default projectile path (your existing behavior) ---
            GameObject proj = Instantiate(currentWeapon.bulletPrefab, firePoint.position, spreadRotation);

            if (proj.TryGetComponent<Rigidbody>(out var rb))
            {
                rb.isKinematic = false; // Ensure rigidbody is non-kinematic before setting velocity
                rb.linearVelocity = dir * currentWeapon.bulletSpeed;
            }

            if (proj.TryGetComponent<IBullet>(out var ib))
                ib.Initialize(gameObject);
            else
                Debug.LogWarning("[PlayerController] Bullet prefab missing IBullet component!", proj);
        }
    }

    void TriggerShotEffects()
    {
        if (shootClip != null)
        {
            if (audioSource != null) audioSource.PlayOneShot(shootClip, shootVolume);
            else AudioSource.PlayClipAtPoint(shootClip, firePoint.position, shootVolume);
        }

        if (muzzleFlash != null)
        {
            muzzleFlash.transform.position = firePoint.position;
            muzzleFlash.transform.rotation = firePoint.rotation;
            muzzleFlash.Play(true);
        }
        else if (muzzleFlashPrefab != null)
        {
            var fx = Instantiate(muzzleFlashPrefab, firePoint.position, firePoint.rotation, firePoint);
            Destroy(fx, muzzleFlashLifetime);
        }

        if (muzzleFlashLight != null)
        {
            muzzleFlashLight.enabled = true;
            flashTimer = flashDuration;
        }
    }

    // ---------------- WEAPON MANAGEMENT ----------------
    public void GiveWeapon()
    {
        if (startingWeapon != null)
        {
            EquipWeapon(startingWeapon);
            DLog("[PlayerController] GiveWeapon() called without an argument; equipped startingWeapon.");
        }
        else
        {
            Debug.LogWarning("[PlayerController] GiveWeapon() called without an argument, but no startingWeapon is set.");
        }
    }

    public void GiveWeapon(Weapon newWeapon)
    {
        EquipWeapon(newWeapon);
        if (newWeapon != null) DLog($"[PlayerController] Player received: {newWeapon.weaponName}");
    }

    public void EquipWeapon(Weapon newWeapon)
    {
        currentWeapon = newWeapon;
        if (newWeapon != null) DLog($"[PlayerController] Equipped: {newWeapon.weaponName}");
    }

    // ---------------- MOVEMENT (with Dash) ----------------
    void HandleMovement()
    {
        int mask = (groundLayer.value == 0) ? Physics.DefaultRaycastLayers : groundLayer.value;
        grounded = controller.isGrounded || SphereOrRayGround(mask, out _, out _);

        if (grounded) groundedTimer = groundedGrace;
        else groundedTimer = Mathf.Max(0f, groundedTimer - Time.deltaTime);

        if ((grounded || groundedTimer > 0f) && velocity.y < 0f)
            velocity.y = -2f;

        // Read input first so we can use it for dash direction decisions.
        Vector2 moveAxes = GetMoveInput();
        Vector3 moveDir;

        bool useWorldAxes = false;
        if (forceWorldAxesWhenTopDown && mainCamera != null)
        {
            float downDot = Vector3.Dot(mainCamera.transform.forward.normalized, Vector3.down);
            if (downDot >= topDownDotThreshold) useWorldAxes = true;
        }

        if (useWorldAxes || mainCamera == null)
        {
            moveDir = new Vector3(moveAxes.x, 0f, moveAxes.y);
        }
        else
        {
            Vector3 camF = Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up).normalized;
            Vector3 camR = Vector3.ProjectOnPlane(mainCamera.transform.right, Vector3.up).normalized;
            if (camF.sqrMagnitude < 1e-4f && camR.sqrMagnitude < 1e-4f)
            {
                camF = Vector3.forward;
                camR = Vector3.right;
            }
            moveDir = camR * moveAxes.x + camF * moveAxes.y;
        }
        if (moveDir.sqrMagnitude > 1f) moveDir.Normalize();

        // Dash input + state (now uses real movement)
        HandleDashInput(moveDir);

        // Desired planar velocity
        Vector3 desiredPlanar;
        if (isDashing)
        {
            float tNorm = Mathf.InverseLerp(dashEndTime - dashDuration, dashEndTime, Time.time);
            float mult = (dashSpeedCurve != null) ? dashSpeedCurve.Evaluate(Mathf.Clamp01(tNorm)) : 1f;
            desiredPlanar = dashDir * (moveSpeed * dashSpeedMultiplier * Mathf.Max(0.01f, mult));

            // Override smoothing while dashing for a snappy feel
            planar = desiredPlanar;
        }
        else
        {
            desiredPlanar = moveDir * moveSpeed;

            float dt = Time.deltaTime;
            float control = grounded ? 1f : airControl;
            if (desiredPlanar.sqrMagnitude > 0.0001f)
                planar = Vector3.MoveTowards(planar, desiredPlanar, acceleration * control * dt);
            else
                planar = Vector3.MoveTowards(planar, Vector3.zero, deceleration * (grounded ? 1 : airControl) * dt);
        }

        // Gravity & move
        velocity.y += gravity * Time.deltaTime;
        Vector3 finalVel = new Vector3(planar.x, velocity.y, planar.z);
        CollisionFlags flags = controller.Move(finalVel * Time.deltaTime);

        if ((flags & CollisionFlags.Below) != 0 && velocity.y < 0f)
            velocity.y = -2f;

        if ((flags & CollisionFlags.Sides) != 0 && planar.sqrMagnitude < 0.0001f)
        {
            Vector3 nudge = new Vector3(transform.forward.x, 0f, transform.forward.z) * unstickNudge;
            controller.Move(nudge);
        }
    }

    void HandleDashInput(Vector3 currentMoveDir)
    {
        if (!dashEnabled) { isDashing = false; return; }

        // End dash when time is up
        if (isDashing && Time.time >= dashEndTime)
        {
            isDashing = false;
        }

        // Press detection (any of: InputManager "Dash", keyboard LeftShift, Xbox 'B' fallback)
        bool dashPressed = SafeGetButtonDown("Dash") || Input.GetKeyDown(dashKey) || Input.GetKeyDown(KeyCode.JoystickButton1);
        if (!dashPressed) return;

        if (Time.time < nextDashAllowedTime || isDashing)
            return;

        // 1) Prefer current actual movement (planar velocity)
        Vector3 planarNoY = new Vector3(planar.x, 0f, planar.z);
        float planarSpeed = planarNoY.magnitude;

        Vector3 chosenDir = Vector3.zero;

        if (planarSpeed >= minDashMoveSpeed)
        {
            chosenDir = planarNoY / Mathf.Max(planarSpeed, 1e-6f);
        }
        else if (currentMoveDir.sqrMagnitude > 0.0001f)
        {
            // 2) Fall back to current input direction (pre-smoothing)
            chosenDir = currentMoveDir.normalized;
        }
        else
        {
            // 3) If movement is required, bail out; otherwise fallback to facing
            if (requireMoveForDash) return;
            chosenDir = transform.forward;
        }

        dashDir = chosenDir;

        if (faceDashDirection && dashDir.sqrMagnitude > 1e-6f)
            transform.rotation = Quaternion.LookRotation(dashDir, Vector3.up);

        // Begin dash
        isDashing = true;
        dashEndTime = Time.time + dashDuration;
        nextDashAllowedTime = Time.time + dashCooldown;

        // Optional i-frames
        if (dashIFrames > 0f)
        {
            isInvulnerable = true;
            invulnEndTime = Time.time + dashIFrames;
        }

        // Optional VFX/SFX
        if (dashClip != null)
        {
            if (audioSource != null) audioSource.PlayOneShot(dashClip, dashVolume);
            else AudioSource.PlayClipAtPoint(dashClip, transform.position, dashVolume);
        }
        if (dashVfx != null) dashVfx.Play(true);

        DLog($"[PlayerController] DASH dir={dashDir} speedRef={planarSpeed:F2}");
    }

    bool SphereOrRayGround(int mask, out Vector3 point, out Vector3 normal)
    {
        Vector3 origin = transform.position + Vector3.up * 0.05f;
        float dist = groundCheckDistance + groundSnapExtra;

        if (Physics.SphereCast(origin, groundCheckRadius, Vector3.down, out RaycastHit hit, dist, mask, QueryTriggerInteraction.Ignore))
        { point = hit.point; normal = hit.normal; return true; }

        if (Physics.Raycast(origin, Vector3.down, out hit, dist, mask, QueryTriggerInteraction.Ignore))
        { point = hit.point; normal = hit.normal; return true; }

        point = default; normal = Vector3.up; return false;
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
            // Just transitioned from active -> zero
            if (_stickWasActive && snapStickWhenZero)
            {
                if (_lastStickDir.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.LookRotation(_lastStickDir, Vector3.up); // SNAP
                _stickSnapUntil = now + stickSnapHoldDuration; // hold briefly to avoid jitter/mouse steal
            }
            _stickWasActive = false;
        }

        // Decide control (mouse wins ties)
        if (now < _mousePriorityUntil)
        {
            AimAtMouse();
            return;
        }

        if (now < _stickSnapUntil)
        {
            // During snap hold, keep last stick direction
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
                // Stick recently had priority but is idle�keep last look
                transform.rotation = Quaternion.LookRotation(_lastStickDir, Vector3.up);
            }
            return;
        }

        // Default fallback
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
        currentHealth = Mathf.Min(currentHealth + amount, maxHealth);
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

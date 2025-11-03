using UnityEngine;

/// <summary>
/// Simple top-down driver that reads the legacy Input Manager for Xbox-style pads.
/// - Move: Left Stick
/// - Aim: Right Stick
/// - Fire: RT
/// - Jump/Roll: A
/// - Interact: RB
/// - Pause: Start (routes to EscPauseUI if present, else sends Escape key)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class TopDownGamepadDriver : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    public float acceleration = 20f;
    public float deceleration = 30f;

    [Header("Aiming")]
    public Transform aimPivot;       // e.g., player body or weapon root to rotate
    public float aimTurnSpeed = 720f;

    [Header("Fire")]
    public MonoBehaviour fireProvider; // any script that has public void FireOnce()
    public float fireThreshold = 0.35f; // RT must exceed this to count as fire

    [Header("Jump / Roll")]
    public MonoBehaviour jumpProvider;  // any script that has public void JumpOrRoll()

    [Header("Interact")]
    public MonoBehaviour interactProvider; // any script that has public void Interact()

    private CharacterController cc;
    private Vector3 velocity;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        if (!aimPivot) aimPivot = transform;
    }

    void Update()
    {
        HandleMovement();
        HandleAim();
        HandleActions();
        HandlePause();
    }

    private void HandleMovement()
    {
        Vector2 ls = GamepadInput.LeftStick; // x = strafe, y = forward
        Vector3 desired = new Vector3(ls.x, 0f, ls.y) * moveSpeed;

        // Smooth acceleration/deceleration on XZ
        Vector3 horizontalVel = new Vector3(velocity.x, 0f, velocity.z);
        Vector3 diff = desired - horizontalVel;

        float rate = (desired.sqrMagnitude > horizontalVel.sqrMagnitude) ? acceleration : deceleration;
        horizontalVel += Vector3.ClampMagnitude(diff, rate * Time.deltaTime);

        // simple gravity so controller stays grounded nicely
        float y = velocity.y + Physics.gravity.y * Time.deltaTime;
        velocity = new Vector3(horizontalVel.x, y, horizontalVel.z);

        cc.Move(velocity * Time.deltaTime);

        // Optional: face move direction if no aim input
        if (aimPivot && GamepadInput.RightStick.sqrMagnitude < 0.01f && ls.sqrMagnitude > 0.01f)
        {
            Quaternion target = Quaternion.LookRotation(new Vector3(ls.x, 0f, ls.y), Vector3.up);
            aimPivot.rotation = Quaternion.RotateTowards(aimPivot.rotation, target, aimTurnSpeed * Time.deltaTime);
        }
    }

    private void HandleAim()
    {
        Vector2 rs = GamepadInput.RightStick;
        if (aimPivot && rs.sqrMagnitude > 0.01f)
        {
            Vector3 dir = new Vector3(rs.x, 0f, rs.y);
            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            aimPivot.rotation = Quaternion.RotateTowards(aimPivot.rotation, target, aimTurnSpeed * Time.deltaTime);
        }
    }

    private void HandleActions()
    {
        // Fire with RT
        if (GamepadInput.RT > fireThreshold && fireProvider)
        {
            var m = fireProvider.GetType().GetMethod("FireOnce");
            if (m != null) m.Invoke(fireProvider, null);
        }

        // Jump/Roll with A
        if (GamepadInput.A_Pressed && jumpProvider)
        {
            var m = jumpProvider.GetType().GetMethod("JumpOrRoll");
            if (m != null) m.Invoke(jumpProvider, null);
        }

        // Interact with RB (hold or press—change to a _Pressed variant if you prefer)
        if (GamepadInput.RB && interactProvider)
        {
            var m = interactProvider.GetType().GetMethod("Interact");
            if (m != null) m.Invoke(interactProvider, null);
        }
    }

    private void HandlePause()
    {
        if (GamepadInput.Start_Pressed)
        {
            // Updated to non-deprecated API
            var esc = FindFirstObjectByType<EscPauseUI>();
            if (esc != null)
            {
                // Simulate ESC by toggling timescale + cursor (or call your esc method if you have one)
                bool pausing = Time.timeScale > 0.5f;
                Time.timeScale = pausing ? 0f : 1f;
                Cursor.lockState = pausing ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = pausing;
            }
            else
            {
                // fallback: send Escape (handled elsewhere if you listen for it)
            }
        }
    }
}

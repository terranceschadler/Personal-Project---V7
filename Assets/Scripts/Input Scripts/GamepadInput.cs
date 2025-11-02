using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem; // Gamepad
#endif

/// <summary>
/// Unified gamepad wrapper:
/// - If the new Input System is available, reads from Gamepad.current
/// - Else uses legacy Input Manager axes with your chosen names:
///     LeftStickHorizontal, LeftStickVertical (Invert ON)
///     RightStickHorizontal, RightStickVertical (Invert ON on Y)
///     Triggers (combined: LT negative, RT positive)
/// Buttons map to standard JoystickButton indices (Xbox layout).
/// </summary>
public static class GamepadInput
{
    // ---- Legacy axis names expected in Project Settings ? Input Manager ----
    private const string AXIS_LX = "LeftStickHorizontal";
    private const string AXIS_LY = "LeftStickVertical";
    private const string AXIS_RX = "RightStickHorizontal";
    private const string AXIS_RY = "RightStickVertical";
    private const string AXIS_TRIG = "Triggers"; // combined LT..RT = -1..+1

    // ---------- Sticks ----------
    public static Vector2 LeftStick
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null) return Gamepad.current.leftStick.ReadValue();
#endif
            return new Vector2(Axis(AXIS_LX), Axis(AXIS_LY));
        }
    }

    public static Vector2 RightStick
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null) return Gamepad.current.rightStick.ReadValue();
#endif
            return new Vector2(Axis(AXIS_RX), Axis(AXIS_RY));
        }
    }

    // ---------- Triggers ----------
    /// Left Trigger 0..1
    public static float LT
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null) return Mathf.Clamp01(Gamepad.current.leftTrigger.ReadValue());
#endif
            float t = Axis(AXIS_TRIG); // -1..+1
            return Mathf.Clamp01(Mathf.Max(0f, -t));   // negative side = LT
        }
    }

    /// Right Trigger 0..1
    public static float RT
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null) return Mathf.Clamp01(Gamepad.current.rightTrigger.ReadValue());
#endif
            float t = Axis(AXIS_TRIG); // -1..+1
            return Mathf.Clamp01(Mathf.Max(0f, t));   // positive side = RT
        }
    }

    /// Combined triggers in [-1..+1] (RT-LT)
    public static float CombinedTriggers
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Gamepad.current != null)
                return Mathf.Clamp((RT - LT), -1f, 1f);
#endif
            return Axis(AXIS_TRIG);
        }
    }

    // ---------- Buttons (Xbox layout) ----------
    public static bool A => Key(KeyCode.JoystickButton0);
    public static bool A_Pressed => KeyDown(KeyCode.JoystickButton0);
    public static bool B => Key(KeyCode.JoystickButton1);
    public static bool B_Pressed => KeyDown(KeyCode.JoystickButton1);
    public static bool X => Key(KeyCode.JoystickButton2);
    public static bool X_Pressed => KeyDown(KeyCode.JoystickButton2);
    public static bool Y => Key(KeyCode.JoystickButton3);
    public static bool Y_Pressed => KeyDown(KeyCode.JoystickButton3);
    public static bool LB => Key(KeyCode.JoystickButton4);
    public static bool LB_Pressed => KeyDown(KeyCode.JoystickButton4);
    public static bool RB => Key(KeyCode.JoystickButton5);
    public static bool RB_Pressed => KeyDown(KeyCode.JoystickButton5);
    public static bool Back => Key(KeyCode.JoystickButton6);
    public static bool Back_Pressed => KeyDown(KeyCode.JoystickButton6);
    public static bool Start => Key(KeyCode.JoystickButton7);
    public static bool Start_Pressed => KeyDown(KeyCode.JoystickButton7);
    public static bool LStickClick => Key(KeyCode.JoystickButton8);
    public static bool LStickClick_Pressed => KeyDown(KeyCode.JoystickButton8);
    public static bool RStickClick => Key(KeyCode.JoystickButton9);
    public static bool RStickClick_Pressed => KeyDown(KeyCode.JoystickButton9);

    public static bool AnyGamepadConnected
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Gamepad.current != null;
#else
            return Mathf.Abs(Axis(AXIS_LX))   > 0.01f ||
                   Mathf.Abs(Axis(AXIS_LY))   > 0.01f ||
                   Mathf.Abs(Axis(AXIS_RX))   > 0.01f ||
                   Mathf.Abs(Axis(AXIS_RY))   > 0.01f ||
                   Mathf.Abs(Axis(AXIS_TRIG)) > 0.01f;
#endif
        }
    }

    // ---------- helpers ----------
    private static float Axis(string name)
    {
        try { return Input.GetAxis(name); }
        catch { return 0f; } // missing axis returns safe 0 instead of throwing
    }
    private static bool Key(KeyCode c) => Input.GetKey(c);
    private static bool KeyDown(KeyCode c) => Input.GetKeyDown(c);
}

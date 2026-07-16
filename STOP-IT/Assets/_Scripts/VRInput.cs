using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// STOP IT! — VRInput
/// Shared, lazily-built reader for the Quest controller index TRIGGERS, used by the
/// world-space UI (menu pointer click + end/lose-screen "press to continue").
///
/// Mirrors the binding strategy in <see cref="XRLocomotionBinder"/>.BuildHoldAction so it
/// resolves on the XRI Default asset whatever the exact control naming. WasPressedThisFrame
/// is frame-based, so any number of callers can poll it in the same frame without
/// "consuming" the press.
/// </summary>
public static class VRInput
{
    private static InputAction _left;
    private static InputAction _right;

    private static void Ensure()
    {
        if (_left != null) return;
        _left  = Build("LeftHand");
        _right = Build("RightHand");
        _left.Enable();
        _right.Enable();
    }

    private static InputAction Build(string hand)
    {
        var a = new InputAction($"VRTrigger_{hand}", InputActionType.Button);
        a.AddBinding($"<XRController>{{{hand}}}/triggerPressed");
        a.AddBinding($"<XRController>{{{hand}}}/triggerButton");
        a.AddBinding($"<XRController>{{{hand}}}/trigger"); // axis as button
        return a;
    }

    /// <summary>True on the frame either index trigger is pressed (rising edge).</summary>
    public static bool AnyTriggerDown()
    {
        Ensure();
        return _left.WasPressedThisFrame() || _right.WasPressedThisFrame();
    }

    /// <summary>True while either index trigger is held.</summary>
    public static bool AnyTriggerHeld()
    {
        Ensure();
        return _left.IsPressed() || _right.IsPressed();
    }

    /// <summary>True on the frame the LEFT index trigger is pressed (rising edge).</summary>
    public static bool LeftTriggerDown()  { Ensure(); return _left.WasPressedThisFrame(); }

    /// <summary>True on the frame the RIGHT index trigger is pressed (rising edge).</summary>
    public static bool RightTriggerDown() { Ensure(); return _right.WasPressedThisFrame(); }
}

using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Utils;

/// <summary>
/// Gamepad polling. Now a thin adapter over <see cref="Engine.Input"/> — the Raylib enums stay in the
/// signatures only because Configuration still persists bindings as Raylib's PadButton; the values
/// are identical to <see cref="PadButton"/>/<see cref="PadAxis"/>, so the cast is a no-op.
/// </summary>
public static class Controller
{
    public static bool IsButtonDown(PadButton button) => Engine.Input.IsPadDown((PadButton)button);

    /// <summary>
    /// The stick reading, scaled by the configured sensitivity and clamped back to [-1, 1]. PlayerController
    /// compares this against a fixed 0.8 threshold, so scaling here is what actually changes how far the
    /// stick must travel before the player moves.
    /// </summary>
    public static float GetGamepadAxisValue(PadAxis axis)
    {
        float value = Engine.Input.GetPadAxis(axis) * Configuration.Config.GamepadSensitivity;
        return Math.Clamp(value, -1f, 1f);
    }
}

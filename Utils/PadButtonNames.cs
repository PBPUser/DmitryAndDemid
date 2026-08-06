using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils.DualSense;

namespace DmitryAndDemid.Utils;

/// <summary>
/// How a pad button is spelled on screen. The engine's <see cref="PadButton"/> names are positional
/// ("RightFaceDown") because they have to fit every pad; a player holding a DualSense is looking at a Cross,
/// so the rebinding screen says Cross.
///
/// Everything here is plain ASCII on purpose — the bundled fonts only bake 32..126, so a real ✕ / ◯ / □ glyph
/// would render as nothing at all.
/// </summary>
public static class PadButtonNames
{
    /// <summary>Names as they are printed on a DualShock/DualSense.</summary>
    public static string PlayStation(PadButton button) => button switch
    {
        // The d-pad directions are spelled "Dpad Up" rather than "Up" because these strings double as
        // translation keys, and a key as generic as "Up" would be a landmine for anything else needing one.
        PadButton.LeftFaceUp => "Dpad Up",
        PadButton.LeftFaceRight => "Dpad Right",
        PadButton.LeftFaceDown => "Dpad Down",
        PadButton.LeftFaceLeft => "Dpad Left",
        PadButton.RightFaceUp => "Triangle",
        PadButton.RightFaceRight => "Circle",
        PadButton.RightFaceDown => "Cross",
        PadButton.RightFaceLeft => "Square",
        PadButton.LeftTrigger1 => "L1",
        PadButton.LeftTrigger2 => "L2",
        PadButton.RightTrigger1 => "R1",
        PadButton.RightTrigger2 => "R2",
        PadButton.MiddleLeft => "Create",
        PadButton.Middle => "PS",
        PadButton.MiddleRight => "Options",
        PadButton.LeftThumb => "L3",
        PadButton.RightThumb => "R3",
        _ => "-",
    };

    /// <summary>The engine's own positional name, which is what a generic pad gets.</summary>
    public static string Generic(PadButton button) =>
        button == PadButton.Unknown ? "-" : button.ToString();

    public static string Describe(PadButton button, bool playStationLayout) =>
        playStationLayout ? PlayStation(button) : Generic(button);

    /// <summary>The name for whatever pad is actually plugged in right now.</summary>
    public static string Describe(PadButton button) => Describe(button, DualSensePad.IsConnected);
}

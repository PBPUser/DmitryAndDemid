using DmitryAndDemid.Rendering;

namespace DmitryAndDemid.Utils;

/// <summary>
/// "Is the player touching anything right now?" — used by the title-screen attract loop to decide when to
/// start a demo, and by the demo playback to bail back to the title on the first input. There is no all-keys
/// query in the input layer, so this checks the keys and pad buttons the game actually uses, plus touch.
/// </summary>
public static class AttractInput
{
    private static readonly KeyCode[] Keys =
    [
        KeyCode.Up, KeyCode.Down, KeyCode.Left, KeyCode.Right,
        KeyCode.Z, KeyCode.X, KeyCode.Enter, KeyCode.Escape, KeyCode.LeftShift,
    ];

    private static readonly PadButton[] Pads =
    [
        PadButton.LeftFaceUp, PadButton.LeftFaceDown, PadButton.LeftFaceLeft, PadButton.LeftFaceRight,
        PadButton.RightFaceDown, PadButton.RightFaceRight, PadButton.RightFaceLeft, PadButton.RightFaceUp,
    ];

    public static bool AnyInput()
    {
        if (Engine.Input.TouchCount > 0)
            return true;
        foreach (KeyCode key in Keys)
            if (Engine.Input.IsKeyDown(key))
                return true;
        foreach (PadButton button in Pads)
            if (Controller.IsButtonDown(button))
                return true;
        return false;
    }
}

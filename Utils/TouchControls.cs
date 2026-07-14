using System.Numerics;
using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Utils;

/// <summary>
/// On-screen touch controls for gameplay.
///
/// Dragging anywhere inside the playfield moves the ship 1:1 with the finger, and holds fire while you drag.
/// That is how touch danmaku is played — a virtual d-pad cannot deliver the precision a bullet hell needs —
/// and it means the only buttons required are BOMB and FOCUS, placed in the UI strip beside the playfield.
///
/// Everything here works in the game's internal coordinate space (Runtime.Width x Height). Touch points
/// arrive in window pixels, so they go through Runtime.WindowToGame first — which is what makes the controls
/// land correctly in the letterboxed fullscreen modes.
/// </summary>
public static class TouchControls
{
    public static bool Enabled => Configuration.Config.TouchControls;

    /// <summary>Layout in the game's 640x480 design units; scaled to the internal resolution on use.</summary>
    private const float ButtonWidth = 84, ButtonHeight = 52;
    private const float BombX = 424, BombY = 404;
    private const float FocusX = 524, FocusY = 404;

    /// <summary>The playfield, in game space. Matches GameplayScreen's destination rectangle.</summary>
    public static Rect Playfield => new(
        32 * Runtime.CurrentRuntime.ScaleF,
        16 * Runtime.CurrentRuntime.ScaleF,
        384 * Runtime.CurrentRuntime.ScaleF,
        448 * Runtime.CurrentRuntime.ScaleF);

    public static Rect BombButton => Scaled(BombX, BombY);
    public static Rect FocusButton => Scaled(FocusX, FocusY);

    private static Rect Scaled(float x, float y)
    {
        float scale = Runtime.CurrentRuntime.ScaleF;
        return new Rect(x * scale, y * scale, ButtonWidth * scale, ButtonHeight * scale);
    }

    /// <summary>Where the dragging finger was last frame, in game space. Null when nothing is dragging.</summary>
    private static Vector2? DragPosition;

    public static bool IsDragging => DragPosition != null;
    public static bool BombHeld { get; private set; }
    public static bool FocusHeld { get; private set; }

    /// <summary>
    /// How far the dragging finger moved since the last frame, in PLAYFIELD units (the 384x448 space the
    /// player's coordinates live in). Zero when not dragging.
    /// </summary>
    public static Vector2 DragDelta { get; private set; }

    /// <summary>Poll once per tick, before the player is moved.</summary>
    public static void Update()
    {
        DragDelta = Vector2.Zero;
        BombHeld = FocusHeld = false;

        if (!Enabled)
        {
            DragPosition = null;
            return;
        }

        Vector2? drag = null;

        for (int i = 0; i < Engine.Input.TouchCount; i++)
        {
            Vector2 point = Runtime.CurrentRuntime.WindowToGame(Engine.Input.GetTouchPosition(i));

            if (Contains(BombButton, point))
                BombHeld = true;
            else if (Contains(FocusButton, point))
                FocusHeld = true;
            else if (Contains(Playfield, point))
                drag ??= point;   // the first finger in the playfield is the one that steers
        }

        if (drag is { } current)
        {
            if (DragPosition is { } previous)
                DragDelta = (current - previous) / Runtime.CurrentRuntime.ScaleF;
            DragPosition = current;
        }
        else
        {
            DragPosition = null;
        }
    }

    private static bool Contains(Rect rect, Vector2 point) =>
        point.X >= rect.X && point.X <= rect.X + rect.Width &&
        point.Y >= rect.Y && point.Y <= rect.Y + rect.Height;

    /// <summary>Draws the buttons. Called from GameplayScreen, which draws in game space.</summary>
    public static void Draw()
    {
        if (!Enabled)
            return;

        DrawButton(BombButton, "BOMB", BombHeld);
        DrawButton(FocusButton, "FOCUS", FocusHeld);
    }

    private static void DrawButton(Rect rect, string label, bool held)
    {
        Rgba fill = held ? new Rgba(255, 255, 255, 140) : new Rgba(255, 255, 255, 60);
        Rgba border = held ? Rgba.White : new Rgba(255, 255, 255, 160);

        DrawRectangleRec(rect, fill);
        DrawRectangleRec(new Rect(rect.X, rect.Y, rect.Width, 2), border);
        DrawRectangleRec(new Rect(rect.X, rect.Y + rect.Height - 2, rect.Width, 2), border);
        DrawRectangleRec(new Rect(rect.X, rect.Y, 2, rect.Height), border);
        DrawRectangleRec(new Rect(rect.X + rect.Width - 2, rect.Y, 2, rect.Height), border);

        FontHandle font = Runtime.CurrentRuntime.Fonts["kodemono"];
        float size = 16 * Runtime.CurrentRuntime.ScaleF;
        Vector2 measure = MeasureTextEx(font, label, size, 1);
        DrawTextEx(font, label,
            new Vector2(rect.X + (rect.Width - measure.X) / 2, rect.Y + (rect.Height - measure.Y) / 2),
            size, 1, Rgba.White);
    }
}

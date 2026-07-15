using System.Numerics;
using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Utils;

/// <summary>
/// On-screen touch controls for gameplay.
///
/// Movement has two styles (Config.TouchStick): finger-follow — dragging inside the playfield moves the ship
/// 1:1 with the finger — or a virtual analog stick that pushes the ship in the deflected direction. On top of
/// that are three buttons: BOMB, FOCUS and an optional SHOOT (Config.TouchShootButton). Every control's
/// position is customizable (Screens/TouchLayoutScreen writes the design-unit coords into config).
///
/// The button/stick glyphs come from the Assets/Textures/controlls.png atlas rather than being drawn as
/// labelled rectangles. Everything works in the game's internal coordinate space (Runtime.Width x Height);
/// touch points arrive in window pixels and go through Runtime.WindowToGame first, which is what makes the
/// controls land correctly in the letterboxed fullscreen modes.
/// </summary>
public static class TouchControls
{
    public static bool Enabled => Configuration.Config.TouchControls;

    /// <summary>Which glyph a control draws. Source rects below index into Assets/Textures/controlls.png.</summary>
    public enum Control { Stick, Focus, Bomb, Shoot }

    /// <summary>Where each glyph sits in the 1024x1024 controlls.png atlas (measured from the drawing).</summary>
    public static Rect SourceOf(Control control) => control switch
    {
        Control.Stick => new Rect(20, 84, 246, 194),
        Control.Focus => new Rect(364, 72, 208, 202),
        Control.Bomb => new Rect(338, 344, 244, 168),
        Control.Shoot => new Rect(324, 550, 232, 166),
        _ => default,
    };

    /// <summary>Control sizes in 640x480 design units. The stick is larger to give a thumb room to swing.</summary>
    public const float ButtonWidth = 88, ButtonHeight = 60;
    public const float StickWidth = 128, StickHeight = 100;

    private static float S => Runtime.CurrentRuntime.ScaleF;

    /// <summary>The playfield, in game space. Matches GameplayScreen's destination rectangle.</summary>
    public static Rect Playfield => new(32 * S, 16 * S, 384 * S, 448 * S);

    // Control rects in game space, built from the customizable design-unit positions in config.
    public static Rect BombButton => new(Configuration.Config.TouchBombX * S, Configuration.Config.TouchBombY * S, ButtonWidth * S, ButtonHeight * S);
    public static Rect FocusButton => new(Configuration.Config.TouchFocusX * S, Configuration.Config.TouchFocusY * S, ButtonWidth * S, ButtonHeight * S);
    public static Rect ShootButton => new(Configuration.Config.TouchShootX * S, Configuration.Config.TouchShootY * S, ButtonWidth * S, ButtonHeight * S);
    public static Rect StickBase => new(Configuration.Config.TouchStickX * S, Configuration.Config.TouchStickY * S, StickWidth * S, StickHeight * S);

    /// <summary>The pause button, in the top-right corner of the playfield. Shown in live play and replay (so a
    /// viewer can pause/leave), never in the attract demo — which bails on any touch anyway.</summary>
    public static Rect PauseButton
    {
        get
        {
            Rect pf = Playfield;
            float sz = 34 * S;
            return new Rect(pf.X + pf.Width - sz - 6 * S, pf.Y + 6 * S, sz, sz);
        }
    }

    private static Vector2 StickCenter => new(StickBase.X + StickBase.Width / 2, StickBase.Y + StickBase.Height / 2);
    private static float StickRadius => MathF.Min(StickBase.Width, StickBase.Height) / 2;

    // --- state read by PlayerController ---
    private static Vector2? DragPosition;

    public static bool IsDragging => DragPosition != null;
    public static bool BombHeld { get; private set; }
    public static bool FocusHeld { get; private set; }
    public static bool ShootHeld { get; private set; }

    /// <summary>Finger movement since last frame, in PLAYFIELD units (the 384x448 space). Drag style only.</summary>
    public static Vector2 DragDelta { get; private set; }

    /// <summary>Stick deflection, normalized to [-1,1] per axis (magnitude 0..1). Stick style only.</summary>
    public static Vector2 MoveVector { get; private set; }

    /// <summary>Whether touch is currently asking to fire — the SHOOT button, or auto-fire while moving.</summary>
    public static bool WantsFire { get; private set; }

    // Knob offset from the stick centre for drawing, game space; zero when not deflected.
    private static Vector2 StickKnob;

    /// <summary>Poll once per tick, before the player is moved.</summary>
    public static void Update()
    {
        DragDelta = Vector2.Zero;
        MoveVector = Vector2.Zero;
        BombHeld = FocusHeld = ShootHeld = false;
        StickKnob = Vector2.Zero;

        if (!Enabled)
        {
            DragPosition = null;
            WantsFire = false;
            return;
        }

        bool stickMode = Configuration.Config.TouchStick;
        bool showShoot = Configuration.Config.TouchShootButton;

        Vector2? drag = null;
        Vector2? stickFinger = null;

        for (int i = 0; i < Engine.Input.TouchCount; i++)
        {
            Vector2 point = Runtime.CurrentRuntime.WindowToGame(Engine.Input.GetTouchPosition(i));

            if (Contains(PauseButton, point))
                continue;   // reserved for the pause button — a tap here must not steer or fire
            if (Contains(BombButton, point))
                BombHeld = true;
            else if (Contains(FocusButton, point))
                FocusHeld = true;
            else if (showShoot && Contains(ShootButton, point))
                ShootHeld = true;
            else if (stickMode)
            {
                // A finger anywhere near the stick grabs it — a generous capture radius, so the thumb need
                // not land dead-centre.
                if (Vector2.Distance(point, StickCenter) <= StickRadius * 1.8f)
                    stickFinger ??= point;
            }
            else if (Contains(Playfield, point))
                drag ??= point; // the first finger in the playfield is the one that steers
        }

        if (stickMode)
        {
            DragPosition = null;
            if (stickFinger is { } finger)
            {
                Vector2 offset = finger - StickCenter;
                float len = offset.Length();
                if (len > StickRadius)
                    offset *= StickRadius / len;
                StickKnob = offset;
                MoveVector = offset / StickRadius;
            }
        }
        else if (drag is { } current)
        {
            if (DragPosition is { } previous)
                DragDelta = (current - previous) / S;
            DragPosition = current;
        }
        else
        {
            DragPosition = null;
        }

        bool moving = stickMode ? MoveVector != Vector2.Zero : IsDragging;
        WantsFire = ShootHeld || moving;
    }

    private static bool Contains(Rect rect, Vector2 point) =>
        point.X >= rect.X && point.X <= rect.X + rect.Width &&
        point.Y >= rect.Y && point.Y <= rect.Y + rect.Height;

    /// <summary>Draws the controls. Called from GameplayScreen, which draws in game space.</summary>
    public static void Draw()
    {
        if (!Enabled)
            return;

        if (Configuration.Config.TouchStick)
            DrawStick(StickKnob != Vector2.Zero);
        DrawControl(Control.Bomb, BombButton, (byte)(BombHeld ? 255 : 190), BombHeld);
        DrawControl(Control.Focus, FocusButton, (byte)(FocusHeld ? 255 : 190), FocusHeld);
        if (Configuration.Config.TouchShootButton)
            DrawControl(Control.Shoot, ShootButton, (byte)(ShootHeld ? 255 : 190), ShootHeld);
    }

    /// <summary>Draws the pause button (two bars on a dim panel). The caller decides when it is shown — live
    /// play and replay, but never the demo.</summary>
    public static void DrawPause()
    {
        if (!Enabled)
            return;
        Rect r = PauseButton;
        DrawRectangleRec(r, new Rgba(0, 0, 0, 90));
        float bw = r.Width * 0.16f, gap = r.Width * 0.16f, bh = r.Height * 0.46f;
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        DrawRectangleRec(new Rect(cx - gap / 2 - bw, cy - bh / 2, bw, bh), Rgba.White with { A = 210 });
        DrawRectangleRec(new Rect(cx + gap / 2, cy - bh / 2, bw, bh), Rgba.White with { A = 210 });
    }

    private static int PausePrevTouchCount;

    /// <summary>True on the frame a finger first lands on the pause button. Independent of <see cref="Update"/>
    /// so it works during replay playback, where the live controller (and thus Update) never runs.</summary>
    public static bool ConsumePauseTap()
    {
        int count = Engine.Input.TouchCount;
        bool tapped = count > 0 && PausePrevTouchCount == 0;
        PausePrevTouchCount = count;
        if (!tapped)
            return false;
        Vector2 p = Runtime.CurrentRuntime.WindowToGame(Engine.Input.GetTouchPosition(0));
        return Contains(PauseButton, p);
    }

    /// <summary>Draws one control's sprite into a rect. Public so the layout editor renders the same visuals.</summary>
    public static void DrawControl(Control control, Rect dest, byte alpha, bool pressed = false)
    {
        if (pressed)
            DrawRectangleRec(dest, new Rgba(255, 255, 255, 40));
        TextureHandle atlas = Runtime.CurrentRuntime.Textures["controlls.png"];
        DrawTexturePro(atlas, SourceOf(control), dest, Vector2.Zero, 0, Rgba.White with { A = alpha });
    }

    /// <summary>Draws the stick base plus a knob dot at the current deflection. Used by gameplay and the editor.</summary>
    public static void DrawStick(bool active, byte alpha = 190)
    {
        DrawControl(Control.Stick, StickBase, (byte)(active ? 255 : alpha), active);
        Vector2 knob = StickCenter + StickKnob;
        float r = 13 * S;
        DrawRectangleRec(new Rect(knob.X - r, knob.Y - r, r * 2, r * 2), new Rgba(255, 255, 255, 180));
    }
}

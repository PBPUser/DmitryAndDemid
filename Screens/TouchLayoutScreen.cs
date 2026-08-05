using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

/// <summary>
/// Drag-to-reposition editor for the on-screen touch controls. Shows the BOMB / FOCUS / SHOOT buttons and the
/// movement stick exactly as gameplay draws them (same controlls.png glyphs), and lets a finger — or, on
/// desktop, the mouse, which the backends report as a single touch point — drag any of them to a new spot.
/// A row of buttons across the top toggles the stick and the shoot button, resets the layout, and leaves.
///
/// Positions are stored in Configuration in 640x480 design units, so they survive resolution and orientation
/// changes; this screen converts pointer coordinates (game space) into those units via Runtime.ScaleF.
/// </summary>
public class TouchLayoutScreen : Screen
{
    private static Configuration Config => Configuration.Config;
    private static float S => Runtime.CurrentRuntime.ScaleF;

    private TouchControls.Control? Dragging;
    private Vector2 GrabOffset;
    private bool WasDown;
    private double LastButtonPress;

    // The layout as it was when the editor opened, so CANCEL can revert every change (positions and toggles).
    private readonly (float BombX, float BombY, float FocusX, float FocusY, float ShootX, float ShootY,
        float StickX, float StickY, bool Stick, bool Shoot) Snapshot;

    public TouchLayoutScreen()
    {
        Snapshot = (Config.TouchBombX, Config.TouchBombY, Config.TouchFocusX, Config.TouchFocusY,
            Config.TouchShootX, Config.TouchShootY, Config.TouchStickX, Config.TouchStickY,
            Config.TouchStick, Config.TouchShootButton);
    }

    // The default layout, restored by RESET. Mirrors the field defaults in Configuration.
    private static readonly (float BombX, float BombY, float FocusX, float FocusY, float ShootX, float ShootY, float StickX, float StickY)
        Defaults = (424, 404, 524, 404, 474, 336, 452, 150);

    private static (float W, float H) SizeOf(TouchControls.Control c) => c == TouchControls.Control.Stick
        ? (TouchControls.StickWidth, TouchControls.StickHeight)
        : (TouchControls.ButtonWidth, TouchControls.ButtonHeight);

    private static Rect RectOf(TouchControls.Control c) => c switch
    {
        TouchControls.Control.Bomb => TouchControls.BombButton,
        TouchControls.Control.Focus => TouchControls.FocusButton,
        TouchControls.Control.Shoot => TouchControls.ShootButton,
        TouchControls.Control.Stick => TouchControls.StickBase,
        _ => default,
    };

    private static bool EnabledIn(TouchControls.Control c) => c switch
    {
        TouchControls.Control.Shoot => Config.TouchShootButton,
        TouchControls.Control.Stick => Config.TouchStick,
        _ => true,
    };

    private static (float X, float Y) GetPos(TouchControls.Control c) => c switch
    {
        TouchControls.Control.Bomb => (Config.TouchBombX, Config.TouchBombY),
        TouchControls.Control.Focus => (Config.TouchFocusX, Config.TouchFocusY),
        TouchControls.Control.Shoot => (Config.TouchShootX, Config.TouchShootY),
        TouchControls.Control.Stick => (Config.TouchStickX, Config.TouchStickY),
        _ => (0, 0),
    };

    private static void SetPos(TouchControls.Control c, float x, float y)
    {
        switch (c)
        {
            case TouchControls.Control.Bomb: Config.TouchBombX = x; Config.TouchBombY = y; break;
            case TouchControls.Control.Focus: Config.TouchFocusX = x; Config.TouchFocusY = y; break;
            case TouchControls.Control.Shoot: Config.TouchShootX = x; Config.TouchShootY = y; break;
            case TouchControls.Control.Stick: Config.TouchStickX = x; Config.TouchStickY = y; break;
        }
    }

    // The controls, top-most last so an overlap resolves to the one drawn on top.
    private static readonly TouchControls.Control[] AllControls =
        [TouchControls.Control.Stick, TouchControls.Control.Bomb, TouchControls.Control.Focus, TouchControls.Control.Shoot];

    private enum TopButton { Stick, Shoot, Reset, Cancel, Done }
    private static readonly TopButton[] TopButtons =
        [TopButton.Stick, TopButton.Shoot, TopButton.Reset, TopButton.Cancel, TopButton.Done];

    // The editor-button glyphs the user drew into controlls.png (reset ↺, cancel ✗, done ✓). The stick and
    // shoot toggles reuse the gameplay control glyphs.
    private static readonly Rect SrcReset = new(756, 74, 134, 110);
    private static readonly Rect SrcCancel = new(646, 806, 90, 98);
    private static readonly Rect SrcDone = new(548, 786, 84, 110);

    private Rect TopButtonRect(int index)
    {
        float bw = 92 * S, bh = 57 * S, gap = 14 * S;
        float total = TopButtons.Length * bw + (TopButtons.Length - 1) * gap;
        float startX = (Runtime.CurrentRuntime.Width - total) / 2f;
        return new Rect(startX + index * (bw + gap), 14 * S, bw, bh);
    }

    private Rect IconOf(TopButton b) => b switch
    {
        TopButton.Stick => TouchControls.SourceOf(TouchControls.Control.Stick),
        TopButton.Shoot => TouchControls.SourceOf(TouchControls.Control.Shoot),
        TopButton.Reset => SrcReset,
        TopButton.Cancel => SrcCancel,
        TopButton.Done => SrcDone,
        _ => default,
    };

    public override void TopUpdate()
    {
        bool down = Engine.Input.TouchCount > 0;
        Vector2 p = down ? Runtime.CurrentRuntime.WindowToGame(Engine.Input.GetTouchPosition(0)) : default;
        bool pressEdge = down && !WasDown;

        if (IsKeyDown(KeyCode.Escape) || Controller.IsButtonDown(PadButton.RightFaceRight))
        {
            Leave();
            return;
        }

        if (pressEdge)
        {
            // Top buttons take priority over dragging a control that sits under them.
            int hitButton = -1;
            for (int i = 0; i < TopButtons.Length; i++)
                if (Contains(TopButtonRect(i), p))
                    hitButton = i;

            if (hitButton >= 0 && GetTime() - LastButtonPress > 0.2)
            {
                LastButtonPress = GetTime();
                Helper.PlaySound(Runtime.CurrentRuntime.Sounds["button"]);
                Activate(TopButtons[hitButton]);
            }
            else
            {
                // Grab a control (top-most first) to start dragging.
                for (int i = AllControls.Length - 1; i >= 0; i--)
                {
                    Rect r = RectOf(AllControls[i]);
                    if (Contains(r, p))
                    {
                        Dragging = AllControls[i];
                        GrabOffset = new Vector2(p.X - r.X, p.Y - r.Y);
                        break;
                    }
                }
            }
        }
        else if (down && Dragging is { } c)
        {
            (float w, float h) = SizeOf(c);
            float x = (p.X - GrabOffset.X) / S;
            float y = (p.Y - GrabOffset.Y) / S;
            float maxX = Runtime.CurrentRuntime.Width / S - w;
            float maxY = Runtime.CurrentRuntime.Height / S - h;
            SetPos(c, Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY));
        }
        else if (!down && Dragging != null)
        {
            Dragging = null;
            Config.Save();
        }

        WasDown = down;
    }

    private void Activate(TopButton b)
    {
        switch (b)
        {
            case TopButton.Stick: Config.TouchStick = !Config.TouchStick; Config.Save(); break;
            case TopButton.Shoot: Config.TouchShootButton = !Config.TouchShootButton; Config.Save(); break;
            case TopButton.Reset:
                Config.TouchBombX = Defaults.BombX; Config.TouchBombY = Defaults.BombY;
                Config.TouchFocusX = Defaults.FocusX; Config.TouchFocusY = Defaults.FocusY;
                Config.TouchShootX = Defaults.ShootX; Config.TouchShootY = Defaults.ShootY;
                Config.TouchStickX = Defaults.StickX; Config.TouchStickY = Defaults.StickY;
                Config.Save();
                break;
            case TopButton.Cancel:
                Config.TouchBombX = Snapshot.BombX; Config.TouchBombY = Snapshot.BombY;
                Config.TouchFocusX = Snapshot.FocusX; Config.TouchFocusY = Snapshot.FocusY;
                Config.TouchShootX = Snapshot.ShootX; Config.TouchShootY = Snapshot.ShootY;
                Config.TouchStickX = Snapshot.StickX; Config.TouchStickY = Snapshot.StickY;
                Config.TouchStick = Snapshot.Stick; Config.TouchShootButton = Snapshot.Shoot;
                Config.Save();
                Leave();
                break;
            case TopButton.Done: Leave(); break;
        }
    }

    private void Leave()
    {
        Config.Save();
        Runtime.CurrentRuntime.RemoveScreen(this);
    }

    private static bool Contains(Rect rect, Vector2 point) =>
        point.X >= rect.X && point.X <= rect.X + rect.Width &&
        point.Y >= rect.Y && point.Y <= rect.Y + rect.Height;

    public override void Render()
    {
        int w = Runtime.CurrentRuntime.Width, h = Runtime.CurrentRuntime.Height;

        // Dim the screen behind, and outline the playfield so the layout has a frame of reference.
        DrawRectangle(0, 0, w, h, new Rgba(0, 0, 0, 190));
        Outline(TouchControls.Playfield, new Rgba(120, 120, 160, 200), 2 * S);

        // Every control, at its current position. Disabled ones (stick/shoot toggled off) show dimmed.
        foreach (TouchControls.Control c in AllControls)
        {
            byte alpha = EnabledIn(c) ? (byte)235 : (byte)70;
            bool grabbed = Dragging == c;
            TouchControls.DrawControl(c, RectOf(c), (byte)(grabbed ? 255 : alpha), grabbed);
        }

        FontHandle font = Runtime.CurrentRuntime.Fonts["kodemono"];
        for (int i = 0; i < TopButtons.Length; i++)
        {
            Rect r = TopButtonRect(i);
            bool on = TopButtons[i] switch
            {
                TopButton.Stick => Config.TouchStick,
                TopButton.Shoot => Config.TouchShootButton,
                _ => false,
            };
            DrawRectangleRec(r, on ? new Rgba(60, 110, 60, 220) : new Rgba(40, 40, 50, 220));
            Outline(r, new Rgba(200, 200, 220, 220), 2 * S);
            DrawButtonIcon(TopButtons[i], r, on);
        }

        // Hint line under the buttons.
        DrawLabel(font, "DRAG CONTROLS TO REPOSITION",
            new Rect(0, 66 * S, w, 24 * S), 14 * S);
    }

    /// <summary>Draws a top-button's glyph from controlls.png, centered in the button and aspect-preserved.</summary>
    private void DrawButtonIcon(TopButton b, Rect area, bool on)
    {
        Rect src = IconOf(b);
        float pad = 8 * S;
        float availW = area.Width - 2 * pad, availH = area.Height - 2 * pad;
        float aspect = src.Width / src.Height;
        float w = availW, h = w / aspect;
        if (h > availH) { h = availH; w = h * aspect; }
        byte alpha = (byte)(on || b is TopButton.Reset or TopButton.Cancel or TopButton.Done ? 255 : 150);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["controlls.png"], src,
            new Rect(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h),
            Vector2.Zero, 0, Rgba.White with { A = alpha });
    }

    private static void DrawLabel(FontHandle font, string text, Rect area, float size)
    {
        Vector2 m = MeasureTextEx(font, text, size, 1);
        DrawTextEx(font, text,
            new Vector2(area.X + (area.Width - m.X) / 2, area.Y + (area.Height - m.Y) / 2), size, 1, Rgba.White);
    }

    private static void Outline(Rect r, Rgba color, float t)
    {
        DrawRectangleRec(new Rect(r.X, r.Y, r.Width, t), color);
        DrawRectangleRec(new Rect(r.X, r.Y + r.Height - t, r.Width, t), color);
        DrawRectangleRec(new Rect(r.X, r.Y, t, r.Height), color);
        DrawRectangleRec(new Rect(r.X + r.Width - t, r.Y, t, r.Height), color);
    }
}

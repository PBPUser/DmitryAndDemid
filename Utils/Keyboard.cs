using DmitryAndDemid.Rendering;
using System.Numerics;
using Gtk;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Utils;

public static class Keyboard
{
    private const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ.,:;/@abcdefghijklmnopqrstuvwxyz+-/*=%0123456789()()[]<>#!?'\"$";
    private static TargetHandle Texture;
    private static bool IsOnNazad = false;
    private static int X = 0, Y = 0;
    private static Action<char?>? Callback = null;
    private const double InputCooldown = 0.2f;
    public static double LastInputTimestamp = 0;
    public static double LastSwitchTimestamp = 0;
    public static double LastClickTimestamp = 0;
    static Vector2 PreviousCursosorPosition = new Vector2(0, 0);
    private static float PreviousCursosorRotation = 0f;
    private static float TargetCursosorRotation = 0f;
    public static int LineWidth;
    private static int LineHeight;
    private static int LetterWidth;
    private static int Spacing;
    private static Rgba Selection = Rgba.Purple with { A = 128 };
    private static TextureHandle Cursosor;
    private static Rect CursosorSource;
    private static Rect CursosorTarget;
    

    static Keyboard()
    {
        Initialize();
    }

    public static int KeyboardHeight;

    public static void Initialize()
    {
        Cursosor = Runtime.CurrentRuntime.Textures["cursosor.png"];
        CursosorSource = Helper.GetFullSource(Cursosor);
        CursosorTarget = new Rect(CursosorSource.Size / 20 * Runtime.CurrentRuntime.ScaleF, CursosorSource.Size / 20 * Runtime.CurrentRuntime.ScaleF);
        var font = Runtime.CurrentRuntime.Fonts["kodemono"];
        int fontSize = (int)(24 * Runtime.CurrentRuntime.ScaleF);
        int spacing = (int)(8 * Runtime.CurrentRuntime.ScaleF);
        var measureLetter = MeasureTextEx(font, "a", fontSize, spacing);
        Texture = Helper.DrawText(chars, fontSize, 0, 0,
            spacing, font, "gradient", Runtime.CurrentRuntime.ScaleF);
        LineWidth = (int)(16 * measureLetter.X + spacing * 15);
        LineHeight = (int)(measureLetter.Y);
        LetterWidth = (int)(measureLetter.X);
        Spacing = spacing;
        KeyboardHeight = LineHeight * 7;
    }
    
    public static void DrawKeyboard(int x, int y)
    {
        float state = (float)Helper.ComputeObjectTimeStart(GetTime(), LastSwitchTimestamp, InputCooldown);
        float state2 = (float)Helper.ComputeObjectTimeStart(GetTime(), LastClickTimestamp, InputCooldown);
        for(int i = 0; i < 6; i++)
            DrawTexturePro(Texture.Texture,
                new Rect(i*(LineWidth+Spacing),0, LineWidth, LineHeight),
                new Rect(x, y+i*LineHeight, LineWidth, LineHeight),
                Vector2.Zero, 0, Rgba.White);
        var s = CursosorTarget.Size * state2 + (1 - state2) * (0.5f * CursosorTarget.Size);
        DrawTexturePro(
            Cursosor, CursosorSource,
            CursosorTarget with
            {
                Position = (new Vector2(x + X * (LetterWidth+Spacing), y + Y * LineHeight) * state +
                           (new Vector2(x,y) + PreviousCursosorPosition * new Vector2((LetterWidth+Spacing), LineHeight)) * (1 - state)) + new Vector2(LetterWidth + Spacing, LineHeight) / 2,
                Size = s
            },
            s,
            state * TargetCursosorRotation + (1-state) * PreviousCursosorRotation
            , Rgba.White);
        
    }
    
    public static void Reset()
    {
        X = 0;
        Y = 0;
        LastInputTimestamp = LastClickTimestamp = LastSwitchTimestamp = GetTime();
        Callback = null;
        PreviousCursosorRotation = 0f;
        PreviousCursosorPosition = new Vector2(0, 0);
    }

    public static void HandleInput()
    {
        if (GetTime() - LastInputTimestamp < InputCooldown)
            return;
        if (IsKeyDown(KeyCode.Left))
        {
            PreviousCursosorRotation = TargetCursosorRotation;
            PreviousCursosorPosition = new Vector2(X, Y);
            TargetCursosorRotation = GetRandomValue(0, 360);
            X -= 1;
            if (X < 0)
                X = 15;
            if (Y == 5 && X < 14)
                X = 7;
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            LastInputTimestamp = GetTime();
            LastSwitchTimestamp = GetTime();
        }

        if (IsKeyDown(KeyCode.Escape))
        {
            if (X == 14 && Y == 5)
            {
                Callback?.Invoke(null);
                Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
            }
            else
            {
                PreviousCursosorRotation = TargetCursosorRotation;
                PreviousCursosorPosition = new Vector2(X, Y);
                Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
                TargetCursosorRotation = GetRandomValue(0, 360);
                LastSwitchTimestamp = GetTime();
                LastInputTimestamp = GetTime();
                X = 14;
                Y = 5;
            }
        }
        if (IsKeyDown(KeyCode.Right))
        {
            PreviousCursosorRotation = TargetCursosorRotation;
            PreviousCursosorPosition = new Vector2(X, Y);
            TargetCursosorRotation = GetRandomValue(0, 360);
            X += 1;
            if (Y == 5 && X > 7 && X < 14)
                X = 14;
            if (X > 15)
                X = 0;
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            LastInputTimestamp = GetTime();
            LastSwitchTimestamp = GetTime();
        }
        if (IsKeyDown(KeyCode.Up))
        {
            PreviousCursosorRotation = TargetCursosorRotation;
            PreviousCursosorPosition = new Vector2(X, Y);
            TargetCursosorRotation = GetRandomValue(0, 360);
            Y -= 1;
            if (Y == -1)
            {
                Y = 5;
                if (X > 7 && X < 14)
                    X = 14;
            }
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            LastInputTimestamp = GetTime();
            LastSwitchTimestamp = GetTime();
        }
        if (IsKeyDown(KeyCode.Down))
        {
            PreviousCursosorRotation = TargetCursosorRotation;
            PreviousCursosorPosition = new Vector2(X, Y);
            TargetCursosorRotation = GetRandomValue(0, 360);
            Y += 1;
            if (Y == 5 && X > 7 && X < 14)
            {
                X = 14;
            }
            if (Y == 6)
            {
                Y = 0;
            }
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            LastInputTimestamp = GetTime();
            LastSwitchTimestamp = GetTime();
        }

        if (IsKeyDown(KeyCode.Z) || IsKeyDown(KeyCode.Enter))
        {
            if (Y == 5)
            {
                if (X == 14)
                {
                    Callback?.Invoke(null);
                    Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
                    return;
                }
                if (X == 15)
                {
                    Callback?.Invoke('\n');
                    Helper.PlaySound(Runtime.CurrentRuntime.Sounds["swap"]);
                    return;
                }
            }
            Callback?.Invoke(chars[X+Y*16]);
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["button"]);
            LastInputTimestamp = GetTime();
            LastClickTimestamp = GetTime();
        }
    }

    public static void SetKeyboardCallback(Action<char?> callback)
    {
        Callback = callback;
    }
}
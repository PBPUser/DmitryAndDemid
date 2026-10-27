using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

/// <summary>
/// Shown after the staff roll on a cleared main-game run. The player enters a nickname, which is recorded onto
/// their score in the per-character board (<see cref="PlayerData.RecordGame"/>). If the run used no continues it
/// then hands off to the replay-save screen; otherwise it drops back to the main menu.
/// </summary>
public class ResultsScreen : Screen
{
    private readonly GameplayScreen Run;
    private GameBox Box => Run.GameBox;
    private readonly FontHandle Font;
    private readonly FontHandle TitleFont;
    private readonly float FontSize;
    private readonly Action? OnDone;
    private readonly bool Won;
    private string Current = "";
    private int LetterIndex;
    private bool Recorded;

    /// <summary>
    /// <paramref name="onDone"/>, if given, replaces the plain "reveal whatever's underneath" ending: it fires
    /// once the whole results/replay-save cascade is done (used by Extra mode to land directly back on the main
    /// menu instead of the DifficultyScreen/PersonSelectScreen chain still sitting beneath the run).
    /// <paramref name="won"/> is recorded onto the score board (<see cref="PlayerData.RecordGame"/>'s win-count
    /// stat) — false for an unrecoverable death (Extra's game-over cascade), true for an actual clear.
    /// </summary>
    public ResultsScreen(GameplayScreen run, Action? onDone = null, bool won = true)
    {
        Run = run;
        OnDone = onDone;
        Won = won;
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        Font = Runtime.CurrentRuntime.Fonts["googlesans"];
        TitleFont = Runtime.CurrentRuntime.Fonts["kodemono"];
        FontSize = 20 * Runtime.CurrentRuntime.ScaleF;
    }

    protected override void Created()
    {
        base.Created();
        AppearTime = GetTime();
        Keyboard.Reset();
        Keyboard.SetKeyboardCallback(OnKey);
    }

    // --- Entrance -------------------------------------------------------------------------------------------
    // The whole readout used to appear at once, fully formed, which made a cleared run land with no ceremony at
    // all. Now each line rises into place in turn and the score counts itself up.

    private double AppearTime;
    /// <summary>Delay between one line starting to arrive and the next.</summary>
    private const double LineStagger = 0.14;
    private const double LineLength = 0.45;
    /// <summary>How long the score spends counting up, measured from when its own line has arrived.</summary>
    private const double ScoreRollLength = 1.4;

    /// <summary>Arrival progress of the <paramref name="index"/>th line from the top, 0 → 1.</summary>
    private float LineAppear(int index) =>
        (float)Helper.ComputeObjectTimeStart(GetTime(), AppearTime + index * LineStagger, LineLength);

    /// <summary>
    /// The score climbing from zero to the real figure, so the number the player earned is spent rather than
    /// simply displayed. Eased out (fast, then decelerating) — a linear count reads as a progress bar.
    /// </summary>
    private int RollingScore()
    {
        float progress = (float)Helper.ComputeObjectTimeStart(GetTime(), AppearTime + LineStagger + LineLength, ScoreRollLength);
        float eased = 1f - (1f - progress) * (1f - progress) * (1f - progress);
        return (int)MathF.Round(Box.FinalScore * eased);
    }

    private void OnKey(char? a)
    {
        // Enter confirms; cancel keeps whatever has been typed. Either way the run is recorded and we move on,
        // so a clear is never silently dropped.
        if (a == '\n' || a == null)
        {
            Record();
            Next();
            return;
        }
        if (LetterIndex < 8)
        {
            Current = Current.Substring(0, LetterIndex) + a;
            LetterIndex++;
        }
    }

    private void Record()
    {
        if (Recorded)
            return;
        Recorded = true;
        string name = string.IsNullOrWhiteSpace(Current) ? "--------" : Current;
        long seconds = (long)(Box.CurrentTick / GameBox.TargetTPS);
        PlayerData.Instance.LastName = name;
        PlayerData.Instance.RecordGame(Box.ProtogonistId, Box.Difficulty, Box.FinalScore,
            won: Won, seconds, stage: 0, percentage: 0f, name);
    }

    private void Next()
    {
        Runtime.CurrentRuntime.RemoveScreen(this);
        // A clean, no-continue clear earns the chance to save a replay; otherwise straight back to the menu.
        if (Box.ContinuesUsed == 0 && Box.Player.Controller is PlayerController controller)
            Runtime.CurrentRuntime.AddScreen(new IngameSaveReplayScreen(controller, Run, OnDone));
        else
            OnDone?.Invoke();
    }

    public override void TopUpdate()
    {
        Keyboard.HandleInput();
        base.TopUpdate();
    }

    public override void Render()
    {
        DrawBackground();
        float sf = Runtime.CurrentRuntime.ScaleF;
        float time = (float)GetTime();

        // The heading keeps breathing once it has landed — a slow scale pulse, so the top of the screen is never
        // completely still while the player is thinking about their name.
        float headingPulse = 1f + MathF.Sin(time * 2.2f) * 0.035f * LineAppear(0);
        DrawCentered(TitleFont, "RESULT", 80 * sf, FontSize * 1.6f * headingPulse, Rgba.Yellow, LineAppear(0));
        DrawCentered(Font, $"SCORE  {RollingScore()}", 140 * sf, FontSize, Rgba.White, LineAppear(1));
        DrawCentered(Font, $"{Box.ProtogonistId}   {DifficultyName(Box.Difficulty)}", 170 * sf, FontSize, Rgba.White, LineAppear(2));
        DrawCentered(Font, Box.ContinuesUsed == 0 ? "NO CONTINUE" : $"CONTINUES {Box.ContinuesUsed}", 196 * sf, FontSize, Rgba.White, LineAppear(3));
        DrawCentered(Font, "ENTER YOUR NAME", 240 * sf, FontSize, Rgba.White, LineAppear(4));

        string shown = string.IsNullOrEmpty(Current) ? "________" : Current.PadRight(8, '_');
        float nameSize = FontSize * 1.4f;
        float nameY = 270 * sf;
        float nameAppear = LineAppear(5);
        DrawCentered(TitleFont, shown, nameY, nameSize, Rgba.Yellow, nameAppear);
        DrawNameCaret(shown, nameY, nameSize, nameAppear, time);

        Keyboard.DrawKeyboard((Runtime.CurrentRuntime.Width - Keyboard.LineWidth) / 2,
            Runtime.CurrentRuntime.Height - Keyboard.KeyboardHeight);
    }

    /// <summary>
    /// A blinking bar under whichever of the eight slots is being typed into. Drawn as its own rectangle,
    /// positioned by measuring the text BEFORE the caret, rather than by swapping a character in the string:
    /// that keeps the name itself perfectly still (a swapped glyph would shift the centred line every blink on
    /// any font that is not monospace) and works whatever font the screen is set to.
    /// </summary>
    private void DrawNameCaret(string shown, float y, float size, float appear, float time)
    {
        if (appear <= 0f || LetterIndex >= shown.Length)
            return;
        float full = MeasureTextEx(TitleFont, shown, size, 1).X;
        float before = LetterIndex == 0 ? 0f : MeasureTextEx(TitleFont, shown.Substring(0, LetterIndex), size, 1).X;
        float slot = MeasureTextEx(TitleFont, shown.Substring(0, LetterIndex + 1), size, 1).X - before;
        float left = (Runtime.CurrentRuntime.Width - full) / 2f + before;
        float blink = 0.35f + 0.65f * MathF.Abs(MathF.Sin(time * 3.4f));
        float sf = Runtime.CurrentRuntime.ScaleF;
        DrawRectangle((int)left, (int)(y + size + 2 * sf + Rise(appear)), (int)slot, (int)MathF.Max(2 * sf, 2),
            Rgba.Yellow with { A = (byte)(255 * blink * appear) });
    }

    private void DrawCentered(FontHandle font, string text, float y, float size, Rgba color)
    {
        float w = MeasureTextEx(font, text, size, 1).X;
        DrawTextEx(font, text, new Vector2((Runtime.CurrentRuntime.Width - w) / 2f, y), size, 1, color);
    }

    /// <summary>A line still on its way in is drawn low and see-through, rising and solidifying into place.</summary>
    private void DrawCentered(FontHandle font, string text, float y, float size, Rgba color, float appear)
    {
        if (appear <= 0f)
            return;
        DrawCentered(font, text, y + Rise(appear), size, color with { A = (byte)(color.A * appear) });
    }

    /// <summary>How far below its resting place a line at <paramref name="appear"/> progress still sits.</summary>
    private static float Rise(float appear) =>
        (1f - Helper.Pow2F(appear)) * 24f * Runtime.CurrentRuntime.ScaleF;

    private static string DifficultyName(int d) =>
        d >= 0 && d < Helper.DifficultyIds.Length ? Helper.DifficultyIds[d] : d.ToString();
}

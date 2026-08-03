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
        Keyboard.Reset();
        Keyboard.SetKeyboardCallback(OnKey);
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

        DrawCentered(TitleFont, "RESULT", 80 * sf, FontSize * 1.6f, Rgba.Yellow);
        DrawCentered(Font, $"SCORE  {Box.FinalScore}", 140 * sf, FontSize, Rgba.White);
        DrawCentered(Font, $"{Box.ProtogonistId}   {DifficultyName(Box.Difficulty)}", 170 * sf, FontSize, Rgba.White);
        DrawCentered(Font, Box.ContinuesUsed == 0 ? "NO CONTINUE" : $"CONTINUES {Box.ContinuesUsed}", 196 * sf, FontSize, Rgba.White);
        DrawCentered(Font, "ENTER YOUR NAME", 240 * sf, FontSize, Rgba.White);
        string shown = string.IsNullOrEmpty(Current) ? "________" : Current.PadRight(8, '_');
        DrawCentered(TitleFont, shown, 270 * sf, FontSize * 1.4f, Rgba.Yellow);

        Keyboard.DrawKeyboard((Runtime.CurrentRuntime.Width - Keyboard.LineWidth) / 2,
            Runtime.CurrentRuntime.Height - Keyboard.KeyboardHeight);
    }

    private void DrawCentered(FontHandle font, string text, float y, float size, Rgba color)
    {
        float w = MeasureTextEx(font, text, size, 1).X;
        DrawTextEx(font, text, new Vector2((Runtime.CurrentRuntime.Width - w) / 2f, y), size, 1, color);
    }

    private static string DifficultyName(int d) =>
        d >= 0 && d < Helper.DifficultyIds.Length ? Helper.DifficultyIds[d] : d.ToString();
}

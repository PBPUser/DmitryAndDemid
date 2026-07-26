using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Utils;
#if DEBUG
using ImGuiNET;
#endif
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

public class PauseMenu : MenuScreen
{
    private string ContinueString = Helper.Translate("ingame.credit");
    private GameplayScreen GameplayScreen;
    
    public PauseMenu(GameplayScreen screen)
    {
        GameplayScreen = screen;
        CurrentY = (int)(192 * Runtime.CurrentRuntime.ScaleF);

        // In-game pause keeps the old reflex: Escape closes it outright instead of walking the cursor down to
        // "exit", which here means abandoning the run.
        EscapeFocusesExitItem = false;
    }

    // The two replay-save entries, kept so they can be disabled once a continue has been spent (a continued run
    // is no longer a legitimate replay).
    private MenuItem? SaveItem = null!;
    private MenuItem? SaveExitItem = null!;
    private MenuItem? ContinueItem = null!;

    public override void CreateMenu()
    {
        MenuItems.Add(ContinueItem = new MenuItem("ingame.continue", "", a =>
        {
            GameBox box = GameplayScreen.GameBox!;
            if (box.IsGameOver)
            {
                // Main game: spend one of the five continues to revive and carry on. Once they are used up the
                // entry does nothing (the player has to exit). Spell / full practice can't be resumed, so there
                // "continue" instead means retry the card from the top.
                if (box.CanContinue)
                    box.UseContinue();   // lifts game-over → unpauses → this menu is removed by Paused=false
                else if (box.IsPractice)
                    RestartRun();
                return;
            }
            BeginClose();
        }) { Hint = "ingame.continue.hint" });
        if (!GameplayScreen.GameBox.IsReplay)
        {
            MenuItems.Add(SaveItem = new MenuItem("ingame.save", "", a =>
            {
                if (GameplayScreen.GameBox!.ContinuesUsed > 0)
                    return;   // disabled after a continue
                IngameSaveReplayScreen replayScreen = new IngameSaveReplayScreen((GameplayScreen.GameBox!.Player.Controller as PlayerController)!, GameplayScreen);
                ReturningFromSubscreen = true;
                Runtime.CurrentRuntime.AddScreen(replayScreen);
            }) { Hint = "ingame.save.hint" });
            MenuItems.Add(SaveExitItem = new MenuItem("ingame.save_and_exit", "", a =>
            {
                if (GameplayScreen.GameBox!.ContinuesUsed > 0)
                    return;   // disabled after a continue
                IngameSaveReplayScreen replayScreen = new IngameSaveReplayScreen((GameplayScreen.GameBox!.Player.Controller as PlayerController)!, GameplayScreen);
                ReturningFromSubscreen = true;
                Runtime.CurrentRuntime.AddScreen(replayScreen);
                replayScreen.ExitAfterSave = true;
            }) { Hint = "ingame.save_and_exit.hint" });
        }
        // Restart replays the same run/card. In spell practice the "continue" entry already does this (a card
        // can't be resumed), so the practice exit menu drops the separate restart to match its intended options:
        // continue, save, save-and-exit, settings, manual, exit.
        if (!GameplayScreen.GameBox!.IsPractice)
            MenuItems.Add(new MenuItem("ingame.restart", "", a => RestartRun()) { Hint = "ingame.restart.hint" });
        MenuItems.Add(new MenuItem("ingame.manual", "",
            a => { ReturningFromSubscreen = true; Runtime.CurrentRuntime.AddScreen(new ManualScreen()); }) { Hint = "ingame.manual.hint" });
        MenuItems.Add(new MenuItem("ingame.settings", "",
            a => { ReturningFromSubscreen = true; Runtime.CurrentRuntime.AddScreen(new SettingsScreen()); }) { Hint = "ingame.settings.hint" });
        MenuItems.Add(new MenuItem("ingame.exit", "", a =>
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
            Runtime.CurrentRuntime.RemoveScreen(GameplayScreen);
        }) { Hint = "ingame.exit.hint" });
    }

    /// <summary>
    /// Escape on a live pause closes this menu via the base Exit(); make sure the run actually un-pauses so the
    /// green pause "blur" shader clears, instead of leaving the game frozen behind a removed menu. On game-over
    /// Escape walks to the exit item (its own action) rather than here, so this only fires for a genuine resume.
    /// </summary>
    public override void Exiting()
    {
        if (!GameplayScreen.GameBox!.IsGameOver)
            GameplayScreen.Resume();
        base.Exiting();
        // Play the close animation instead of vanishing instantly. The base Exit() defers the actual removal by
        // MenuActivateCooldown (0.5s) via ItemActivated, but our own Closing branch in TopUpdate takes over and
        // removes the screen once the ~0.28s slide-out has finished.
        BeginClose();
    }

    /// <summary>Set once the menu has begun its slide-out (Escape / resume). While true no further input is taken
    /// and the screen removes itself when the disappear animation has run its course.</summary>
    private bool Closing;
    private const float CloseDuration = 0.28f;

    /// <summary>Start the close animation: resume the run (so the green pause blur clears and gameplay carries on
    /// under the sliding menu) and drive <see cref="TimeDisappear"/> so the fork / pause board / menu slide out.
    /// The screen is actually removed from TopUpdate once <see cref="CloseDuration"/> has elapsed.</summary>
    private void BeginClose()
    {
        if (Closing)
            return;
        Closing = true;
        TimeDisappear = (float)GetTime();
        if (!GameplayScreen.GameBox!.IsGameOver)
            GameplayScreen.Resume();
    }

    /// <summary>Replays the current run/card from its starting chapter, behind a black loading screen.</summary>
    private void RestartRun()
    {
        var screen = GameplayScreen.CreateCopy();
        Runtime.CurrentRuntime.AddScreen(screen);
        // The loader MUST remove itself once done: with an empty callback it stayed on the stack as the
        // (now-transparent) topmost screen forever, and since only Screens.Last() receives TopUpdate, the
        // restarted run never saw Escape again — pause was dead after the first restart from this menu.
        BlackLoadingScreen? loader = null;
        loader = new BlackLoadingScreen(3, 0.2, () => Runtime.CurrentRuntime.RemoveScreen(loader!), true, 1);
        Runtime.CurrentRuntime.AddScreen(loader);
        // Tear down the old run on the next safe point. RefreshScreens applies the removals at the top of the
        // frame, so doing it inline (rather than the old un-awaited Task.Delay, which fired immediately anyway)
        // is both correct and simpler; the black fade covers the swap.
        Runtime.CurrentRuntime.AddAction(() =>
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
            Runtime.CurrentRuntime.RemoveScreen(GameplayScreen);
            GameplayScreen.Unload();
            Unload();
        });
    }

    /// <summary>Set when this menu opens a sub-screen (save-replay / manual / settings), so its re-appearance
    /// after that sub-screen closes doesn't replay the open animation and pause sting.</summary>
    private bool ReturningFromSubscreen;

    public override void Activated()
    {
        Closing = false;
        TimeDisappear = float.MaxValue;
        // Play the appear animation + pause sting only on a genuine open (a fresh pause), not when the menu is
        // merely revealed again after a sub-screen it opened has closed.
        if (ReturningFromSubscreen)
            ReturningFromSubscreen = false;
        else
        {
            TimeAppear = (float)GetTime();
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["pause"]);
        }
        base.Activated();
    }

    public override void TopUpdate()
    {
        // Mid slide-out: take no more input, just wait for the animation to finish and then remove ourselves.
        if (Closing)
        {
            if (GetTime() - TimeDisappear >= CloseDuration)
                Runtime.CurrentRuntime.RemoveScreen(this);
            return;
        }

        GameBox box = GameplayScreen.GameBox!;
        // On game-over, Escape must walk to (and then commit on) the exit item rather than silently closing the
        // overlay: closing it would strand a frozen, un-resumable run behind no menu — the spell-card softlock.
        // During a live pause it keeps the old reflex of closing immediately.
        EscapeFocusesExitItem = box.IsGameOver;
        // A continue spends the run's replay legitimacy, so the two save entries switch off once one is used.
        if(!GameplayScreen.GameBox.IsReplay)
            SaveItem.Enabled = SaveExitItem.Enabled = box.ContinuesUsed == 0;
        // "Continue" is dead on a main-game game-over with no continues left (and no card to retry); dim it there
        // so the cursor skips past it to the still-usable options.
        ContinueItem.Enabled = !box.IsGameOver || box.CanContinue || box.IsPractice;
        // Its hint follows suit: only the game-over/CanContinue branch actually spends a rassrochka (UseContinue);
        // a live pause just resumes and a practice retry restarts the card for free, so both read as the free variant.
        ContinueItem.Hint = box.IsGameOver && box.CanContinue ? "ingame.continue.hint" : "ingame.continue.hint_free";
        // Quick keys, no confirmation: R restarts the run/card immediately, Q quits to the menu below. Each
        // tears the pause menu down (directly or via RestartRun), so it can't re-fire on a held key.
        if (IsKeyDown(KeyCode.R))
        {
            RestartRun();
            return;
        }
        if (IsKeyDown(KeyCode.Q))
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
            Runtime.CurrentRuntime.RemoveScreen(GameplayScreen);
            return;
        }
        base.TopUpdate();
    }

    public override void Render()
    {
        float time = (float)GetTime();
        var z = (float)Helper.ComputeObjectTime(time, TimeAppear, 0.25, TimeDisappear, 0.25);
        var textureFork = Runtime.CurrentRuntime.Textures["vilkaCut.png"];
        var texturePause = Runtime.CurrentRuntime.Textures["pause.png"];
        var forkPosition = new Vector2(100, 320) * Runtime.CurrentRuntime.ScaleF;
        var pausePosition = new Vector2(130, 160) * Runtime.CurrentRuntime.ScaleF;
        var forkPositionHidden = new Vector2(-100, 320) * Runtime.CurrentRuntime.ScaleF;
        var pausePositionHidden = new Vector2(-100, 100) * Runtime.CurrentRuntime.ScaleF;
        var forkSize = Helper.GetSize(textureFork);
        var pauseSize = Helper.GetSize(texturePause);
        var forkSizeTarget = forkSize / 4 * Runtime.CurrentRuntime.ScaleF;
        var pauseSizeTarget = pauseSize / 4 * Runtime.CurrentRuntime.ScaleF;
        SetShaderValue(Runtime.CurrentRuntime.Shaders["fork_tint"], GetShaderLocation(Runtime.CurrentRuntime.Shaders["fork_tint"], "color"), new Vector3(0,1,.2f), UniformType.Vec3);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["fork_tint"]);
        DrawTexturePro(textureFork,
            new Rect(0, 0, forkSize),
            new Rect(forkPosition * z + forkPositionHidden*(1-z), forkSizeTarget),
            forkSizeTarget / 2, MathF.Sin(time * 3) * 6, Rgba.White);
        EndShaderMode();
        DrawTexturePro(texturePause,
            new Rect(0, 0, pauseSize),
            new Rect(pausePosition * MathF.Pow(z,2) + pausePositionHidden*(1-MathF.Pow(z,2)), pauseSizeTarget),
            pauseSizeTarget / 2, 0, Rgba.White);
        CurrentX = (int)((160f - 64f * z) * Runtime.CurrentRuntime.ScaleF);

        // On a main-game game-over, show how many continues are still on the table. Translated (translation.json
        // "ingame.credit") rather than a hardcoded English word, and pinned centre-bottom of the screen.
        GameBox box = GameplayScreen.GameBox!;
        if (box.IsGameOver)
        {
            var font = Runtime.CurrentRuntime.Fonts["newsreader"];
            float sf = Runtime.CurrentRuntime.ScaleF;
            float fs = 18 * sf;
            string txt = ContinueString.Replace("%s", $"{box.ContinuesRemaining}");
            Vector2 m = MeasureTextEx(font, txt, fs, 2);
            var pos = new Vector2(((224 * sf) - m.X / 2),
                Runtime.CurrentRuntime.Height - m.Y - 16 * sf);
            DrawTextEx(font, txt, pos, fs, 2,
                Rgba.White);
        }
        DrawMenu();
    }
    
#if DEBUG
    public override void DrawImgui()
    {
        ImGui.Begin("Debug Strings");
        foreach (var s in GameplayScreen.GameBox.DebugStrings)
        {
            ImGui.Text(s);
        }
        ImGui.End();
        GameplayScreen.GameBox.DebugStrings.Clear();
    }
#endif
}
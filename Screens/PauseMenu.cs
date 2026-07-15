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
    private GameplayScreen GameplayScreen;
    
    public PauseMenu(GameplayScreen screen)
    {
        GameplayScreen = screen;
        CurrentY = (int)(256 * Runtime.CurrentRuntime.ScaleF);

        // In-game pause keeps the old reflex: Escape closes it outright instead of walking the cursor down to
        // "exit", which here means abandoning the run.
        EscapeFocusesExitItem = false;
    }

    // The two replay-save entries, kept so they can be disabled once a continue has been spent (a continued run
    // is no longer a legitimate replay).
    private MenuItem SaveItem = null!;
    private MenuItem SaveExitItem = null!;
    private MenuItem ContinueItem = null!;

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
            GameplayScreen.Resume();
            Runtime.CurrentRuntime.RemoveScreen(this);
        }));
        MenuItems.Add(SaveItem = new MenuItem("ingame.save", "", a =>
        {
            if (GameplayScreen.GameBox!.ContinuesUsed > 0)
                return;   // disabled after a continue
            IngameSaveReplayScreen replayScreen = new IngameSaveReplayScreen((GameplayScreen.GameBox!.Player.Controller as PlayerController)!, GameplayScreen);
            Runtime.CurrentRuntime.AddScreen(replayScreen);
        }));
        MenuItems.Add(SaveExitItem = new MenuItem("ingame.save_and_exit", "", a =>
        {
            if (GameplayScreen.GameBox!.ContinuesUsed > 0)
                return;   // disabled after a continue
            IngameSaveReplayScreen replayScreen = new IngameSaveReplayScreen((GameplayScreen.GameBox!.Player.Controller as PlayerController)!, GameplayScreen);
            Runtime.CurrentRuntime.AddScreen(replayScreen);
            replayScreen.ExitAfterSave = true;
        }));
        // Restart replays the same run/card. In spell practice the "continue" entry already does this (a card
        // can't be resumed), so the practice exit menu drops the separate restart to match its intended options:
        // continue, save, save-and-exit, settings, manual, exit.
        if (!GameplayScreen.GameBox!.IsPractice)
            MenuItems.Add(new MenuItem("ingame.restart", "", a => RestartRun()));
        MenuItems.Add(new MenuItem("ingame.manual", "",
            a => Runtime.CurrentRuntime.AddScreen(new ManualScreen())));
        MenuItems.Add(new MenuItem("ingame.settings", "",
            a => Runtime.CurrentRuntime.AddScreen(new SettingsScreen())));
        MenuItems.Add(new MenuItem("ingame.exit", "", a =>
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
            Runtime.CurrentRuntime.RemoveScreen(GameplayScreen);
        }));
    }

    /// <summary>Replays the current run/card from its starting chapter, behind a black loading screen.</summary>
    private void RestartRun()
    {
        var screen = GameplayScreen.CreateCopy();
        Runtime.CurrentRuntime.AddScreen(screen);
        Runtime.CurrentRuntime.AddScreen(new BlackLoadingScreen(3, 0.2, () => {}, true, 1));
        Task.Run(() =>
        {
            Task.Delay(2000);
            Runtime.CurrentRuntime.AddAction(() =>
            {
                Runtime.CurrentRuntime.RemoveScreen(this);
                Runtime.CurrentRuntime.RemoveScreen(GameplayScreen);
                GameplayScreen.Unload();
                Unload();
            });
        });
    }

    public override void Activated()
    {
        TimeAppear = (float)GetTime();
        TimeDisappear = float.MaxValue;
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["pause"]);
        base.Activated();
    }

    public override void TopUpdate()
    {
        GameBox box = GameplayScreen.GameBox!;
        // On game-over, Escape must walk to (and then commit on) the exit item rather than silently closing the
        // overlay: closing it would strand a frozen, un-resumable run behind no menu — the spell-card softlock.
        // During a live pause it keeps the old reflex of closing immediately.
        EscapeFocusesExitItem = box.IsGameOver;
        // A continue spends the run's replay legitimacy, so the two save entries switch off once one is used.
        SaveItem.Enabled = SaveExitItem.Enabled = box.ContinuesUsed == 0;
        // "Continue" is dead on a main-game game-over with no continues left (and no card to retry); dim it there
        // so the cursor skips past it to the still-usable options.
        ContinueItem.Enabled = !box.IsGameOver || box.CanContinue || box.IsPractice;
        base.TopUpdate();
    }

    public override void Render()
    {
        float time = (float)GetTime();
        var z = (float)Helper.ComputeObjectTime(time, TimeAppear, 0.25, TimeDisappear, 0.25);
        var textureFork = Runtime.CurrentRuntime.Textures["vilkaCut.png"];
        var texturePause = Runtime.CurrentRuntime.Textures["pause.png"];
        var forkPosition = new Vector2(100, 320) * Runtime.CurrentRuntime.ScaleF;
        var pausePosition = new Vector2(130, 220) * Runtime.CurrentRuntime.ScaleF;
        var forkPositionHidden = new Vector2(-100, 320) * Runtime.CurrentRuntime.ScaleF;
        var pausePositionHidden = new Vector2(-100, 160) * Runtime.CurrentRuntime.ScaleF;
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

        // On a main-game game-over, show how many continues are still on the table above the menu.
        GameBox box = GameplayScreen.GameBox!;
        if (box.IsGameOver && box.CanContinue)
        {
            var font = Runtime.CurrentRuntime.Fonts["newsreader"];
            float fs = 18 * Runtime.CurrentRuntime.ScaleF;
            string txt = $"CONTINUE  {box.ContinuesRemaining}/{GameBox.MaxContinues}";
            DrawTextEx(font, txt, new Vector2(CurrentX, CurrentY - 44 * Runtime.CurrentRuntime.ScaleF), fs, 2,
                Helper.Mix(Rgba.Yellow, Rgba.White, MathF.Abs(time % 1f - 0.5f) * 2f));
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
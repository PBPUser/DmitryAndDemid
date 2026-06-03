using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Utils;
using ImGuiNET;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Screens;

public class PauseMenu : MenuScreen
{
    private GameplayScreen GameplayScreen;
    
    public PauseMenu(GameplayScreen screen)
    {
        GameplayScreen = screen;
        CurrentY = (int)(256 * Runtime.CurrentRuntime.ScaleF);
    }

    public override void CreateMenu()
    {
        MenuItems.Add(new MenuItem("ingame.continue", "", a =>
        {
            if (GameplayScreen.GameBox!.IsGameOver)
                return;
            GameplayScreen.Resume();
            Runtime.CurrentRuntime.RemoveScreen(this);
        }));
        MenuItems.Add(new MenuItem("ingame.save", "", a =>
        {
            IngameSaveReplayScreen replayScreen = new IngameSaveReplayScreen((GameplayScreen.GameBox!.Player.Controller as PlayerController)!, GameplayScreen);
            Runtime.CurrentRuntime.AddScreen(replayScreen);
        }));
        MenuItems.Add(new MenuItem("ingame.save_and_exit", "", a =>
        {
            IngameSaveReplayScreen replayScreen = new IngameSaveReplayScreen((GameplayScreen.GameBox!.Player.Controller as PlayerController)!, GameplayScreen);
            Runtime.CurrentRuntime.AddScreen(replayScreen);
            replayScreen.ExitAfterSave = true;
        }));
        MenuItems.Add(new MenuItem("ingame.restart", "", a =>
        {
            var screen = GameplayScreen.CreateCopy();
            var pause = new PauseMenu(screen);
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
        }));
        MenuItems.Add(new MenuItem("ingame.exit", "", a =>
        {
            Runtime.CurrentRuntime.RemoveScreen(this); 
            Runtime.CurrentRuntime.RemoveScreen(GameplayScreen);
        }));
    }

    public override void Activated()
    {
        TimeAppear = (float)GetTime();
        TimeDisappear = float.MaxValue;
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["pause"]);
        base.Activated();
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
        SetShaderValue(Runtime.CurrentRuntime.Shaders["fork_tint"], GetShaderLocation(Runtime.CurrentRuntime.Shaders["fork_tint"], "color"), new Vector3(0,1,.2f), ShaderUniformDataType.Vec3);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["fork_tint"]);
        DrawTexturePro(textureFork,
            new Rectangle(0, 0, forkSize),
            new Rectangle(forkPosition * z + forkPositionHidden*(1-z), forkSizeTarget),
            forkSizeTarget / 2, MathF.Sin(time * 3) * 6, Color.White);
        EndShaderMode();
        DrawTexturePro(texturePause,
            new Rectangle(0, 0, pauseSize),
            new Rectangle(pausePosition * MathF.Pow(z,2) + pausePositionHidden*(1-MathF.Pow(z,2)), pauseSizeTarget),
            pauseSizeTarget / 2, 0, Color.White);
        CurrentX = (int)((160f - 64f * z) * Runtime.CurrentRuntime.ScaleF);
        
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
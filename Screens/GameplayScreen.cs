using DmitryAndDemid.Rendering;
using DmitryAndDemid.Common;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid;
using DmitryAndDemid.Data;
using System.Numerics;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;
#if DEBUG
using ImGuiNET;
#endif
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DmitryAndDemid.Screens;

public class GameplayScreen : Screen
{
    public GameplayScreen(ProtogonistData data, int difficulty, FileStageInfo[] stages, int chapter, bool practice)
    {
        SetBackground(Runtime.CurrentRuntime.Textures["gameplay_background.png"]);
        Data = data;
        Difficulty = difficulty;
        Stages = stages;
        Chapter = chapter;
        Practice = practice;
        Source = new Rect(0, 0, 384, -448);
        UIAboveSource = new Rect(0, 0, 384 * Runtime.CurrentRuntime.ScaleF, -448 * Runtime.CurrentRuntime.ScaleF);
        Dest = new Rect(32 * Runtime.CurrentRuntime.ScaleF, 16 * Runtime.CurrentRuntime.ScaleF, 384 * Runtime.CurrentRuntime.ScaleF, 448 * Runtime.CurrentRuntime.ScaleF);
        DialogDest = Helper.GetFullscreenSource();
        DialogSource = Helper.GetFullscreenSource();
        DialogSource.Height *= -1;
        DieShader = Runtime.CurrentRuntime.Shaders["die"];
        PauseMenu = new PauseMenu(this);
        SetShaderValue(
            DieShader, 
            GetShaderLocation(DieShader, "scale"),
            Runtime.CurrentRuntime.ScaleF,
            UniformType.Float
            );
        LocationDiePosition = GetShaderLocation(DieShader, "pos");
        LocationDieTime = GetShaderLocation(DieShader, "time");
        DifficultySource = new Rect(0, 160*difficulty, 1920, 160);
        DifficultyTargetStart = Helper.Scale(new Rect(152, 20, 144, 12), Runtime.CurrentRuntime.ScaleF);
        DifficultyTarget = Helper.Scale(new Rect(456, 24, 144, 12), Runtime.CurrentRuntime.ScaleF);
        LetterWidth = (int)(MeasureTextEx(Runtime.CurrentRuntime.Fonts["kodemono"],
            "j",
            (int)(24 * Runtime.CurrentRuntime.ScaleF),
            0).X+(int)(2 * Runtime.CurrentRuntime.ScaleF));
        GameEffectsTextures[0] = LoadRenderTexture(384, 448);
        GameEffectsTextures[1] = LoadRenderTexture(384, 448);
        GameEffectsTextures[2] = LoadRenderTexture(384, 448);
        GameEffectsTextures[3] = LoadRenderTexture(384, 448);
        GameBox = new GameBox(this, data, stages, chapter, difficulty, practice);
        PauseEffect = new GameplayScreenEffect(GameBox, new Vector2(), int.MaxValue, "pause", float.MaxValue, float.MaxValue)
        {
            UseSteps = true,
            StepLength = .25f,
            UseRealTime = true
        };
        UILeftSource  = Helper.GetFullSourceRenderTexture(GameBox.UILeft);
        LeftDest = new Rect(
            Runtime.CurrentRuntime.ScaleF * 416,
            0, UILeftSource.Size
        );
        
    }

    private ProtogonistData Data;
    private int Difficulty;
    private FileStageInfo[] Stages;
    private int Chapter;
    private bool Practice;

    public GameplayScreen CreateCopy() => new(Data, Difficulty, Stages, Chapter, Practice);
    public int LetterWidth = 0;
    public PauseMenu PauseMenu;
    
    Rect Source;
    Rect Dest;
    Rect DialogSource;
    Rect DialogDest;
    private Rect LeftDest;
    private Rect UILeftSource;
    private Rect UIAboveSource;

    private static Rect Fullscreen = Helper.GetFullscreenSource();
    private static Rect BGSource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["gameplay_background.png"]);
    private static Rect DestEffect = new Rect(0, 0, 384, 448); 

    private Rect DifficultySource;
    private Rect DifficultyTargetStart;
    private Rect DifficultyTarget;

    public GameplayScreenEffect PauseEffect;
    
    TargetHandle[] GameEffectsTextures = new TargetHandle[4];
    private int GameEffectTextureIndex = 1;
    
    public GameBox GameBox;
    
    public void Resume() => GameBox.IsPaused = false;

    private bool paused;
    
    public bool Paused
    {
        get => paused;
        set
        {
            if (paused == value)
                return;
            paused = value;
            GameBox.IsPaused = value;
            if (value)
            {
                Runtime.CurrentRuntime.AddScreen(PauseMenu);
                GameBox.ScreenEffects.Add(PauseEffect);
                PauseEffect.TimeAppear = (float)GetTime();
                PauseEffect.TimeDisappear = float.MaxValue;
            }
            else
            {
                Runtime.CurrentRuntime.RemoveScreen(PauseMenu);
                GameBox.RemoveScreenEffect(PauseEffect);
                PauseEffect.TimeDisappear = (float)(GetTime()+1);
            }
        }
    }
    
    public override void PreRender(double f)
    {
        GameBox.Update();
    }

    public override void TopUpdate()
    {
        float time = GameBox.GetTime();
        if (time < 0)
            return;
        GameBox.ProcessInput();
        if ((IsKeyDown(KeyCode.Escape) ||
             Controller.IsButtonDown(Configuration.Config.PauseButton)) 
            && !GameBox.IsGameOver && GetTime() - MenuScreen.PreviousKeyTimestamp > MenuScreen.MenuSwitchCooldown)
        {
            MenuScreen.PreviousKeyTimestamp = GetTime();
            Paused = !Paused;
        }
        base.TopUpdate();
    }

    private ShaderHandle DieShader;
    private int LocationDiePosition, LocationDieTime;
    
    public override void Render()
    {
        float time = GameBox.GetTime();
        if (time < -.5)
            return;
        GameBox.RenderBox();
        BeginTextureMode(GameEffectsTextures[0]);
        DrawTexturePro(GameBox.Background.Texture,
            Source, DestEffect,
            Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        GameEffectTextureIndex = 0;
        foreach (var gse in GameBox.ScreenEffects.Where(x => x.Layer == GameplayScreenEffect.EffectLayer.BackgroundOnly))
        {
            GameEffectTextureIndex = (GameEffectTextureIndex + 1) % 2;
            BeginTextureMode(GameEffectsTextures[GameEffectTextureIndex]);
            ClearBackground(Rgba.Black with {A = 0});
            gse.ApplyShading(time);
            DrawTexturePro(GameEffectsTextures[(GameEffectTextureIndex+1) % 2].Texture,
                Source, DestEffect, Vector2.Zero, 0, Rgba.White);
            EndShaderMode();
            EndTextureMode();
        }
        BeginTextureMode(GameEffectsTextures[2]);
        ClearBackground(Rgba.Black with {A = 0});
        DrawTexturePro(GameEffectsTextures[GameEffectTextureIndex].Texture,
            Source, DestEffect, Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        GameEffectTextureIndex = 0;
        DrawTexturePro(Runtime.CurrentRuntime.Textures["gameplay_background.png"], BGSource,Fullscreen, Vector2.Zero, 0, Rgba.White);
        BeginTextureMode(GameEffectsTextures[0]);
        ClearBackground(Rgba.Black with {A = 0});
        DrawTexturePro(GameEffectsTextures[2].Texture,
            Source, DestEffect,
            Vector2.Zero, 0, Rgba.White);
        DrawTexturePro(GameBox.Box.Texture,
            Source, DestEffect,
            Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        foreach (GameplayScreenEffect gse in GameBox.ScreenEffects.Where(x => x.Layer == GameplayScreenEffect.EffectLayer.BackgroundAndGameplay))
        {
            GameEffectTextureIndex = (GameEffectTextureIndex + 1) % 2;
            BeginTextureMode(GameEffectsTextures[GameEffectTextureIndex]);
            ClearBackground(Rgba.Black with {A = 0});
            gse.ApplyShading(time);
            DrawTexturePro(GameEffectsTextures[(GameEffectTextureIndex+1) % 2].Texture,
                Source, DestEffect, Vector2.Zero, 0, Rgba.White);
            EndShaderMode();
            EndTextureMode();
        }
        DrawTexturePro(GameEffectsTextures[GameEffectTextureIndex].Texture,
            Source, Dest, Vector2.Zero, 0, Rgba.White);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["difficulties_ingame.png"],
            DifficultySource, DifficultyTarget with{ Height = (float)(Helper.ComputeObjectTimeStart(time,2f, .25f) * DifficultyTarget.Height) },
            Vector2.Zero, 0, Rgba.White);
        DrawTexturePro(GameBox.UIAboveGameplay.Texture,
            UIAboveSource,
            Dest,
            Vector2.Zero, 0, Rgba.White);
        DrawTexturePro(GameBox.UILeft.Texture,
            UILeftSource,
            LeftDest,
            Vector2.Zero, 0, Rgba.White);
        TouchControls.Draw();
        if (time - TimeAppear > 2f)
            return;
        DrawTexturePro(Runtime.CurrentRuntime.Textures["difficulties_ingame.png"],
            DifficultySource, DifficultyTargetStart with{ Height = (float)((1-Helper.EaseInOutElasticF((float)Helper.ComputeObjectTimeStart(time,1.75f, .25f))) * DifficultyTarget.Height) },
            Vector2.Zero, 0, Rgba.White);
    }
    
    public override void Unload()
    {
        Runtime.CurrentRuntime.SetScreenRenderingFrom(0);
        UnloadRenderTexture(GameEffectsTextures[0]);
        UnloadRenderTexture(GameEffectsTextures[1]);
        UnloadRenderTexture(GameEffectsTextures[2]);
        UnloadRenderTexture(GameEffectsTextures[3]);
        GameBox.Dispose();
    }
    
    #if DEBUG
    public override void DrawImgui()
    {
        ImGui.Begin("Gameplay Screen Debug Info");
        ImGui.Text("Tick: "+GameBox.CurrentTick);
        ImGui.Text("Time: "+GameBox.GetTime());
        ImGui.Text("TPS: "+GameBox.CurrentTick / GameBox.GetTime());
        ImGui.Text("Paused: "+GameBox.IsPaused);
        ImGui.Text("Game Over: "+GameBox.IsGameOver);
        ImGui.End();
        if (GameBox.StageInfo != null)
        {
            ImGui.Begin($"Stage Info [{GameBox.StageInfo.Chapters.Length}]: ");
            for(int i = 0; i < GameBox.StageInfo.Chapters.Length; i++)
            {
                if (GameBox.StageInfo.Chapters[i] == GameBox.ChapterInfo)
                    ImGui.Text($"v Current chapter v");
                ImGui.Text($"{i}. {GameBox.StageInfo.Chapters[i].Length}");
                if (GameBox.StageInfo.Chapters[i] == GameBox.ChapterInfo)
                    ImGui.Text($"^ Current chapter ^");
            }
            ImGui.End();
            ImGui.Begin($"Overlay Info [{GameBox.GameplayOverlays.Count}]: ");
            for(int i = 0; i < GameBox.GameplayOverlays.Count; i++)
            {
                ImGui.Text($"{i}. {GameBox.GameplayOverlays[i].GetType()}");
            }
            ImGui.End();
            ImGui.Begin($"Effect Info [{GameBox.ScreenEffects.Count}]: ");
            for(int i = 0; i < GameBox.ScreenEffects.Count; i++)
            {
                ImGui.Text($"{i}. {GameBox.ScreenEffects[i]}");
            }
            ImGui.End();
        }

        ImGui.Begin("Stage objects");
        foreach (var obj in GameBox.BoxObjects)
        {
            ImGui.Text($"{obj.CreatedAt}, {obj.Position} {obj.TextureSize}");
        }
        ImGui.End();
        ImGui.Begin("Debug Strings");
        foreach (var debugString in GameBox.DebugStrings)
        {
            ImGui.Text(debugString);
        }
        ImGui.End();
        GameBox.DebugStrings.Clear();
        base.DrawImgui();
    }
#endif
    
    protected override void Created()
    {
        //GameBox.UpdateScoreFirstTime();
        //GameBox.UpdateUI();
        //Runtime.CurrentRuntime.SetScreenRenderingFrom(Runtime.CurrentRuntime.GetScreenIndex(this));
        base.Created();
    }
    
    
}

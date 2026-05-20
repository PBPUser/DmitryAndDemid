using DmitryAndDemid.Common;
using static Raylib_cs.Raylib;
using DmitryAndDemid;
using DmitryAndDemid.Data;
using Raylib_cs;
using System.Numerics;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;
using ImGuiNET;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DmitryAndDemid.Screens;

public class GameplayScreen : Screen
{
    public GameplayScreen(ProtogonistData data, int difficulty, FileStageInfo stage, int chapter, bool practice)
    {
        SetBackground(Runtime.CurrentRuntime.Textures["gameplay_background.png"]);
        //Game = new Game(data, stage, this, difficulty);
        Source = new Rectangle(0, 0, 384, -448);
        UIAboveSource = new Rectangle(0, 0, 384 * Runtime.CurrentRuntime.ScaleF, -448 * Runtime.CurrentRuntime.ScaleF);
        Dest = new Rectangle(32 * Runtime.CurrentRuntime.ScaleF, 16 * Runtime.CurrentRuntime.ScaleF, 384 * Runtime.CurrentRuntime.ScaleF, 448 * Runtime.CurrentRuntime.ScaleF);
        DialogDest = Helper.GetFullscreenSource();
        DialogSource = Helper.GetFullscreenSource();
        DialogSource.Height *= -1;
        DieShader = Runtime.CurrentRuntime.Shaders["die"];
        PauseMenu = new PauseMenu(this);
        SetShaderValue(
            DieShader, 
            GetShaderLocation(DieShader, "scale"),
            Runtime.CurrentRuntime.ScaleF,
            ShaderUniformDataType.Float
            );
        LocationDiePosition = GetShaderLocation(DieShader, "pos");
        LocationDieTime = GetShaderLocation(DieShader, "time");
        DifficultySource = new Rectangle(0, 160*difficulty, 1920, 160);
        DifficultyTargetStart = Helper.Scale(new Rectangle(152, 20, 144, 12), Runtime.CurrentRuntime.ScaleF);
        DifficultyTarget = Helper.Scale(new Rectangle(456, 24, 144, 12), Runtime.CurrentRuntime.ScaleF);

        LetterWidth = (int)(MeasureTextEx(Runtime.CurrentRuntime.Fonts["kodemono"],
            "j",
            (int)(24 * Runtime.CurrentRuntime.ScaleF),
            0).X+(int)(2 * Runtime.CurrentRuntime.ScaleF));
        GameEffectsTextures[0] = LoadRenderTexture(384, 448);
        GameEffectsTextures[1] = LoadRenderTexture(384, 448);
        GameEffectsTextures[2] = LoadRenderTexture(384, 448);
        GameEffectsTextures[3] = LoadRenderTexture(384, 448);
        GameBox = new GameBox(this, data, stage, chapter, difficulty, practice);

        PauseEffect = new GameplayScreenEffect(GameBox, new Vector2(), int.MaxValue, "pause", float.MaxValue, float.MaxValue)
        {
            UseSteps = true,
            StepLength = .25f,
            UseRealTime = true
        };
    }

    public int LetterWidth = 0;
    public PauseMenu PauseMenu;
    
    Rectangle Source;
    Rectangle Dest;
    Rectangle DialogSource;
    Rectangle DialogDest;
    private Rectangle UIAboveSource;

    private static Rectangle Fullscreen = Helper.GetFullscreenSource();
    private static Rectangle BGSource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["gameplay_background.png"]);
    private static Rectangle DestEffect = new Rectangle(0, 0, 384, 448); 

    private Rectangle DifficultySource;
    private Rectangle DifficultyTargetStart;
    private Rectangle DifficultyTarget;

    public GameplayScreenEffect PauseEffect;
    
    RenderTexture2D[] GameEffectsTextures = new RenderTexture2D[4];
    private int GameEffectTextureIndex = 1;
    
    //public Game? Game;
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
                PauseEffect.TimeAppear = (float)Raylib.GetTime();
                PauseEffect.TimeDisappear = float.MaxValue;
            }
            else
            {
                Runtime.CurrentRuntime.RemoveScreen(PauseMenu);
                GameBox.RemoveScreenEffect(PauseEffect);
                PauseEffect.TimeDisappear = (float)(Raylib.GetTime()+1);
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
        if (time < GameBox.CountTimeFrom)
            return;
        GameBox.ProcessInput();
        if ((IsKeyDown(KeyboardKey.Escape) ||
             Controller.IsButtonDown(Configuration.Config.PauseButton)) 
            && !GameBox.IsGameOver && GetTime() - MenuScreen.PreviousKeyTimestamp > MenuScreen.MenuSwitchCooldown)
        {
            MenuScreen.PreviousKeyTimestamp = GetTime();
            Paused = !Paused;
        }
        base.TopUpdate();
    }

    private Shader DieShader;
    private int LocationDiePosition;
    private int LocationDieTime;
    
    public override void Render()
    {
        float time = GameBox.GetTime();
        if (time < -.5)
            return;
        GameBox.RenderBox();
        BeginTextureMode(GameEffectsTextures[0]);
        DrawTexturePro(GameBox.Background.Texture,
            Source, DestEffect,
            Vector2.Zero, 0, Color.White);
        EndTextureMode();
        GameEffectTextureIndex = 0;
        foreach (var gse in GameBox.ScreenEffects.Where(x => x.Layer == GameplayScreenEffect.EffectLayer.BackgroundOnly))
        {
            GameEffectTextureIndex = (GameEffectTextureIndex + 1) % 2;
            BeginTextureMode(GameEffectsTextures[GameEffectTextureIndex]);
            ClearBackground(Color.Black with {A = 0});
            gse.ApplyShading(time);
            DrawTexturePro(GameEffectsTextures[(GameEffectTextureIndex+1) % 2].Texture,
                Source, DestEffect, Vector2.Zero, 0, Color.White);
            EndShaderMode();
            EndTextureMode();
        }
        BeginTextureMode(GameEffectsTextures[2]);
        ClearBackground(Color.Black with {A = 0});
        DrawTexturePro(GameEffectsTextures[GameEffectTextureIndex].Texture,
            Source, DestEffect, Vector2.Zero, 0, Color.White);
        EndTextureMode();
        GameEffectTextureIndex = 0;
        DrawTexturePro(Runtime.CurrentRuntime.Textures["gameplay_background.png"], BGSource,Fullscreen, Vector2.Zero, 0, Color.White);
        BeginTextureMode(GameEffectsTextures[0]);
        ClearBackground(Color.Black with {A = 0});
        DrawTexturePro(GameEffectsTextures[2].Texture,
            Source, DestEffect,
            Vector2.Zero, 0, Color.White);
        DrawTexturePro(GameBox.Box.Texture,
            Source, DestEffect,
            Vector2.Zero, 0, Color.White);
        EndTextureMode();
        foreach (GameplayScreenEffect gse in GameBox.ScreenEffects.Where(x => x.Layer == GameplayScreenEffect.EffectLayer.BackgroundAndGameplay))
        {
            GameEffectTextureIndex = (GameEffectTextureIndex + 1) % 2;
            BeginTextureMode(GameEffectsTextures[GameEffectTextureIndex]);
            ClearBackground(Color.Black with {A = 0});
            gse.ApplyShading(time);
            DrawTexturePro(GameEffectsTextures[(GameEffectTextureIndex+1) % 2].Texture,
                Source, DestEffect, Vector2.Zero, 0, Color.White);
            EndShaderMode();
            EndTextureMode();
        }
        DrawTexturePro(GameEffectsTextures[GameEffectTextureIndex].Texture,
            Source, Dest, Vector2.Zero, 0, Color.White);
        //DrawTexturePro(Game.Dialog.Texture,
        //    DialogSource, DialogDest,
        //    Vector2.Zero, 0, Color.White);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["difficulties_ingame.png"],
            DifficultySource, DifficultyTarget with{ Height = (float)(Helper.ComputeObjectTimeStart(time,2f, .25f) * DifficultyTarget.Height) },
            Vector2.Zero, 0, Color.White);
        DrawTexturePro(GameBox.ScoreTexture.Texture,
            GameBox.ScoreSrc, 
            GameBox.ScoreDest, 
            Vector2.Zero, 0, Color.White);
        DrawTexturePro(GameBox.ScoreTexture.Texture,
            GameBox.ScoreSrc, 
            GameBox.ScoreDest, 
            Vector2.Zero, 0, Color.White);
        DrawTexturePro(GameBox.UIAboveGameplay.Texture,
            UIAboveSource,
            Dest,
            Vector2.Zero, 0, Color.White);
        //DrawTexturePro(
        //    Game.UITexture.Texture,
        //    new Rectangle(0, Game.UITexture.Texture.Height, Game.UITexture.Texture.Width,
        //        -Game.UITexture.Texture.Height),
        //    new Rectangle(Game.UIPositionX, Game.UIPositionY, Game.UITexture.Texture.Width, Game.UITexture.Texture.Height),
        //    Vector2.Zero, 0, Color.White);
        
        if (time - TimeAppear > 2f)
            return;
        DrawTexturePro(Runtime.CurrentRuntime.Textures["difficulties_ingame.png"],
            DifficultySource, DifficultyTargetStart with{ Height = (float)((1-Helper.EaseInOutElasticF((float)Helper.ComputeObjectTimeStart(time,1.75f, .25f))) * DifficultyTarget.Height) },
            Vector2.Zero, 0, Color.White);
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
                {
                    ImGui.Text($"v Current chapter v");
                }
                ImGui.Text($"{i}. {GameBox.StageInfo.Chapters[i].Length}");
                if (GameBox.StageInfo.Chapters[i] == GameBox.ChapterInfo)
                {
                    ImGui.Text($"^ Current chapter ^");
                }
            }
            ImGui.End();
            ImGui.Begin($"Overlay Info [{GameBox.GameplayOverlays.Count}]: ");
            for(int i = 0; i < GameBox.GameplayOverlays.Count; i++)
            {
                ImGui.Text($"{i}. {GameBox.GameplayOverlays[i].GetType()}");
            }
            ImGui.End();
        }

        ImGui.Begin("Stage objects");
        foreach (var obj in GameBox.BoxObjects)
        {
            ImGui.Text($"{obj.CreatedAt}, {obj.FloatingPoints[0x13]}");
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

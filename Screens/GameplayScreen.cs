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
    public GameplayScreen(ProtogonistData data, int difficulty, FileStageInfo[] stages, int chapter, bool practice,
        PlayerControllerBase? controller = null, GameType mode = GameType.Default)
    {
        Mode = mode;
        PlaybackController = controller;
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
        GameBox = new GameBox(this, data, stages, chapter, difficulty, practice, PlaybackController, mode);
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

        if (Configuration.Config.Vertical)
            ApplyVerticalLayout();
    }

    /// <summary>
    /// Portrait layout: the playfield fills the screen width from the top, and the HUD dashboard (score,
    /// hearts, bombs, graze) sits in the strip below it rather than in a side column. The playfield's own
    /// coordinate space is unchanged — only where the finished render lands on screen.
    /// </summary>
    private void ApplyVerticalLayout()
    {
        float w = Runtime.CurrentRuntime.Width;
        float h = Runtime.CurrentRuntime.Height;

        // Playfield: fill the width, top-aligned (the playfield is 384x448).
        float playHeight = w * 448f / 384f;
        Dest = new Rect(0, 0, w, playHeight);

        // HUD: the dashboard, kept at its own 224x480 aspect, dropped into the strip left over below the
        // playfield and pinned to the bottom-left.
        float stripHeight = MathF.Max(0, h - playHeight);
        float hudScale = UILeftSource.Height > 0 ? stripHeight / UILeftSource.Height : 1f;
        LeftDest = new Rect(0, h - stripHeight, UILeftSource.Width * hudScale, stripHeight);
    }

    private ProtogonistData Data;
    private int Difficulty;
    private FileStageInfo[] Stages;
    private int Chapter;
    private bool Practice;

    /// <summary>Which mode spawned this run — Default (main game), Extra, Practice or SpellPractice. Continues
    /// are offered only in Default; Practice / SpellPractice seed the life counters differently.</summary>
    public readonly GameType Mode;

    /// <summary>Non-null when this screen is playing back a replay (a ReplayController) instead of live input.</summary>
    private PlayerControllerBase? PlaybackController;

    /// <summary>Title-screen attract mode: plays a replay, and bails back to the title on any input.</summary>
    public bool IsDemo;

    public GameplayScreen CreateCopy() => new(Data, Difficulty, Stages, Chapter, Practice, mode: Mode);
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
            // A demo (attract mode) never shows the pause / game-over menu. On death the box sets
            // IsGameOver → IsPaused → Paused here; without this guard that would queue an orphaned PauseMenu
            // onto the title screen. The demo instead just ends (TopUpdate removes it), which is byte-for-byte
            // the same clean teardown as a replay that finished by clearing.
            if (IsDemo)
                return;
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
        // Start rendering at this screen: the gameplay background is opaque and fully covers the menu beneath
        // it, so drawing the main menu every frame under it is wasted work — and its animated waves/character
        // would bleed through the semi-transparent pause overlay. The pause menu sits ABOVE this screen in the
        // stack, so it still renders (last) on top of the frozen gameplay.
        // Set from here (not Created()) because the screen is not in the Screens list yet when Created runs.
        int index = Runtime.CurrentRuntime.GetScreenIndex(this);
        if (index > 0)
            Runtime.CurrentRuntime.SetScreenRenderingFrom(index);

        GameBox.Update();
    }

    public override void TopUpdate()
    {
        float time = GameBox.GetTime();
        if (time < 0)
            return;
        // Attract-mode demo: end (back to the title) on any input, on the player's death, or once a cleared
        // run has finished fading. No pause, no live input processing — the replay drives everything.
        if (IsDemo)
        {
            if (AttractInput.AnyInput() || GameBox.IsGameOver || (GameBox.Cleared && GameBox.ClearFade >= 1f))
            {
                Runtime.CurrentRuntime.RemoveScreen(this);
                // Cover the hand-off back to the title with a plain black fade + rotating fifo, matching the
                // BlackLoadingScreen shown when the demo loaded.
                BlackLoadingScreen? loader = null;
                loader = new BlackLoadingScreen(0.9, 0.4, () => Runtime.CurrentRuntime.RemoveScreen(loader), true, 0);
                Runtime.CurrentRuntime.AddScreen(loader);
            }
            return;
        }
        // A cleared normal run fades out and then rolls into the character's ending, which in turn plays the
        // staff roll. If no ending is authored for this character we just fall back to the main menu.
        if (GameBox.Cleared && GameBox.ClearFade >= 1f)
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
            // Only a live run rolls into ending → staff roll → results → replay save. A replay that happens to
            // clear just returns to wherever it was launched from.
            if (PlaybackController == null)
                ShowEnding();
            return;
        }
        GameBox.ProcessInput();
        // The on-screen pause button toggles pause the same way Escape does — live play or replay, never demo
        // (the demo path returned above). Polled every frame so its tap edge is not missed.
        bool touchPause = TouchControls.Enabled && TouchControls.ConsumePauseTap();
        if ((IsKeyDown(KeyCode.Escape) ||
             Controller.IsButtonDown(Configuration.Config.PauseButton) || touchPause)
            && !GameBox.IsGameOver && GetTime() - MenuScreen.PreviousKeyTimestamp > MenuScreen.MenuSwitchCooldown)
        {
            MenuScreen.PreviousKeyTimestamp = GetTime();
            Paused = !Paused;
        }
        base.TopUpdate();
    }

    /// <summary>
    /// Routes a full main-game clear into the character's "good" ending (<c>Endings/{ID}_good.json</c>), asking
    /// it to run the staff roll after. A missing or unreadable ending file just drops back to the main menu.
    /// </summary>
    private void ShowEnding()
    {
        string path = $"Assets/Data/Endings/{Data.ID}_good.json";
        if (!Assets.Exists(path))
            return;
        try
        {
            EndingInfo? info = System.Text.Json.JsonSerializer.Deserialize<EndingInfo>(Assets.ReadAllText(path));
            if (info != null)
                Runtime.CurrentRuntime.AddScreen(new EndingScreen(Difficulty, info, showStaffRoll: true, this));
        }
        catch
        {
            // A malformed ending file should not strand the player — fall through to the menu.
        }
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
        // On a normal last-stage clear, fade ALL gameplay (playfield + HUD) toward black. A screen-effect shader
        // only covers the playfield layers, so the fade is applied here, at the on-screen composite, by tinting
        // every final blit from white down to black.
        float fade = GameBox.ClearFade;
        Rgba tint = fade <= 0f
            ? Rgba.White
            : new Rgba((byte)(255 * (1 - fade)), (byte)(255 * (1 - fade)), (byte)(255 * (1 - fade)), 255);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["gameplay_background.png"], BGSource,Fullscreen, Vector2.Zero, 0, tint);
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
            Source, Dest, Vector2.Zero, 0, tint);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["difficulties_ingame.png"],
            DifficultySource, DifficultyTarget with{ Height = (float)(Helper.ComputeObjectTimeStart(time,2f, .25f) * DifficultyTarget.Height) },
            Vector2.Zero, 0, tint);
        DrawTexturePro(GameBox.UIAboveGameplay.Texture,
            UIAboveSource,
            Dest,
            Vector2.Zero, 0, tint);
        DrawTexturePro(GameBox.UILeft.Texture,
            UILeftSource,
            LeftDest,
            Vector2.Zero, 0, tint);
        // Live play gets the movement/action controls; replay and the attract demo do not — their input is on
        // rails, so the sticks and buttons would be dead. The pause button rides along in live play and replay
        // (so a viewer can pause/leave), but never in the demo, which exits on any touch.
        if (PlaybackController == null)
            TouchControls.Draw();
        if (!IsDemo)
            TouchControls.DrawPause();
        // Attract-mode indicator: "DEMO PLAY" slowly fading in and out in the middle of the screen. Driven by
        // the wall clock (GetTime here is Gfx.GetTime) so it keeps pulsing even after game-over freezes the box.
        if (IsDemo)
        {
            var demoTex = Runtime.CurrentRuntime.Textures["demo-play.png"];
            float demoFade = 0.5f + 0.5f * MathF.Sin((float)GetTime() * 0.6f);   // slow pulse
            DrawTexture(demoTex,
                (Runtime.CurrentRuntime.Width - demoTex.Width) / 2,
                (Runtime.CurrentRuntime.Height - demoTex.Height) / 2,
                Rgba.White with { A = (byte)(255 * demoFade) });
        }
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

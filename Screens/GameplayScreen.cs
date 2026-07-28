using DmitryAndDemid.Rendering;
using DmitryAndDemid.Common;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid;
using DmitryAndDemid.Data;
using System.Numerics;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;
using Gdk;
#if DEBUG
using ImGuiNET;
#endif
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Screen = DmitryAndDemid.Common.Screen;

namespace DmitryAndDemid.Screens;

public class GameplayScreen : Screen
{
    public GameplayScreen(ProtogonistData data, int difficulty, FileStageInfo[] stages, int chapter, bool practice,
        PlayerControllerBase? controller = null, GameType mode = GameType.Default, int startStage = 0)
    {
        UIPracticeOverplaySource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["spell_practice_overlay.png"]);
        UIPracticeOverplayDestination = new Rect(432, 96, 192,64) * Runtime.CurrentRuntime.ScaleF;
        
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
        DialogSource = Helper.GetFullscreenSource();
        DialogDest = Helper.GetFullscreenSource();
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
        GameBox = new GameBox(this, data, stages, chapter, difficulty, practice, PlaybackController, mode, startStage);
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
#if SWITCH
        Runtime.SwTrace("[gp] GameplayScreen ctor done");
#endif
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

        // The HUD render target (Helper.GetFullSourceRenderTexture) reports a NEGATIVE height — that encodes
        // the vertical flip — so layout maths must use the magnitudes. Reading the raw negative height was the
        // bug: `UILeftSource.Height > 0` was always false, hudScale fell back to 1, and the dashboard was
        // stretched into a sliver pinned to the bottom-left corner.
        float hudSrcW = MathF.Abs(UILeftSource.Width);
        float hudSrcH = MathF.Abs(UILeftSource.Height);

        // Playfield fills the width at the top, but is capped at 78% of the height so the strip below is always
        // tall enough to hold the HUD at a usable size. Keep the 384x448 aspect and centre it horizontally in
        // case the cap makes it narrower than the screen.
        float playHeight = MathF.Min(w * 448f / 384f, h * 0.78f);
        float playWidth = playHeight * 384f / 448f;
        Dest = new Rect((w - playWidth) / 2f, 0, playWidth, playHeight);

        // HUD dashboard: keep its own aspect, scale it to fill the leftover bottom strip's height, and CENTRE
        // it there.
        float stripTop = playHeight;
        float stripHeight = MathF.Max(0, h - stripTop);
        float hudScale = hudSrcH > 0 ? stripHeight / hudSrcH : 1f;
        float hudWidth = hudSrcW * hudScale;
        LeftDest = new Rect((w - hudWidth) / 2f, stripTop, hudWidth, stripHeight);
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
    private Rect UIPracticeOverplaySource;
    private Rect UIPracticeOverplayDestination;

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
        _gpFrame++;
        GpTrace("PreRender enter");
        // Start rendering at this screen: the gameplay background is opaque and fully covers the menu beneath
        // it, so drawing the main menu every frame under it is wasted work — and its animated waves/character
        // would bleed through the semi-transparent pause overlay. The pause menu sits ABOVE this screen in the
        // stack, so it still renders (last) on top of the frozen gameplay.
        // Set from here (not Created()) because the screen is not in the Screens list yet when Created runs.
        int index = Runtime.CurrentRuntime.GetScreenIndex(this);
        if (index > 0)
            Runtime.CurrentRuntime.SetScreenRenderingFrom(index);

        GpTrace("PreRender before box.Update");
        GameBox.Update();
        GpTrace("PreRender after box.Update");
    }

    public override void TopUpdate()
    {
        GpTrace("TopUpdate enter");
        float time = GameBox.GetTime();
        if (time < 0)
        {
            GpTrace("TopUpdate early (intro)");
            return;
        }
        GpTrace("TopUpdate past intro");
        // Attract-mode demo: end (back to the title) on any input, on the player's death, or once a cleared
        // run has finished fading. No pause, no live input processing — the replay drives everything.
        if (IsDemo)
        {
            if (AttractInput.AnyInput() || GameBox.IsGameOver || GameBox.ClearSequenceDone)
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
        if (GameBox.ClearSequenceDone)
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
        GpTrace("TopUpdate after input/pause");
        base.TopUpdate();
        GpTrace("TopUpdate done");
    }

    /// <summary>
    /// Routes a full main-game clear into the character's "good" ending (<c>Endings/{ID}_good.json</c>), asking
    /// it to run the staff roll after. A missing or unreadable ending file just drops back to the main menu.
    /// </summary>
    private void ShowEnding()
    {
        // Good vs bad ending: a "good" clear is a one-credit clear (no continues) on Normal or harder. Easy —
        // even a 1cc — and any run that spent a continue get the BAD ending and do NOT unlock Extra.
        bool goodEnding = GameBox.Difficulty >= 1 && GameBox.ContinuesUsed == 0;
        UnlockClearTrophies(goodEnding);

        string suffix = goodEnding ? "good" : "bad";
        string path = $"Assets/Data/Endings/{Data.ID}_{suffix}.json";
        if (!Assets.Exists(path))
            path = $"Assets/Data/Endings/{Data.ID}_good.json";   // fall back if the bad-ending file is absent
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

    /// <summary>
    /// Unlocks the clear trophies for a finished main-game run and, on a good clear, unlocks Extra. Uses the real
    /// trophy set in Assets/Data/Trophy (indices: 0-3 clear_easy/normal/hard/max, 4 clear_extra, 5-7
    /// clearas_&lt;akob/sugar/qaw&gt;_good, 8-10 clearas_&lt;…&gt;_bad, 11 clear_good, 12 clear_bad).
    /// </summary>
    private void UnlockClearTrophies(bool goodEnding)
    {
        var pd = PlayerData.Instance;
        int diff = GameBox.Difficulty;
        bool extra = GameBox.Mode == GameType.Extra;
        if (extra)
            pd.SetTrophyUnlocked(4, true);                       // clear_extra
        else if (diff >= 0 && diff <= 3)
            pd.SetTrophyUnlocked(diff, true);                    // clear_easy/normal/hard/max
        int charBase = Data.ID switch { "akob" => 0, "sugar" => 1, "qaw" => 2, _ => -1 };
        if (charBase >= 0)
            pd.SetTrophyUnlocked((goodEnding ? 5 : 8) + charBase, true);   // clearas_<char>_good / _bad
        pd.SetTrophyUnlocked(goodEnding ? 11 : 12, true);        // clear_good / clear_bad
        // A good (Normal+ 1cc) main-game clear unlocks Extra; a bad or Easy clear does not.
        if (goodEnding && !extra)
            pd.IsExtraUnlocked = true;
    }

    private ShaderHandle DieShader;
    private int LocationDiePosition, LocationDieTime;

    // Switch bring-up: brackets the first few gameplay frames (PreRender→TopUpdate→Render) to a crash-proof file
    // so a hard native fault (which the managed handler can't catch) localises to a stage. No-op off-Switch and
    // after the first frames. _gpFrame is bumped once per frame in PreRender.
    private static int _gpFrame;
    private static void GpTrace(string s)
    {
#if SWITCH
        if (_gpFrame < 8) Runtime.SwTrace($"[gp f{_gpFrame}] " + s);
#elif ANDROID
        // Same bring-up bracketing on Android: a native GL fault in the first gameplay frames leaves no managed
        // stack for OnDrawFrame's catch, so the last line logged here is where it died. Routed to logcat.
        if (_gpFrame < 8) Utils.Platform.Trace($"[gp f{_gpFrame}] " + s);
#endif
    }

    // The frame brackets above stop at frame 8, but the first REAL render (RenderBox + the effect-shader
    // composite) only happens once the loading screen lets the box time cross -0.5, ~180 frames in. This
    // counter tracks real renders instead, so the post-RenderBox effect passes — the other prime suspect for a
    // native GL fault — are traced on the first few of them.
    private static int _realRender;
    private static void RTrace(string s)
    {
#if ANDROID || SWITCH
        if (_realRender < 3) Utils.Platform.Trace($"[gpR {_realRender}] " + s);
#endif
    }

    public override void Render()
    {
        float time = GameBox.GetTime();
        if (time < -.5)
            return;
        RTrace("render start");
        GameBox.RenderBox();
        RTrace("after RenderBox");
        BeginTextureMode(GameEffectsTextures[0]);
        // Clear before the background composite, like every other effects-target bind below. The background
        // draw does not always cover every texel with full opacity, and on a tiled GPU (Adreno) the uncleared
        // remainder is garbage rather than zero — the source of random flicker in the playfield.
        ClearBackground(Rgba.Black with { A = 0 });
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
        RTrace("after BackgroundOnly effects");
        BeginTextureMode(GameEffectsTextures[2]);
        ClearBackground(Rgba.Black with {A = 0});
        DrawTexturePro(GameEffectsTextures[GameEffectTextureIndex].Texture,
            Source, DestEffect, Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        GameEffectTextureIndex = 0;
        // Fade ALL gameplay (playfield + HUD) toward black. Two fades share this path: the last-stage clear
        // (ClearFade, a one-way fade into the ending) and the between-levels transition (StageFade, which dips
        // out then back in as one stage becomes the next). A screen-effect shader only covers the playfield
        // layers, so the fade is applied here, at the on-screen composite, by tinting every final blit.
        float fade = MathF.Max(GameBox.ClearFade, GameBox.StageFade);
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
        RTrace("after BackgroundAndGameplay effects");
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
        if(GameBox.IsSpellPractice)
            DrawTexturePro(Runtime.CurrentRuntime.Textures["spell_practice_overlay.png"], UIPracticeOverplaySource, 
                UIPracticeOverplayDestination, Vector2.Zero, 0, Rgba.White);
        RTrace("after UI composite");
        // Live play gets the movement/action controls; replay and the attract demo do not — their input is on
        // rails, so the sticks and buttons would be dead. The pause button rides along in live play and replay
        // (so a viewer can pause/leave), but never in the demo, which exits on any touch.
        if (PlaybackController == null)
            TouchControls.Draw();
        RTrace("after TouchControls.Draw");
        if (!IsDemo)
            TouchControls.DrawPause();
        RTrace("render done");
        _realRender++;
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
        // Stage complete: the banner first appears OVER the live gameplay (while the run holds, then either rolls
        // into the next stage or begins the fade to black on the last one), and stays up through that fade. Its
        // own fade-in is driven by StageBannerAlpha, independent of the screen's fade-to-black.
        if (GameBox.ShowStageBanner)
        {
            TextureHandle done = Runtime.CurrentRuntime.Textures["stage_complete.png"];
            float a = GameBox.StageBannerAlpha;
            // Centre the banner INSIDE the gamebox (the playfield Dest rect), not across the whole window, so it
            // reads as part of the play area rather than a full-screen overlay.
            float w = Dest.Width * 0.9f;
            float h = done.Height * (w / done.Width);
            DrawTexturePro(done, Helper.GetFullSource(done),
                new Rect(Dest.X + (Dest.Width - w) / 2f, Dest.Y + (Dest.Height - h) / 2f, w, h),
                Vector2.Zero, 0, Rgba.White with { A = (byte)(255 * a) });
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

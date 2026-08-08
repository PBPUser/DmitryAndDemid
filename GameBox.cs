using DmitryAndDemid.Rendering;
using System.Diagnostics;
using System.Numerics;
using DmitryAndDemid.Backgrounds;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Gameplay.Effects;
using DmitryAndDemid.Gameplay.GameplayOverlays;
using DmitryAndDemid.Gameplay.RuntimeData;
using DmitryAndDemid.Screens;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid;

public class GameBox : IDisposable
{
    public const float TargetTPS = 60;
    public List<RuntimeObject>  BoxObjects = new();
    public List<GameplayOverlay> GameplayOverlays = new();
    public RuntimeStageInfo? StageInfo;
    public RuntimeChapter? ChapterInfo;
    public Player Player;
    public string ProtogonistId;
    public int Difficulty;
    public int TickOffset = 0;
    public int CurrentTick = 0;

    /// <summary>
    /// Benchmark mode: when set, <see cref="BoxUpdate"/> skips the wall-clock tick gate so the sim can be driven
    /// as fast as the CPU allows (each call = one full tick). Used by the headless <c>--bench</c> / config Bench
    /// path to measure raw sim throughput — the make-or-break number for the Switch/mono-nx port. Off in normal
    /// play, where the fixed 60 TPS pacing is essential. See Runtime.RunBench and docs/switch-port.md.
    /// </summary>
    public bool BenchMode;
    public bool IsFailed = false;
    bool SpellTimedOut = false;

    /// <summary>Selects the chapter-end reward drops for the CURRENT chapter (set by its create script, consumed
    /// and reset in NextChapter). 0 = the standard heart-piece/star-piece rule; 1 = the stage-3 pizza finale
    /// (full heart / full bomb / full power).</summary>
    public int CustomChapterReward = 0;

    // Per-RUN tallies (GameBox lives for the whole run, so these accumulate across every stage — unlike the
    // per-chapter IsFailed). Used for clear conditions like "no bombs", "no deaths", "N toilets spawned".
    public int BombsUsedThisRun = 0;
    public int DeathsThisRun = 0;
    public int ToiletsSpawnedThisRun = 0;

    /// <summary>The spell.failed wording chosen when the current card was failed. Picked once — the key has
    /// several variants and Translate() randomises, so resolving it per frame would make the word flicker.</summary>
    string SpellFailedText = "";
    public bool InChapterDelay = false;
    FileStageInfo[] Stages;
    List<RuntimeObject> ObjectsAddQueue = new();
    List<RuntimeObject> ObjectsRemoveQueue = new();
    List<GameplayScreenEffect> ScreenEffectsToAdd = new();
    List<GameplayScreenEffect> ScreenEffectsToRemove = new();
    List<GameplayOverlay> GameplayOverlaysToAdd = new();
    List<GameplayOverlay> GameplayOverlaysToRemove = new();
    SignalGameplayOverlay SignalGameplayOverlay;
    GameplayScreen GameplayScreen;
    float TickLength = 1f / TargetTPS;
    int StageIndex = 0;
    bool RequiresRefresh;
    public bool IsSpellPractice;
    public bool IsPractice;
    public bool IsReplay;   // playback (replay viewer / title demo): never mutates the player's progress
    /// <summary>True only for the title-screen attract demo (a subset of replays). Set on the owning screen via
    /// an object-initializer after construction, so read it lazily through here rather than caching it.</summary>
    public bool IsDemo => GameplayScreen.IsDemo;

    /// <summary>Holding Control while watching a replay (or the title-screen demo) races the tick target ahead
    /// 4x faster than wall time; how much of that actually lands depends on how many frames render while it's
    /// held, same as the normal 60 TPS throttle below already depends on frame rate.</summary>
    float ReplaySpeedMultiplier => IsReplay && IsKeyDown(KeyCode.LeftControl) ? 4f : 1f;

    int ComputeCurrentTickFromStartingTime => (int)(GetTime() * TargetTPS * ReplaySpeedMultiplier);
    
    public GameBox(GameplayScreen screen, ProtogonistData data, FileStageInfo[] stages, int chapter, int difficulty, bool isPractice,
        PlayerControllerBase? controller = null, GameType mode = GameType.Default, int startStage = 0)
    {
        GameplayScreen = screen;
        Mode = mode;
        IsPractice = isPractice;
        // Spell practice is a subset of practice: it keeps its own records and, unlike a full practice run,
        // ends after the single card the player picked (see NextChapter).
        IsSpellPractice = mode == GameType.SpellPractice;
        // A supplied controller (a ReplayController) drives playback; live play passes none and gets the human one.
        // Playback (replay viewer or the title-screen demo) must not touch the player's progress — no stage
        // unlocks, no spell-card records — so remember whether this box is a replay.
        IsReplay = controller is ReplayController;
        Player = new Player(this, data, controller ?? new PlayerController());
        ProtogonistId = data.ID;
        Difficulty = difficulty;
        Stages = stages;
        PauseTimestamp = (float)(GetTime() + 3);
        Background = LoadRenderTexture(384, 448);
        Box = LoadRenderTexture(384, 448);
        UIAboveGameplay = LoadRenderTexture(
            (int)(Runtime.CurrentRuntime.ScaleF * 384f),
            (int)(Runtime.CurrentRuntime.ScaleF * 448f)
        );
        UILeft = LoadRenderTexture((int)(Runtime.CurrentRuntime.ScaleF * 224),
            (int)(Runtime.CurrentRuntime.ScaleF * 480));
        EnsureLaserGlow();   // bake the emitter glow here — outside any render target (can't nest in the box pass)
        // startStage lets a replay begin partway through the campaign (the level the viewer picked). Live play
        // always passes 0. The full stages array is kept either way, so play advances normally from here on.
        StageIndex = Math.Clamp(startStage, 0, stages.Length - 1);
        LoadStage(stages[StageIndex], chapter, difficulty);
        SignalGameplayOverlay = new SignalGameplayOverlay(this);
        AddOverlay(SignalGameplayOverlay);
        // The point-of-collection hint line is opt-out (Settings → item line hint).
        if (Configuration.Config.ShowItemLineHint)
            AddOverlay(new ItemGetBorderLineOverlay(this));
        // Seed the life/bomb stock per mode: spell practice is a bare single-life attempt (0 spare lives, no
        // bombs), while full practice hands the player a maxed-out stock to freely experiment with. The main
        // game and extra keep the character's default starting stock.
        if (Mode == GameType.SpellPractice)
            Player.SetLivesAndBombs(0, 0);
        else if (Mode == GameType.Practice)
            Player.SetLivesAndBombs(MaxLives, MaxBombs);
        UpdateUI();
    }

    /// <summary>The most lives / bombs the HUD can show (8 slots); what full practice seeds the player with.</summary>
    public const int MaxLives = 8;
    public const int MaxBombs = 8;

    /// <summary>Which mode started this run. Continues are a main-game (Default) affordance only.</summary>
    public GameType Mode;

    /// <summary>How many continues a run may spend before game-over is final.</summary>
    public const int MaxContinues = 5;

    /// <summary>True while the player may still spend a continue: a live main-game run under the cap.</summary>
    public bool CanContinue => Mode == GameType.Default && !IsReplay && Continue < MaxContinues;

    /// <summary>Continues still available to spend on the current game-over.</summary>
    public int ContinuesRemaining => Math.Max(0, MaxContinues - Continue);

    /// <summary>
    /// Spends a continue: revives the player with a fresh life/bomb stock and lifts the game-over, resuming the
    /// run from where it fell. No-op once the cap is reached or outside the main game.
    /// </summary>
    public void UseContinue()
    {
        if (!CanContinue)
            return;
        ClearBullets();
        Player.Revive();
        IsGameOver = false;   // the setter unpauses and bumps the continue count
    }
    
    #region Update

    public const int DelayBetweenChapters = 120;

    /// <summary>How long (seconds) the "stage complete" banner is held over the LIVE gameplay after a stage's
    /// last chapter, before the run either advances to the next stage or begins the final fade.</summary>
    public const float StageCompleteHold = 3.2f;

    /// <summary>How long (seconds) a normal last-stage clear takes to fade all gameplay to black.</summary>
    public const float ClearFadeDuration = 4f;

    /// <summary>How long (seconds) the fully-black frame (banner still up) is held after the fade finishes and
    /// before the run rolls into the ending.</summary>
    public const float ClearHoldDuration = 1.2f;

    /// <summary>Set when a stage's last chapter ends: the "stage complete" banner is up over the still-visible
    /// gameplay while the run decides between the next stage and the ending. Distinct from <see cref="Cleared"/>,
    /// the final fade-to-black that only happens on the very last stage.</summary>
    public bool StageComplete { get; private set; }
    private float StageCompleteAt;

    /// <summary>When the "stage complete" banner first appeared. Drives its quick fade-in and stays valid through
    /// the following fade-to-black, so the banner holds full opacity across the whole end sequence.</summary>
    private float BannerAppearAt;

    /// <summary>Set when a normal (non-practice) run reaches the end of the LAST stage; starts the fade-out.</summary>
    public bool Cleared { get; private set; }
    private float ClearedAt;

    /// <summary>0 before/at the clear, ramping to 1 (fully black) over <see cref="ClearFadeDuration"/>.</summary>
    public float ClearFade => Cleared ? Math.Clamp((GetTime() - ClearedAt) / ClearFadeDuration, 0f, 1f) : 0f;

    /// <summary>Seconds to fade the old stage out to black, and to fade the new stage back in, when rolling from
    /// one stage into the next. The stage is swapped at the fully-black midpoint so the cut is unseen.</summary>
    public const float StageFadeOutDuration = 0.7f;
    public const float StageFadeInDuration = 0.7f;

    /// <summary>True once the between-levels transition has swapped in the next stage (we are now fading it IN).</summary>
    private bool StageSwapped;

    /// <summary>True during the between-levels fade transition (after the banner hold, before the new stage is
    /// fully faded in). Once set, the hold is done and the fade state machine drives every frame.</summary>
    private bool StageAdvancing;

    /// <summary>Raw wall-clock (<see cref="Gfx.GetTime"/>) time the between-levels transition began. The
    /// transition spans <see cref="SwapToNextStage"/>, which re-anchors <see cref="CountTimeFrom"/> and so resets
    /// the box's own <c>GetTime()</c> — hence the transition is timed off the wall clock, which does not reset.</summary>
    private double StageTransitionAt;

    /// <summary>0 = clear, 1 = fully black: the between-levels fade (out then in). Applied by GameplayScreen the
    /// same way <see cref="ClearFade"/> is, so the whole screen dips through black as one stage becomes the next.</summary>
    public float StageFade { get; private set; }

    /// <summary>True while the "stage complete" banner should be drawn — over live gameplay first, then held
    /// through the fade to black (the banner is hidden behind full black once the next stage is swapped in).</summary>
    public bool ShowStageBanner => (StageComplete && !StageSwapped) || Cleared;

    /// <summary>The banner's opacity: a ~0.4s fade-in from when it appeared, then held at 1.</summary>
    public float StageBannerAlpha => ShowStageBanner ? Math.Clamp((GetTime() - BannerAppearAt) / 0.4f, 0f, 1f) : 0f;

    /// <summary>True once the whole clear sequence — fade to black THEN the held banner — is over, which is when
    /// the screen may transition on to the ending.</summary>
    public bool ClearSequenceDone => Cleared && GetTime() - ClearedAt >= ClearFadeDuration + ClearHoldDuration;

    /// <summary>
    /// Drives the end-of-stage sequence once the "stage complete" hold elapses. On the last stage it begins the
    /// final fade to black (the ending). Otherwise it fades the screen out to black, swaps in the next stage at
    /// the fully-black midpoint, then fades the new stage back in — a clean crossfade between levels.
    /// </summary>
    void AdvanceStageOrFinish()
    {
        // Decide last-stage-vs-next only BEFORE the swap. Once StageSwapped is set we are mid fade-IN of the new
        // stage; SwapToNextStage has already incremented StageIndex, so re-testing it here would wrongly see the
        // freshly-swapped-in stage as "the last one" and jump to the ending in the middle of the transition.
        if (!StageSwapped && StageIndex + 1 >= Stages.Length)
        {
            // Last stage — begin the slow fade to black. The banner stays up via ShowStageBanner through it.
            StageComplete = false;
            StageAdvancing = false;
            Cleared = true;
            ClearedAt = GetTime();
            return;
        }

        // Between levels: a fade-out / swap / fade-in transition, timed off the wall clock (see StageTransitionAt).
        float t = (float)(Gfx.GetTime() - StageTransitionAt);
        if (t < StageFadeOutDuration)
        {
            StageFade = t / StageFadeOutDuration;   // fading the finished stage to black
        }
        else if (!StageSwapped)
        {
            StageFade = 1f;                         // fully black — swap the stage here, unseen
            SwapToNextStage();
            StageSwapped = true;
        }
        else
        {
            float fadeIn = t - StageFadeOutDuration;
            StageFade = Math.Clamp(1f - fadeIn / StageFadeInDuration, 0f, 1f);   // revealing the new stage
            if (fadeIn >= StageFadeInDuration)
            {
                StageFade = 0f;
                StageSwapped = false;
                StageAdvancing = false;
                StageComplete = false;              // transition done — hand control back to normal play
            }
        }
    }

    /// <summary>Re-anchors the clock and loads the next stage from its first chapter, as a fresh start.</summary>
    void SwapToNextStage()
    {
        // The previous stage's last chapter was already unloaded when it ended; clear the reference so
        // LoadStage's NextChapter doesn't re-record its spell attempt or unload it a second time.
        ChapterInfo = null;
        StageIndex++;
        CurrentTick = 0;
        TickOffset = 0;
        // Clear any pending death-respawn: RestoreTick is an absolute tick from the PREVIOUS stage (set to
        // deathTick+120 whenever the player last died). With CurrentTick now back at 0 it would read as "still
        // respawning", so Player.Update would yank the player to the start column and slide it in from far
        // off-screen over RestoreTick/60 seconds — the player losing its position and only drifting back after
        // a while. Zeroing it lets the player carry its position straight into the next stage.
        Player.RestoreTick = 0;
        CountTimeFrom = Gfx.GetTime();
        LoadStage(Stages[StageIndex], 0, Difficulty);
    }
    public void Update()
    {
        BoxUpdate();
    }

    public void ProcessInput()
    {
        
    }
    #endregion
    #region Managment
    #if DEBUG
    private Vector2 DarkStrengthPos;
    #endif
    private int ChapterIndex = -1;
    private bool ChapterScoreShown = false;
    private Stopwatch? SpellcardStopwatch = null;
    public const int Deathlength = 15;
    private int ScoreChapterMax;
    private int ChapterScoreCurrent;
    private const int ChapterMaxScoreDelay = 240;
    
    void BoxUpdate()
    {
        if (IsPaused)
            return;

        // While the "stage complete" banner is up, the sim is frozen on its last frame (the banner sits over the
        // still-visible gameplay). The banner holds for StageCompleteHold, then the fade-out/swap/fade-in
        // transition (or, on the last stage, the final fade to black) takes over — driven every frame by
        // AdvanceStageOrFinish. Once the transition begins we stop re-checking the hold: SwapToNextStage resets
        // the box clock partway through, so the hold check (box GetTime) is only valid before it.
        if (StageComplete)
        {
            if (!StageAdvancing && GetTime() - StageCompleteAt < StageCompleteHold)
                return;
            if (!StageAdvancing)
            {
                StageAdvancing = true;
                StageTransitionAt = Gfx.GetTime();
            }
            AdvanceStageOrFinish();
            return;
        }

        // A dialog now plays OVER the live simulation instead of freezing it: the player can move, shoot and
        // graze, and the background keeps animating, while the characters talk. What it does NOT do is start the
        // fight — see the chapter-clock hold below and the boss-action / damage guards further down.
        if (IsDialogActive)
        {
            Dialog!.Update();
            if (Dialog.Finished)
                EndDialog();
        }
        if (!BenchMode && CurrentTick >= ComputeCurrentTickFromStartingTime)
            return;
        Score = (int)MathUtil.MoveTowards(Score, ScoreTarget, MathF.Max((ScoreTarget - Score) / 30f, 10));
        CurrentTick++;
        // Hold the chapter/spell clock for the length of the dialog: the non-spell/spell only STARTS once the
        // player has read it. CurrentTick still advances (so the player's own shoot cadence and focus animation,
        // which read raw CurrentTick, keep working), but TickOffset — the same lever the boss-death fast-forward
        // uses — is walked back in lockstep so CurrentTickWithOffset (the chapter length countdown, the spell
        // timer, the score ramp) stays frozen until the conversation ends.
        if (IsDialogActive)
            TickOffset--;
        if (CurrentTick + TickOffset >= ChapterInfo.TickStart + ChapterInfo.Length)
        {
            if (!ChapterScoreShown)
            {
                ShowChapterScore();
                ClearAll(false);
                ChapterTitleDisappear = GetTime();
            }
            if (CurrentTick + TickOffset >= ChapterInfo.TickStart + ChapterInfo.Length + DelayBetweenChapters)
            {
                NextChapter();
            }
        }
        else if (!IsDialogActive)
            // The chapter's per-tick spawn script is part of the attack, so it waits for the dialog too.
            ChapterInfo.UpdateScript?.Invoke(ChapterInfo);
        if (RequiresRefresh)
        {
            ScreenEffects.AddRange(ScreenEffectsToAdd);
            ScreenEffects.RemoveAll(x => ScreenEffectsToRemove.Contains(x));
            BoxObjects.AddRange(ObjectsAddQueue);
            BoxObjects.RemoveAll(x => ObjectsRemoveQueue.Contains(x));
            GameplayOverlays.AddRange(GameplayOverlaysToAdd);
            GameplayOverlays.RemoveAll(x => GameplayOverlaysToRemove.Contains(x));
            ObjectsAddQueue.Clear();
            ObjectsRemoveQueue.Clear();
            ScreenEffectsToAdd.Clear();
            ScreenEffectsToRemove.Clear();
            GameplayOverlaysToAdd.Clear();
            GameplayOverlaysToRemove.Clear();
            RequiresRefresh = false;
        }

        if (ChapterInfo.Type == ChapterType.Spell)
        {
            ChapterScoreCurrent = (int)Math.Clamp(ScoreChapterMax * (MathF.Abs(CurrentTick + TickOffset - (ChapterInfo.TickStart + ChapterInfo.Length)) / (ChapterInfo.Length - ChapterMaxScoreDelay)), 0, ScoreChapterMax);
            if((CurrentTickWithOffset - ChapterInfo.TickStart) % TargetTPS == 0 && !InChapterDelay && (ChapterInfo.TickStart + ChapterInfo!.Length - CurrentTickWithOffset) < (ChapterInfo!.Length > 600 ? 300 : 600))
                Helper.PlaySound(Runtime.CurrentRuntime.Sounds["pre-timeout"]);
            if (CurrentTickWithOffset - ChapterInfo.TickStart == ChapterInfo.Length)
            {
                // An invincible-boss card can only be outlasted, not captured. Surviving it to the clock WITHOUT
                // dying or bombing (IsFailed covers both) is a success and pays the full card score, rather than
                // the time-decayed amount a normal card's late capture would give.
                if (!IsFailed && ChapterInfo.BossInvincible)
                {
                    AddOverlay(new ScoreGameplayOverlay(this, ScoreChapterMax * 10, ChapterInfo.Length,
                        SpellcardStopwatch!.Elapsed.TotalSeconds, 0.5f, 3f));
                    ScoreTarget += ScoreChapterMax;
                }
                else
                {
                    AddOverlay(new TimerGameplayOverlay(this, "bonus-failed.png", ChapterInfo.Length, SpellcardStopwatch!.Elapsed.TotalSeconds, 0.5f, 3f));
                    Helper.PlaySound(Runtime.CurrentRuntime.Sounds["fault"]);
                }
                SpellcardStopwatch!.Stop();
                SpellcardStopwatch = null;
                SpellTimedOut = true;   // ran the clock out: an attempt, but not a capture
            }
        }
        #if DEBUG
        if (IsKeyDown(KeyCode.LeftControl))
        {
            if(IsKeyDown(KeyCode.B))
                if(!GameplayOverlays.Any(x => x is ScoreGameplayOverlay && GetTime() - x.TimeAppear < 0.5))
                    AddOverlay(new ScoreGameplayOverlay(this, GetRandomValue(0, int.MaxValue), 600, 1.4, .5f, 3f));
        }
        else if (IsKeyDown(KeyCode.RightShift) && ComputeCurrentTickFromStartingTime % TargetTPS == 0)
        {
            if(IsKeyDown(KeyCode.L))
                SpawnMysticalToilet();
            if (IsKeyDown(KeyCode.P))
            {
                var obj = RuntimeObject.LoadFromFile(RuntimeObject.CollectableFEIs[0], this);
                obj.X = Player.X;
                obj.Y = 64;
                AddObject(obj);
            }
            if (IsKeyDown(KeyCode.D))
                Player.HeartPoints++;
            if (IsKeyDown(KeyCode.F))
                Player.HeartPoints--;
            if (IsKeyDown(KeyCode.G))
                Player.HeartSpices++;
            if (IsKeyDown(KeyCode.H))
                Player.HeartSpices--;
            if (IsKeyDown(KeyCode.R))
                // test laser: fires straight down the player's column from the top edge (telegraph .75s, fire 2s)
                SpawnLaser(new Vector2(Player.X, 0), MathF.PI / 2f, 480, 24, 45, 120, 30);
            if (IsKeyDown(KeyCode.T))
                // test ray: a widening cone fired down the player's column from the top edge (spread 40px/side)
                SpawnRay(new Vector2(Player.X, 0), MathF.PI / 2f, 300, 16, 40, 45, 120, 30);
            if(IsKeyDown(KeyCode.L))
                UpdateUI();
            if (IsKeyDown(KeyCode.Z))
                DarkStrengthPos = new Vector2(Player.X, Player.Y);
            if (IsKeyDown(KeyCode.X))
                if (!ScreenEffects.Any(x => x is DarkStrengthScreenEffect && GetTime() - x.TimeAppear < 1.5))
                {
                    var pos = new Vector2(Player.X, Player.Y);
                    var offset = DarkStrengthPos - pos;
                    offset *= new Vector2(MathUtil.Sign(offset.X), MathUtil.Sign(offset.Y));
                    AddScreenEffect(new DarkStrengthScreenEffect(this, offset, pos, 0b0000_1111, 20, GetTime(), GetTime()+2f));
                }
            if(IsKeyDown(KeyCode.C))
                if(!ScreenEffects.Any(x => x is StrengthScreenEffect && GetTime() - x.TimeAppear < 0.05))
                    AddScreenEffect(new StrengthScreenEffect(this, new Vector2(Player.X, Player.Y), 40, GetTime(), GetTime()+0.75f, 0x11bb2a, 0x11bb2a));
        }
        #endif
        float x, y, z, r;
        
        foreach(var  obj in BoxObjects)
        {
            var bitMask = obj.Header[0];
            if ((bitMask & RuntimeObject.FlagIsDied) == RuntimeObject.FlagIsDied)
            {
                if (obj.Header[0xA] + Deathlength < CurrentTick)
                    RemoveObject(obj);
                continue;
            }
            if ((bitMask & RuntimeObject.FlagIsUsed) == RuntimeObject.FlagIsUsed)
            {
                if (obj.Header[0xA] + Deathlength < CurrentTick)
                    RemoveObject(obj);
                continue;
            }
            x = obj.FloatingPoints[0x10];
            y = obj.FloatingPoints[0x11];
            z = obj.FloatingPoints[0x12];
            r = obj.FloatingPoints[0x5];
            if ((obj.Header[0] & RuntimeObject.FlagIsCollectableBullet) == RuntimeObject.FlagIsCollectableBullet)
            {
                obj.UpdateCollectableBullet();
            }
            else if ((obj.Header[0] & RuntimeObject.FlagIsCollectable) == RuntimeObject.FlagIsCollectable)
            {
                obj.UpdateCollectable();
            }
            else
            {
                // During a dialog the boss/enemy attack scripts are held (the fight hasn't started), but bullets
                // must keep moving — including the player's own shots. Every bullet (player or enemy) carries
                // FlagIsBullet; entities do not, so gate the per-tick action on it. Entrance movement (move-to-
                // target) inside Update still runs, so a boss can slide into place while talking.
                bool runAction = !IsDialogActive ||
                                 (obj.Header[0] & RuntimeObject.FlagIsBullet) == RuntimeObject.FlagIsBullet;
                obj.Update(runAction);
            }
            obj.FloatingPoints[0x20] = obj.FloatingPoints[0x10] - x;
            obj.FloatingPoints[0x21] = obj.FloatingPoints[0x11] - y;
            obj.FloatingPoints[0x22] = obj.FloatingPoints[0x12] - z;
            obj.FloatingPoints[0x23] = obj.FloatingPoints[0x5] - r;
            // Lasers and rays are exempt from the offscreen cull: a beam is commonly anchored at a screen edge (or
            // fired from just outside), and it removes itself when its life ends rather than by leaving the box.
            // Objects explicitly flagged FlagPersistOffscreen (remove-proof) are likewise exempt — e.g. a bullet
            // formation that spawns outside the box and travels in.
            if ((obj.Header[0] & RuntimeObject.FlagIsLaser) != RuntimeObject.FlagIsLaser &&
                (obj.Header[0] & RuntimeObject.FlagIsRay) != RuntimeObject.FlagIsRay &&
                (obj.Header[0] & RuntimeObject.FlagPersistOffscreen) != RuntimeObject.FlagPersistOffscreen &&
                (obj.X < -32 || obj.Y < -32 || obj.X > 416 || obj.Y > 480))
            {
                RemoveObject(obj);
                continue;
            }
            // Between chapters (InChapterDelay) AND while a dialog is on screen, no damage is dealt or taken:
            // player shots and bombs pass straight through the boss, and nothing can die, until the fight starts.
            if (InChapterDelay || IsDialogActive)
                continue;
            if (obj.Health <= 0)
            { 
                if ((obj.Header[0] & RuntimeObject.FlagIsBoss) == RuntimeObject.FlagIsBoss)
                {
                        // Jump the chapter clock to this chapter's end so it wraps up (score, then NextChapter).
                        // This SETS the offset — `CurrentTick + TickOffset` lands exactly on TickStart+Length —
                        // rather than ADDING to it. Adding accumulated any prior offset: after a dialog (which now
                        // holds the clock by driving TickOffset NEGATIVE) a boss kill landed SHORT of the end, so
                        // the chapter never advanced; across several kills it overshot and stages blitzed through.
                        TickOffset = ChapterInfo.TickStart + ChapterInfo.Length - CurrentTick;
                        int ticks = CurrentTickWithOffset - ChapterInfo.TickStart;
                        SpellcardStopwatch ??= new Stopwatch();
                 
                        if (ChapterInfo.Type == ChapterType.Spell)
                        { 
                            if (!IsFailed )
                        {
                            AddOverlay(new ScoreGameplayOverlay(this, ChapterScoreCurrent * 10, ticks,
                                SpellcardStopwatch!.Elapsed.TotalSeconds, .5f, 3));
                            ScoreTarget += ChapterScoreCurrent;
                        }
                        else
                            AddOverlay(new TimerGameplayOverlay(this, "bonus-failed.png", ticks,
                                SpellcardStopwatch!.Elapsed.TotalSeconds, 0.5f, 3f));
                        }
                    
                        SpellcardStopwatch = null;
                        obj.UpdateAction = null;
                }
                else
                {
                    obj.Header[0] |= RuntimeObject.FlagIsUsed;
                    obj.Header[0xa] = CurrentTick;
                    // The player killed this enemy (entities spawn with Health >= 1, so Health <= 0 here means it
                    // was chipped to death by player fire). Award the authored per-enemy value — Header[0x6],
                    // "Added score when killed" — or a base bounty when the enemy has none, so a kill always scores.
                    ScoreTarget += obj.Header[0x6] > 0 ? obj.Header[0x6] : EnemyKillScore;
                    obj.DieAction?.Invoke(obj);
                    RemoveObject(obj);
                    ScreenEffects.Add(new EntityDeathScreenEffect(this, new Vector2(obj.X, obj.Y), 40, GetTime(), GetTime()+0.75f, obj.Header[0xC], obj.Header[0xB]));
                }
            }
            if ((bitMask & RuntimeObject.FlagDangerousRelatedToEnemy) ==
                RuntimeObject.FlagDangerousRelatedToEnemy)
            {
                bool broken = false;
                foreach (var obj2 in BoxObjects)
                {
                    if ((obj2.Header[0] & RuntimeObject.FlagIsBullet) == RuntimeObject.FlagIsBullet)
                        continue;
                    // An invincible entity (e.g. a boss during its scripted entrance / dialog) absorbs no damage:
                    // player shots pass through it so it cannot be chipped or killed before the fight begins.
                    if ((obj2.Header[0] & RuntimeObject.FlagInvincible) == RuntimeObject.FlagInvincible)
                        continue;
                    if (MathUtil.Vector2Distance(obj.Position, obj2.Position) <
                        (obj.CollisionScale * obj.FloatingPoints[0x13] +
                         obj2.CollisionScale * obj2.FloatingPoints[0x13]) / 2)
                    {
                        obj.Header[0] |= RuntimeObject.FlagIsDied;
                        obj.Header[0xa] = CurrentTick;
                        obj2.FloatingPoints[0] -= obj.FloatingPoints[0x9];
                        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["damage"]);
                        Player.Weapon.AddShootTargetScore();
                        Player.Weapon.SpawnDistortionEffect((int)obj.X, (int)obj.Y);
                        RemoveObject(obj);
                        broken = true;
                        break;
                    }
                }
                if (broken)
                    continue;
            }
            if(!Player.CollisionEnabled || Player.Weapon.IsBombActive)
                continue;
            // A bullet the player has already claimed — caught on Akob's fork, or swept up by a bomb / a death —
            // is loot, not a threat. It flies AT the player on purpose (UpdateCollectableBullet), so leaving it in
            // the damage pass would kill them the moment their prize arrived; it must not graze either. The bomb
            // and death cases only ever looked safe because the player happens to be uncollidable through both.
            if ((bitMask & RuntimeObject.FlagIsCollectableBullet) == RuntimeObject.FlagIsCollectableBullet)
                continue;
            if ((bitMask & RuntimeObject.FlagDangerousRelatedToPlayer) ==
                RuntimeObject.FlagDangerousRelatedToPlayer)
            {
                if ((bitMask & RuntimeObject.FlagIsLaser) == RuntimeObject.FlagIsLaser)
                {
                    // A laser only kills during its fire phase. Hit-test the player point against the beam
                    // segment (a capsule of half-width LaserWidth/2). It is not removed on hit and never grazes.
                    int lage = CurrentTick - obj.CreatedAt;
                    if (lage >= obj.LaserTelegraphTicks && lage < obj.LaserTelegraphTicks + obj.LaserFireTicks)
                    {
                        Vector2 p1 = obj.Position;
                        Vector2 p2 = p1 + Helper.GetDirection(obj.RenderRotation) * obj.LaserLength;
                        float dist = MathUtil.PointSegmentDistance(new Vector2(Player.X, Player.Y), p1, p2);
                        if (dist < Player.CollisionRadius / 2 + obj.LaserWidth / 2)
                        {
                            Player.Die();
                            if (!IsFailed)
                                SpellFailedText = Helper.Translate("spell.failed");
                            IsFailed = true;
                        }
                    }
                    continue;
                }
                if ((bitMask & RuntimeObject.FlagIsRay) == RuntimeObject.FlagIsRay)
                {
                    // A ray kills only during its fire phase, like a laser, but its lethal area is a widening cone:
                    // project the player onto the beam axis (clamped to [0, length]) and compare the perpendicular
                    // distance against the half-width interpolated from LaserWidth/2 at the emitter to
                    // LaserWidth/2 + RaySpread at the tip.
                    int rage = CurrentTick - obj.CreatedAt;
                    if (rage >= obj.LaserTelegraphTicks && rage < obj.LaserTelegraphTicks + obj.LaserFireTicks)
                    {
                        Vector2 dir = Helper.GetDirection(obj.RenderRotation);
                        Vector2 rel = new Vector2(Player.X, Player.Y) - obj.Position;
                        float along = Vector2.Dot(rel, dir);
                        if (along >= 0 && along <= obj.LaserLength)
                        {
                            float perp = (rel - dir * along).Length();
                            float t = obj.LaserLength > 0 ? along / obj.LaserLength : 0f;
                            float halfW = obj.LaserWidth / 2f + obj.RaySpread * t;
                            if (perp < halfW + Player.CollisionRadius / 2f)
                            {
                                Player.Die();
                                if (!IsFailed)
                                    SpellFailedText = Helper.Translate("spell.failed");
                                IsFailed = true;
                            }
                        }
                    }
                    continue;
                }
                var distance = MathUtil.Vector2Distance(new(Player.X, Player.Y), obj.Position);
                var collision = Player.CollisionRadius + obj.CollisionScale * obj.FloatingPoints[0x13];
                if (distance < collision / 2)
                {
                    Player.Die();
                    if (!IsFailed)
                        SpellFailedText = Helper.Translate("spell.failed");  // 4 variants; pick one, once
                    IsFailed = true;
                    RemoveObject(obj);
                    continue;
                }
                if ((bitMask & RuntimeObject.FlagIsBullet) != RuntimeObject.FlagIsBullet)
                    continue;
                if ((bitMask & RuntimeObject.FlagIsGrazed) == RuntimeObject.FlagIsGrazed)
                    continue;
                if (distance > collision * 4)
                    continue;
                Player.Graze++;
                AddScreenEffect(new GrazeScreenEffect(this, new Vector2(obj.X, obj.Y), 0, GetTime(), GetTime()+1f, -Helper.FindAngle(new Vector2(Player.X, Player.Y), new Vector2(obj.X, obj.Y))));
                obj.Header[0] |= RuntimeObject.FlagIsBoss;
            }
        }
        Player.Update();
    }

    void SpawnDrop(Vector2 position, Drop drop)
    {
        var rnd = new Random(CurrentTickWithOffset);
        for (int i = 0; i < drop.DropPower; i++)
            AddObject(SpawnCollectableWithPop(RuntimeObject.CollectableFEIs[0], position, rnd));
        for (int i = 0; i < drop.DropLargePower; i++)
            AddObject(SpawnCollectableWithPop(RuntimeObject.CollectableFEIs[1], position, rnd));
    }

    /// <summary>
    /// Loads a collectable at <paramref name="position"/> and gives it a slight pop: a small upward launch with a
    /// wide horizontal jitter, so items burst out of the kill and arc up before the collectable gravity
    /// (FloatingPoints[6]) pulls them back down, instead of appearing motionless.
    /// </summary>
    RuntimeObject SpawnCollectableWithPop(FileEntityInfo preset, Vector2 position, Random rnd)
    {
        var obj = RuntimeObject.LoadFromFile(preset, this);
        obj.Position = position;
        // +Y is down, so a negative Y is upward. ~0.8..2.0 up, ±1.5 sideways.
        obj.CollectableVelocity = new Vector2((rnd.NextSingle() - 0.5f) * 3f, -(rnd.NextSingle() * 1.2f + 0.8f));
        return obj;
    }
    
    public void AddObject(RuntimeObject obj)
    {
        ObjectsAddQueue.Add(obj);
        RequiresRefresh = true;
    }

    public void RemoveObject(RuntimeObject obj)
    {
        ObjectsRemoveQueue.Add(obj);
        RequiresRefresh = true;
        obj.DieAction?.Invoke(obj);
        obj.RemoveAction?.Invoke(obj);
        // A boss owns two screen effects — the cursor ring under it and the background distortion — and both are
        // created to last forever (timeDisappear = float.MaxValue), so nothing ever retires them by itself. They
        // also hold a reference to this object and keep reading its position every frame, so when the object goes
        // they have to go with it; otherwise the ring stays on screen after the boss is killed, tracking a corpse.
        // Done here rather than in the death branch so every path is covered (kill, ClearAll, a scripted swap),
        // and nulled so a second RemoveObject on the same object is a no-op — DieAction is already known to fire
        // twice per kill.
        if (obj.BossCircleEffect != null)
        {
            RemoveScreenEffect(obj.BossCircleEffect);
            obj.BossCircleEffect = null;
        }
        if (obj.BackgroundDistortionEffect != null)
        {
            RemoveScreenEffect(obj.BackgroundDistortionEffect);
            obj.BackgroundDistortionEffect = null;
        }
        if ((obj.Header[0] & RuntimeObject.FlagIsBullet) != RuntimeObject.FlagIsBullet)
            if ((obj.Header[0] & RuntimeObject.FlagIsBullet) != RuntimeObject.FlagIsBullet)
            {
                Drop d = obj.GoodDrop;
                if ((obj.Header[0] & RuntimeObject.FlagUseBadDropScenario) != RuntimeObject.FlagUseBadDropScenario)
                    d = IsFailed ? obj.BadDrop : obj.GoodDrop;
                // TODO: Drop Drop
            }
    }

    /// <summary>Spawns a straight-beam laser: telegraph (thin warning) → fire (lethal) → fade. Angle in radians
    /// (screen space, +Y is down); length/width in playfield pixels; phase lengths in ticks (60 per second).</summary>
    public RuntimeObject SpawnLaser(Vector2 origin, float angleRadians, float length, float width,
        int telegraphTicks, int fireTicks, int fadeTicks)
    {
        var laser = RuntimeObject.MakeLaser(this, origin, angleRadians, length, width, telegraphTicks, fireTicks, fadeTicks);
        AddObject(laser);
        return laser;
    }

    /// <summary>Spawns a ray: a laser-like beam that widens into a cone (by <paramref name="spread"/> px per side at
    /// the tip) from a large emitting dot and fades along its finite <paramref name="length"/>. Angle in radians
    /// (+Y down); width/spread in playfield pixels; phase lengths in ticks. Lethal only during the fire phase.</summary>
    public RuntimeObject SpawnRay(Vector2 origin, float angleRadians, float length, float width, float spread,
        int telegraphTicks, int fireTicks, int fadeTicks)
    {
        var ray = RuntimeObject.MakeRay(this, origin, angleRadians, length, width, spread, telegraphTicks, fireTicks, fadeTicks);
        AddObject(ray);
        return ray;
    }

    // A soft radial glow — bright white centre fading to transparent at the edge — baked once from the
    // emit_circle shader. Drawn (tinted) at a laser/ray emitter so its start point reads as a bright gradient
    // (white -> beam colour -> transparent) instead of the old star sprite. Baked outside any render target
    // (from the constructor), since BeginTextureMode can't nest inside the box's own render pass.
    private static TargetHandle LaserGlowTarget;
    private static bool LaserGlowBaked;
    private static TextureHandle LaserGlow => LaserGlowTarget.Texture;

    static void EnsureLaserGlow()
    {
        if (LaserGlowBaked)
            return;
        const int size = 64;
        LaserGlowTarget = LoadRenderTexture(size, size);
        ShaderHandle shader = Runtime.CurrentRuntime.Shaders["emit_circle"];
        TextureHandle any = Runtime.CurrentRuntime.Textures["star.png"];   // content ignored; emit_circle uses only UVs
        BeginTextureMode(LaserGlowTarget);
        ClearBackground(new Rgba(0, 0, 0, 0));
        SetShaderValue(shader, GetShaderLocation(shader, "color"), new Vector3(1f, 1f, 1f), UniformType.Vec3);
        SetShaderValue(shader, GetShaderLocation(shader, "resolution"), new Vector2(size, size), UniformType.Vec2);
        BeginShaderMode(shader);
        DrawTexturePro(any, new Rect(0, 0, any.Width, any.Height), new Rect(0, 0, size, size), Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        SetTextureFilter(LaserGlowTarget.Texture, FilterMode.Bilinear);
        LaserGlowBaked = true;
    }

    void DrawLaser(RuntimeObject obj)
    {
        int age = CurrentTick - obj.CreatedAt;
        int tele = obj.LaserTelegraphTicks, fire = obj.LaserFireTicks, fade = obj.LaserFadeTicks;
        float length = obj.LaserLength, fullWidth = obj.LaserWidth;
        float angleDeg = obj.RenderRotation * 180f / MathF.PI;

        float width;
        Rgba beam, core;
        if (age < tele)
        {
            // telegraph: a thin flickering warning line, so the player can dodge before it fires
            width = MathF.Max(1.5f, fullWidth * 0.14f);
            byte a = (byte)(255 * (0.30f + 0.30f * MathF.Abs(MathF.Sin(age * 0.7f))));
            beam = new Rgba(255, 70, 90, a);
            core = new Rgba(255, 210, 210, a);
        }
        else if (age < tele + fire)
        {
            // fire: full-width beam with a hot white core, snapping to full width over the first few ticks
            float ignite = Math.Clamp((age - tele) / 3f, 0f, 1f);
            width = fullWidth * (0.55f + 0.45f * ignite);
            beam = new Rgba(255, 40, 60, 235);
            core = new Rgba(255, 255, 255, 255);
        }
        else
        {
            // fade: shrink and fade out
            float p = fade > 0 ? Math.Clamp((age - tele - fire) / (float)fade, 0f, 1f) : 1f;
            width = fullWidth * (1f - p);
            beam = new Rgba(255, 40, 60, (byte)(235 * (1f - p)));
            core = new Rgba(255, 255, 255, (byte)(255 * (1f - p)));
        }
        if (width < 0.5f)
            return;

        // Both quads are pinned at the emitter (origin at their left-centre) and rotated to the beam angle, so
        // the beam extends `length` px along the angle from (obj.X, obj.Y).
        DrawRectanglePro(new Rect(obj.X, obj.Y, length, width), new Vector2(0, width / 2f), angleDeg, beam);
        float cw = width * 0.42f;
        DrawRectanglePro(new Rect(obj.X, obj.Y, length, cw), new Vector2(0, cw / 2f), angleDeg, core);

        // Bloom dot at the emitter: a soft additive flare pinned at the laser's origin (obj.X, obj.Y), pulsing
        // and rotating, tinted like the beam with a hot white centre. Its brightness rides the beam's alpha, so
        // it is faint during the telegraph and blazes while the beam fires.
        // Emitter start point: a bright radial gradient — a beam-coloured halo with a hot white core, fading to
        // transparent — instead of the old spinning star. Two additive passes of the baked glow (white centre,
        // transparent edge): the larger tinted the beam colour, the smaller white.
        TextureHandle glow = LaserGlow;
        var glowSrc = new Rect(0, 0, glow.Width, glow.Height);
        float pulse = 0.85f + 0.15f * MathF.Sin(age * 0.5f);
        float outer = (width * 3.0f + 12f) * pulse;
        float inner = outer * 0.5f;
        byte oa = beam.A;
        byte ia = core.A;
        BeginBlendMode(BlendMode.Additive);
        DrawTexturePro(glow, glowSrc, new Rect(obj.X, obj.Y, outer, outer),
            new Vector2(outer / 2f, outer / 2f), 0, beam with { A = oa });
        DrawTexturePro(glow, glowSrc, new Rect(obj.X, obj.Y, inner, inner),
            new Vector2(inner / 2f, inner / 2f), 0, new Rgba(255, 255, 255, ia));
        EndBlendMode();
    }

    void DrawRay(RuntimeObject obj)
    {
        int age = CurrentTick - obj.CreatedAt;
        int tele = obj.LaserTelegraphTicks, fire = obj.LaserFireTicks, fade = obj.LaserFadeTicks;
        float length = obj.LaserLength;
        float baseHalf = obj.LaserWidth / 2f;
        float tipHalf = baseHalf + obj.RaySpread;
        if (length < 1f)
            return;
        float angleDeg = obj.RenderRotation * 180f / MathF.PI;

        // Phase → overall opacity + colour. The ray previews faintly during telegraph (so the cone can be read
        // and dodged), blazes during fire, then fades. It is only lethal during fire (see collision).
        float opacity;
        Rgba beam, core;
        if (age < tele)
        {
            opacity = 0.20f + 0.16f * MathF.Abs(MathF.Sin(age * 0.5f));   // faint pulsing preview
            beam = new Rgba(255, 100, 130, 255);
            core = new Rgba(255, 220, 230, 255);
        }
        else if (age < tele + fire)
        {
            float ignite = Math.Clamp((age - tele) / 3f, 0f, 1f);        // snap in over the first few ticks
            opacity = 0.55f + 0.45f * ignite;
            beam = new Rgba(255, 45, 75, 255);
            core = new Rgba(255, 255, 255, 255);
        }
        else
        {
            float p = fade > 0 ? Math.Clamp((age - tele - fire) / (float)fade, 0f, 1f) : 1f;
            opacity = 1f - p;
            beam = new Rgba(255, 45, 75, 255);
            core = new Rgba(255, 255, 255, 255);
        }
        if (opacity < 0.02f)
            return;

        Vector2 dir = Helper.GetDirection(obj.RenderRotation);
        const int slices = 26;

        // The cone body is a stack of trapezoid slices along the beam. Each slice's half-width is interpolated
        // from baseHalf (at the emitter) to tipHalf (at the end), and its alpha fades from the origin to nothing
        // at the tip — the "gradient spreading from the emitting dot" that also softens the finite length. Drawn
        // with normal alpha so overlapping slices don't blow out.
        for (int i = 0; i < slices; i++)
        {
            float t0 = i / (float)slices, t1 = (i + 1) / (float)slices;
            float mid = (t0 + t1) * 0.5f;
            float halfW = baseHalf + (tipHalf - baseHalf) * mid;
            float lengthFade = 1f - mid;
            Vector2 sp = obj.Position + dir * (t0 * length);
            float w = (t1 - t0) * length + 0.75f;     // tiny overlap so slices don't seam
            float h = 2f * halfW;
            byte a = (byte)Math.Clamp(200f * opacity * lengthFade, 0, 255);
            DrawRectanglePro(new Rect(sp.X, sp.Y, w, h), new Vector2(0, h / 2f), angleDeg, beam with { A = a });
        }

        // A narrower, hotter core cone drawn additively over the body, plus the large emitting dot: a soft
        // additive flare pinned at the origin, sized off the base width + spread so a wider cone gets a bigger
        // mouth. Both pulse/spin like the laser's flare.
        BeginBlendMode(BlendMode.Additive);
        for (int i = 0; i < slices; i++)
        {
            float t0 = i / (float)slices, t1 = (i + 1) / (float)slices;
            float mid = (t0 + t1) * 0.5f;
            float halfW = (baseHalf + (tipHalf - baseHalf) * mid) * 0.4f;
            float lengthFade = 1f - mid;
            Vector2 sp = obj.Position + dir * (t0 * length);
            float w = (t1 - t0) * length + 0.75f;
            float h = 2f * MathF.Max(0.5f, halfW);
            byte a = (byte)Math.Clamp(200f * opacity * lengthFade, 0, 255);
            DrawRectanglePro(new Rect(sp.X, sp.Y, w, h), new Vector2(0, h / 2f), angleDeg, core with { A = a });
        }

        var flare = Runtime.CurrentRuntime.Textures["star.png"];
        var flareSrc = new Rect(0, 0, flare.Width, flare.Height);
        float pulse = 0.75f + 0.25f * MathF.Sin(age * 0.5f);
        float outer = (obj.LaserWidth * 1.6f + obj.RaySpread * 0.6f + 14f) * pulse;
        float inner = outer * 0.5f;
        float spin = age * 2.6f;
        byte oa = (byte)Math.Clamp(210f * opacity, 0, 255);
        byte ia = (byte)Math.Clamp(255f * opacity, 0, 255);
        DrawTexturePro(flare, flareSrc, new Rect(obj.X, obj.Y, outer, outer),
            new Vector2(outer / 2f, outer / 2f), spin, beam with { A = oa });
        DrawTexturePro(flare, flareSrc, new Rect(obj.X, obj.Y, inner, inner),
            new Vector2(inner / 2f, inner / 2f), -spin * 0.6f, new Rgba(255, 255, 255, ia));
        EndBlendMode();
    }

    public void AddScreenEffect(GameplayScreenEffect effect)
    {
        ScreenEffectsToAdd.Add(effect);
        RequiresRefresh = true;
    }

    public void RemoveScreenEffect(GameplayScreenEffect effect)
    {
        ScreenEffectsToRemove.Add(effect);
        RequiresRefresh = true;
    }

    public void AddOverlay(GameplayOverlay overlay)
    {
        GameplayOverlaysToAdd.Add(overlay);
        RequiresRefresh = true;
    }

    public void RemoveOverlay(GameplayOverlay overlay)
    {
        GameplayOverlaysToRemove.Add(overlay);
        RequiresRefresh = true;
    }
    
    /// <summary>
    /// Spawns bullet entity <paramref name="i"/> tinted to <paramref name="color"/> (an 0xRRGGBB int). The
    /// colour is baked into the bullet at load time from its template's Header[4], so it is set just for this
    /// spawn and restored, leaving the shared template untouched for every other spawner.
    /// </summary>
    public RuntimeObject SpawnObject(int i, int color)
    {
        var template = StageInfo.Entities[i];
        int previous = template.Header[4];
        template.Header[4] = color;
        RuntimeObject spawned = SpawnObject(i);
        template.Header[4] = previous;
        return spawned;
    }

    public RuntimeObject SpawnObject(int i)
    {
        if(!StageInfo.Entities[i].IsBullet)
            if (StageInfo.Entities[i].IsBoss)
            {
                var bossList = BoxObjects.Where(x => x.BossId == StageInfo.Entities[i].Header[0x7] && (x.Header[0] & 0x200) == 0x200);
                if (bossList.Count() > 0)
                {
                    var boss = bossList.First();
                    boss.LoadAnotherFile(StageInfo.Entities[i]);
                    boss.CreateAction?.Invoke(boss);
                    return boss;
                }
            }

        var x = RuntimeObject.LoadFromFile(StageInfo.Entities[i], this);
        x.Header[0x17] = CurrentTick;
        if (StageInfo.Entities[i].IsBullet && Player.RestoreTick + 60 > CurrentTick)
        {
            x.Header[2] = 128;
            x.Header[0] |= RuntimeObject.FlagIsCollectableBullet;
            x.CollectableVelocity = Helper.GetDirection(x.Position, new Vector2(Player.X, Player.Y));
        }
        AddObject(x);
        return x;
    }

    public RuntimeObject? MysticalToilet = null;

    /// <summary>How many collectables the toilet is currently holding — 0 when there is no toilet on screen.</summary>
    public int ToiletHoardCount => MysticalToilet?.SwallowedItems?.Count ?? 0;

    /// <summary>
    /// The mystical toilet eats a collectable the player was not claiming (see RuntimeObject.UpdateCollectable).
    /// The item leaves the box but its INSTANCE is kept, so when the toilet dies its die script can put the same
    /// items back exactly as they were. The count is shown in the UI strip while the toilet is alive.
    /// </summary>
    public void SwallowIntoToilet(RuntimeObject collectable)
    {
        if (MysticalToilet == null)
            return;
        (MysticalToilet.SwallowedItems ??= new List<RuntimeObject>()).Add(collectable);
        RemoveObject(collectable);
        UpdateUI();
    }

    public void SpawnMysticalToilet()
    {
        if (MysticalToilet != null)
            return;
        var toilet = RuntimeObject.LoadFromFile(RuntimeObject.MagicalToilet, this);
        toilet.MaxHealth = toilet.Health = MathF.Pow(2, Difficulty) * 10 * Player.Signal + 150;
        MysticalToilet = toilet;
        ToiletsSpawnedThisRun++;
        AddObject(MysticalToilet);
        toilet.Header[0x55] = 120 / (Difficulty + 1);
        toilet.CreatedAt = CurrentTick;   // its 12-second life is measured from here (see the MysticalToilet action)
        toilet.X = 192;
        toilet.Y = 64;
        AddOverlay(new MysticalToiletOverlay(this, 0.25f, 3));
        // The same health bar a boss gets — it has real health (set above) and the player is meant to shoot it
        // down before it leaves, so it needs the same read on how close that is.
        AddOverlay(new BossHealthOverlay(this, toilet));
        // Countdown to the toilet's escape, hovering under it — the chapter timer up top doesn't track this
        // (the toilet spawns off Player.Signal, not off the chapter clock, and isn't shown outside Spell/NonSpell
        // chapters at all).
        AddOverlay(new ToiletTimerOverlay(this, toilet));
        UpdateUI();   // brings up the hoard row in the stats strip (DrawToiletHoard)
        // Arrival flourish: a brown circle collapsing onto the toilet with lightning striking into it, plus a
        // brief nudge of the screen. Shake strength is in texture-coordinate units, so 0.008 is ~3px of the
        // 384-wide playfield — about a twelfth of what the boss death throws, and over in a quarter second.
        // This announces a nuisance, not a death.
        float appearTime = GetTime();
        AddScreenEffect(new ToiletAppearScreenEffect(this, toilet, 4, appearTime, appearTime + 1.1f));
        AddScreenEffect(new ShakeScreenEffect(this, 0.008f, 26, 100, appearTime, appearTime + 0.25f));
        // TODO: Play toilet spawn sound
    }

    public void ClearAll(bool drop)
    {
        foreach (var obj in BoxObjects)
        {
            var bm = obj.Header[0];
            
            if((bm & RuntimeObject.FlagIsBullet) ==  RuntimeObject.FlagIsBullet)
            {
                if (drop)
                {
                    if ((bm & RuntimeObject.FlagIsBullet) != RuntimeObject.FlagIsBullet)
                    {
                        // TODO: Play removal sound
                        // TODO: Add objects removal shader 
                        obj.Header[0] |= RuntimeObject.FlagIsCollectableBullet;
                        obj.Header[2] = 128;
                        obj.CollectableVelocity = -1 * Helper.GetDirection(obj.Position, new Vector2(Player.X, Player.Y));
                    }
                }
                else
                {
                    RemoveObject(obj);
                    // TODO: Add objects removal shader
                }
            }
            else if ((bm & RuntimeObject.FlagIsBoss) == RuntimeObject.FlagIsBoss)
            {
                if ((bm & RuntimeObject.FlagIsFinalBossChapter) == RuntimeObject.FlagIsFinalBossChapter)
                {
                    var time = GetTime();
                    // TODO: Play boss death
                    obj.Header[0] |= RuntimeObject.FlagIsDied;
                    Player.GameBox.AddScreenEffect(new ShakeScreenEffect(Player.GameBox, 0.1f,  20, 100, 
                        time, time+.5f));
                    AddScreenEffect(new BossDeathScreenEffect(this, obj.Position, 45, time, time+2f));
                    RemoveObject(obj);
                    SpawnDrop(obj.Position, IsFailed && (obj.Header[0] & RuntimeObject.FlagUseBadDropScenario) == RuntimeObject.FlagUseBadDropScenario ? obj.BadDrop : obj.GoodDrop);
                }
                else
                {
                    SpawnDrop(obj.Position, IsFailed && (obj.Header[0] & RuntimeObject.FlagUseBadDropScenario) == RuntimeObject.FlagUseBadDropScenario ? obj.BadDrop : obj.GoodDrop);
                    obj.UpdateAction = null;
                }
            }
            else
            {
                if (drop)
                {
                    SpawnDrop(obj.Position, IsFailed && (obj.Header[0] & RuntimeObject.FlagUseBadDropScenario) == RuntimeObject.FlagUseBadDropScenario ? obj.BadDrop : obj.GoodDrop);
                }
                RemoveObject(obj);
            }

        }
    }
    
    public void LoadStage(FileStageInfo stage, int chapter, int difficulty)
    {
        if (!IsReplay)
        {
            PlayerData.Instance.SetStageUnlocked(stage.Header[1], true);
            // Reaching a stage unlocks its music (and the boss music) in the music room. Header[2]/[8] index the
            // music list; unlock by the entry's Number, which is what the music room keys on.
            UnlockMusicByListIndex(stage.Header[2]);
            UnlockMusicByListIndex(stage.Header[8]);
        }
        StageInfo = RuntimeStageInfo.LoadFromFile(stage, difficulty, this);
        AddOverlay(new StageTitleOverlay(this, stage.Header[1]) { TimeAppear = GetTime() + 5f });
        // ...and name the BGM along the bottom of the playfield while the title is up. Header[2] is the stage's
        // music-list index, the same one unlocked above. Header[8] (boss music) gets no card: nothing in the
        // game switches to it yet, so there is no moment to announce.
        AddOverlay(new MusicTitleOverlay(this, stage.Header[2]) { TimeAppear = GetTime() + 5f });

        // `chapter` used to be accepted and then ignored: NextChapter() always advanced from -1 to 0, so a
        // spell-practice run could only ever start on the stage's first chapter. Start where we were asked to.
        ChapterIndex = chapter - 1;
        OnStageEntered();
        NextChapter();
    }

    /// <summary>True once a launched-from-mid-campaign replay has restored its resource snapshot (once per run).</summary>
    private bool ReplayStateRestored = false;

    /// <summary>
    /// Runs as each stage begins (initial load and every stage swap). While recording it snapshots the player's
    /// resources and marks where this stage's inputs start in the buffer; during playback it points the replay
    /// controller at this stage's segment and, on the launched-from stage, restores the recorded resources so a
    /// mid-campaign replay reproduces the run's state instead of a fresh default stock.
    /// </summary>
    void OnStageEntered()
    {
        if (Player.Controller is ReplayController rc)
        {
            rc.SetReadBase(StageIndex);
            if (StageIndex == rc.StageFrom && !ReplayStateRestored)
            {
                ReplayStateRestored = true;
                ReplayStageInfo? info = rc.InfoFor(StageIndex);
                if (info != null)
                {
                    score = scoreTarget = info.Score;
                    Player.X = info.PosX;
                    Player.Y = info.PosY;
                    Player.RestoreState(info.Lives, info.Bombs, info.HeartSpices, info.BombsSpices,
                        info.Power, info.Graze, info.Signal);
                }
            }
        }
        else if (Player.Controller is PlayerController pc)
        {
            pc.BeginRecordingStage(new ReplayStageInfo(0, StageIndex)
            {
                Score = score,
                Lives = Player.LivesValue,
                Bombs = Player.BombsValue,
                HeartSpices = Player.HeartSpicesValue,
                BombsSpices = Player.BombsSpicesValue,
                Power = Player.PowerValue,
                Graze = Player.GrazeValue,
                Signal = Player.SignalValue,
                PosX = Player.X,
                PosY = Player.Y,
            });
        }
    }

    /// <summary>Unlocks the music-room entry at the given music-list index (by its Number), if any.</summary>
    static void UnlockMusicByListIndex(int listIndex)
    {
        if (listIndex < 0 || listIndex >= MusicInfo.MusicInformations.Count)
            return;
        MusicInfo? m = MusicInfo.MusicInformations[listIndex];
        if (m != null)
            PlayerData.Instance.SetMusicUnlocked(m.Number, true);
    }

    public void NextChapter()
    {
        // Once a normal run has been cleared the update loop keeps calling this (the run is not paused so the
        // fade can keep ticking); bail out so the last chapter is not recorded again and again.
        if (Cleared)
            return;

        // The card we are leaving, if it was one, goes into the player's record before we move on.
        if (ChapterInfo is { Type: ChapterType.Spell })
            RecordSpellAttempt(ChapterInfo.SpellcardTitle, !IsFailed && !SpellTimedOut);

        // Chapter-end reward for the spell/non-spell we are leaving (the initial null load and non-boss chapters
        // are skipped). IsFailed is set by both a death and a bomb (PlayerWeapon.Bomb); SpellTimedOut by the clock
        // running out on a spell. The three outcomes are:
        //   fail    = died or used a bomb
        //   timeout = the clock ran out on a NON-timeout card (didn't capture in time) — a survival/TimeoutCard
        //             is "cleared" by surviving to the end, so timing it out is not a fail
        //   clear   = neither of the above (captured, or survived a TimeoutCard / non-spell)
        // Standard reward: clear -> heart piece, fail -> star piece, timeout -> nothing. A card whose create
        // script set CustomChapterReward gets its own drops instead (e.g. the stage-3 pizza finale).
        if (ChapterInfo is { Type: ChapterType.Spell or ChapterType.NonSpell })
        {
            bool fail = IsFailed;
            bool timeout = !IsFailed && !ChapterInfo.TimeoutCard && SpellTimedOut;
            bool clear = !fail && !timeout;

            switch (CustomChapterReward)
            {
                case 1:   // stage-3 pizza finale: full heart on a clean clear, star on fail, full power on timeout
                    if (fail) Player.Bombs++;                 // a star (full bomb)
                    else if (timeout) Player.Power = 400;     // full power
                    else Player.HeartPoints++;                // full heart
                    break;
                default:
                    if (fail) Player.BombsSpices++;           // a star piece
                    else if (clear) Player.HeartSpices++;     // a heart piece (auto-converts to a heart at 4)
                    // timeout: nothing
                    break;
            }
        }
        // Consumed for the chapter we just left; the next chapter's create script re-sets it if needed.
        CustomChapterReward = 0;

        // Spell practice plays exactly the one card the player picked. On the initial load ChapterInfo is still
        // null (nothing has played yet), so we fall through and load the chosen card; once that card is what we
        // are leaving, end straight into the game-over / retry menu instead of advancing into the rest of the
        // stage — which previously either played on through later chapters or left the run frozen with no menu.
        if (IsSpellPractice && ChapterInfo != null)
        {
            ChapterInfo.Unload();
            IsGameOver = true;   // leave ChapterInfo in place (unloaded) — the same shape as the practice end below
            return;
        }

        ChapterIndex++;
        ChapterInfo?.Unload();
        if (StageInfo!.Chapters.Length <= ChapterIndex)
        {
            if (IsPractice)
            {
                IsGameOver = true;
            }
            else
            {
                // Normal run reached the end of a stage. Raise the "stage complete" banner over the live
                // gameplay; BoxUpdate then either advances to the next stage or (on the last one) starts the fade
                // to black. Deliberately NOT setting IsPaused/IsGameOver: those freeze GetTime(), which the banner
                // hold and the fade are both driven off.
                if (!IsSpellPractice)
                {
                    StageComplete = true;
                    StageCompleteAt = BannerAppearAt = GetTime();
                }
            }
            return;
        }

        TimerAppear = GetTime();
        TimerDisappear = float.MaxValue;
        ChapterInfo = StageInfo.Chapters[ChapterIndex];
        // Chapter-local timing (spawn scripts, spell timer, chapter-end) is all CurrentTick + TickOffset -
        // TickStart, and TickStart is CUMULATIVE across the stage. In a normal run CurrentTick has already counted
        // up to TickStart by the time a mid-stage chapter plays. Spell practice jumps straight to one chapter with
        // CurrentTick at 0, so without this the local tick starts at -TickStart: the spawn scripts' conditions
        // (e.g. localTick == 30) don't match for ~TickStart ticks and no bullets appear. Seed TickOffset with
        // TickStart so the picked card starts at local tick 0 and its attack spawns immediately.
        if (IsSpellPractice)
            TickOffset = ChapterInfo.TickStart - CurrentTick;
        ChapterScoreShown = false;
        RenderChapterTitle = false;
        RenderBossTitle = false;
        FireBackgroundEvent("chapter", ChapterIndex);
        if (ChapterInfo.Type == ChapterType.Spell)
        {
            // TODO: Play SpellCard SoundHandle
            SpellcardStopwatch = new Stopwatch();
            SpellcardStopwatch.Start();
            RenderBossTitle = true;
            RenderChapterTitle = true;
            ChapterTitleAppear = GetTime();
            ChapterTitleDisappear = float.MaxValue;
            IsFailed = false;
            SpellTimedOut = false;
            SpellFailedText = "";
            ChapterScoreCurrent = ScoreChapterMax = ChapterInfo.MaxScore;
            AddScreenEffect(new SpellCardAttackScreenEffect(this, Vector2.Zero, 0, GetTime(), GetTime()+2));
            // Volumetric circles pouring out of the spell's focus point (192,96 — the same anchor the spell
            // background shader uses), fading out over the same 2s the attack banner plays.
            AddScreenEffect(new CirclesScreenEffect(this, new Vector2(192, 96), 0, GetTime(), GetTime()+2));
            FireBackgroundEvent("spell");
            DualSenseFeedback.OnSpellCardStart();
        }
        else if (ChapterInfo.Type == ChapterType.NonSpell)
        {
            RenderBossTitle = true;
            // Non-spells did not clear the per-chapter outcome (only the spell path did), which would leak a
            // previous failure into this chapter's end-of-chapter reward. Start it clean.
            IsFailed = false;
            SpellTimedOut = false;
        }
        InChapterDelay = false;
        StartDialog();
        ChapterInfo.CreateScript?.Invoke(ChapterInfo);
    }
    
    public void Dispose()
    {
        UnloadRenderTexture(Background);
        UnloadRenderTexture(ScoreTexture);
        UnloadRenderTexture(HiScoreTexture);
        UnloadRenderTexture(Box);
        UnloadRenderTexture(UIAboveGameplay);
        UnloadRenderTexture(UILeft);
        foreach (var overlay in GameplayOverlays)
            overlay.Dispose();
        ChapterInfo?.Unload();
    }
    
    public void ClearBullets()
    {
        foreach (var obj in BoxObjects)
        {
            if ((obj.Header[0] & RuntimeObject.FlagIsBullet) == RuntimeObject.FlagIsBullet)
            {
                obj.Header[0] |= RuntimeObject.FlagIsCollectableBullet;
                obj.Header[2] = 128;
                obj.CollectableVelocity = -1 * Helper.GetDirection(obj.Position, new Vector2(Player.X, Player.Y));
            }
        }
    }

    void ShowChapterScore()
    {
        ChapterScoreShown = true;
        InChapterDelay = true;
        TimerDisappear = GetTime() + .5f;
    }
    #endregion
    #region Render
    private static StageBackground StageBackgroundObject = new HousesBackground();

    /// <summary>
    /// Raises a named event at the current stage background. Backgrounds ignore events by default; one that
    /// overrides <see cref="StageBackground.OnEvent"/> can react to it. Fired at natural gameplay beats (chapter
    /// start, spell card) below, and public so entity/chapter behaviour can raise its own cues.
    /// </summary>
    public void FireBackgroundEvent(string name, float value = 0f) => StageBackgroundObject.OnEvent(name, value);
    private static Rgba Transparent = Rgba.Black with { A = 0 };
    public List<GameplayScreenEffect> ScreenEffects = new();
    public TargetHandle Background, Box, UIAboveGameplay, UILeft;
    // Android/Switch bring-up: bracket the first few real RenderBox passes (the heavy render-target + shader
    // gameplay draw, which the loading screen delays past the frame-count brackets in GameplayScreen). A native
    // GL fault here leaves no managed stack, so the last line logged is the offending section. No-op elsewhere.
    private static int _rbTrace;
    private static void RbTrace(string s)
    {
#if ANDROID || SWITCH
        if (_rbTrace < 3) Utils.Platform.Trace("[rb] " + s);
#endif
    }

    public void RenderBox()
    {
        RbTrace("enter");
        float time = GetTime();
        int typeI = ChapterInfo != null ? (int)ChapterInfo!.Type : 0;
        float tickDelta = GetTime() - (CurrentTick / TargetTPS);
        foreach (var overlay in GameplayOverlays)
            overlay.Update();
        StageBackgroundObject.Draw(Background, CurrentTick, tickDelta);
        BeginTextureMode(Background);
        if (typeI == 3 && !InChapterDelay)
        {
            // Low graphics turns off the spell-card shader (the heaviest per-pixel pass): the card background
            // still draws, just without the animated shader on top.
            bool useSpellShader = ChapterInfo!.ApplyShader && Configuration.Config.HighGraphics;
            if (useSpellShader)
            {
                SetShaderValue(ChapterInfo.SpellShader!.Value, ChapterInfo.LocPosition, [192f, 96f], UniformType.Vec2);
                SetShaderValue(ChapterInfo.SpellShader!.Value, ChapterInfo.LocTime, GetTime() / 8, UniformType.Float);
                // Bind the tiled pizza overlay for shaders that use it (spellcard_nikitab). Shaders without a
                // "pizza" sampler get location -1 and this is a no-op, so it is safe to bind unconditionally.
                SetShaderValueTexture(ChapterInfo.SpellShader!.Value,
                    GetShaderLocation(ChapterInfo.SpellShader!.Value, "pizza"),
                    Runtime.CurrentRuntime.Textures["pizza.png"]);
                BeginShaderMode(ChapterInfo.SpellShader.Value);
            }
            DrawTexture(ChapterInfo!.SpellcardTexture!.Value, 0,0,Rgba.White);
            if (useSpellShader)
                EndShaderMode();
        }
        EndTextureMode();
        RbTrace("background target done");
        BeginTextureMode(Box);
        ClearBackground(Transparent);
        Player.Draw();
        RbTrace("player drawn");
        foreach (var obj in BoxObjects)
        {
            if ((obj.Header[0] & RuntimeObject.FlagIsLaser) == RuntimeObject.FlagIsLaser)
            {
                DrawLaser(obj);
                continue;
            }
            if ((obj.Header[0] & RuntimeObject.FlagIsRay) == RuntimeObject.FlagIsRay)
            {
                DrawRay(obj);
                continue;
            }
            #if DEBUG
            if(IsKeyDown(KeyCode.A))
                DrawRectangle((int)(obj.TargetRectangle.X-obj.Origin.X), (int)(obj.TargetRectangle.Y-obj.Origin.X), (int)obj.TargetRectangle.Width,
                    (int)obj.TargetRectangle.Height, Rgba.Magenta with {A = 64});
            #endif
            if ((obj.Header[0] & RuntimeObject.FlagApplyShader) == RuntimeObject.FlagApplyShader)
            {
                SetShaderValue(obj.Shader, obj.Header[0x40], obj.CreatedAt, UniformType.Int);
                SetShaderValue(obj.Shader, obj.Header[0x41], CurrentTick, UniformType.Int);
                SetShaderValue(obj.Shader, obj.Header[0x42], obj.TexturePosition, UniformType.Vec2); //3
                SetShaderValue(obj.Shader, obj.Header[0x43], obj.TextureSize, UniformType.Vec2); //6,32
                SetShaderValue(obj.Shader, obj.Header[0x44], obj.TotalTextureSize, UniformType.Vec2); //128
                SetShaderValue(obj.Shader, obj.Header[0x45], obj.Header[2], UniformType.Int); 
                BeginShaderMode(obj.Shader);
            }

            float bulletDV = (obj.Header[0] & RuntimeObject.FlagIsBullet) == RuntimeObject.FlagIsBullet ?
                MathF.PI / 2 : 0;
            // Render scale: any entrance scale (bosses) times a "bloom" for a freshly spawned bullet — it grows
            // in from nothing over its first few ticks, overshooting slightly, so bullets bloom into view.
            float renderScale = obj.EntranceScale;
            if ((obj.Header[0] & RuntimeObject.FlagIsBullet) == RuntimeObject.FlagIsBullet)
            {
                int age = CurrentTick - obj.Header[0x17];
                if (age >= 0 && age < BulletBloomTicks)
                    renderScale *= EaseOutBack(age / (float)BulletBloomTicks);
            }
            Rect drawDest = obj.TargetRectangle with { X = obj.TargetRectangle.X + obj.FloatingPoints[0x20] * tickDelta, Y = obj.TargetRectangle.Y + obj.FloatingPoints[0x21] * tickDelta };
            Vector2 drawOrigin = obj.Origin;
            if (renderScale != 1f)   // scale about the centre for spawn / entrance / bloom animations
            {
                drawDest = drawDest with { Width = drawDest.Width * renderScale, Height = drawDest.Height * renderScale };
                drawOrigin *= renderScale;
            }
            Rgba tint = obj.RenderAlpha >= 1f ? Rgba.White : Rgba.White with { A = (byte)Math.Clamp(obj.RenderAlpha * 255f, 0f, 255f) };
            DrawTexturePro(
                obj.Texture,
                obj.SourceRectangle,
                drawDest,
                drawOrigin, (obj.RenderRotation + bulletDV) * 180 / MathF.PI + obj.FloatingPoints[0x23]*tickDelta   , tint
            );
            EndShaderMode();
        }
        float appear2 = MathF.Pow((float)Helper.ComputeObjectTimeStart(time,ChapterTitleAppear+1, 1),6);
        Player.Weapon.DrawTopLayer();
        EndTextureMode();
        RbTrace("box target (objects) done");
        BeginTextureMode(UIAboveGameplay);
        float appearTimer = (float)Helper.ComputeObjectTime(time,TimerAppear, .5f, TimerDisappear, .5);
        ClearBackground(Transparent);

        // Where the spell card's name actually lands this frame. It slides in and scales down, so the score
        // line below it has to be positioned from these values rather than recomputed — otherwise it drifts
        // away from the title during the animation.
        Vector2 titlePosition = Vector2.Zero;
        float titleDrawnHeight = 0;
        float titleRightEdge = 0;

        if (RenderChapterTitle)
        {
            float appear1 = MathF.Pow((float)Helper.ComputeObjectTimeStart(time,ChapterTitleAppear, 1),2);
            float appear3 = (float)Helper.ComputeObjectTimeStart(time,ChapterTitleDisappear, 1);
            float scaling = (1 - appear1) * 9 + 1;
            titlePosition = new Vector2(
                UIAboveGameplay.Texture.Width - (scaling * (1-appear3) * ChapterInfo!.ChapterTitleTexture!.Value.Texture.Width),
                120 * Runtime.CurrentRuntime.ScaleF * (0.075f+1-appear2));
            titleDrawnHeight = ChapterInfo!.ChapterTitleTexture!.Value.Texture.Height * scaling;
            titleRightEdge = titlePosition.X + ChapterInfo!.ChapterTitleTexture!.Value.Texture.Width * scaling;

            DrawTextureEx(ChapterInfo!.ChapterTitleTexture!.Value.Texture,
                titlePosition,
                0, scaling,
                Rgba.White with {A = Helper.TimeToTransparency(appear1)});
        }
        foreach (var overlay in GameplayOverlays)
            overlay.DrawOverlay();
        if (RenderBossTitle)
        {
            float sf = Runtime.CurrentRuntime.ScaleF;
            var bossTex = ChapterInfo!.BossTitleTexture!.Value.Texture;
            DrawTexture(bossTex, (int)(sf * 4), (int)(sf * 4), Rgba.White);

            // A row of stars under the antagonist's (boss) title: one per remaining spell of this boss the
            // player has already seen ("unlocked"). Left-aligned beneath the title — slightly smaller than the
            // old row and packed with a much tighter gap. The star shader gives each a scrolling rainbow body.
            int stars = UnlockedRemainingSpells();
            if (stars > 0)
            {
                TextureHandle star = Runtime.CurrentRuntime.Textures["star.png"];
                float starSize = 13 * sf, starGap = 0.5f * sf;
                float starX0 = sf * 4;
                float starY = sf * 4 + bossTex.Height + 2 * sf;
                ShaderHandle starShader = Runtime.CurrentRuntime.Shaders["star"];
                SetShaderValue(starShader, GetShaderLocation(starShader, "time"), (float)Gfx.GetTime(), UniformType.Float);
                SetShaderValue(starShader, GetShaderLocation(starShader, "res"), new Vector2(star.Width, star.Height), UniformType.Vec2);
                SetShaderValue(starShader, GetShaderLocation(starShader, "alpha"), 1f, UniformType.Float);
                BeginShaderMode(starShader);
                for (int i = 0; i < stars; i++)
                {
                    float starX = starX0 + i * (starSize + starGap);
                    DrawTexturePro(star, new Rect(0, 0, star.Width, star.Height),
                        new Rect(starX, starY, starSize, starSize), Vector2.Zero, 0, Rgba.White);
                }
                EndShaderMode();
            }
        }
        // Ease the timer toward its target height: dropped by ~1.5 of its own height during a spell card,
        // back at the top for a non-spell boss chapter. Eased per-second (frame-rate independent) so the move
        // animates smoothly on a chapter change.
        float timerDropTarget = (typeI == 3 && !InChapterDelay) ? Helper.TimerTextureSize.Y * 1.5f : 0f;
        float timerDt = Math.Clamp(time - LastTimerDropTime, 0f, 0.1f);
        LastTimerDropTime = time;
        TimerDrop += (timerDropTarget - TimerDrop) * Math.Clamp(timerDt * 8f, 0f, 1f);
        if(typeI > 1 && !InChapterDelay)
        {
            // Re-prepared here rather than up in the RenderBox prologue: the overlay loop above (including
            // ToiletTimerOverlay) may have redrawn the shared timer texture with its own countdown text since
            // then, so this has to be the last write before the blit below.
            Helper.PrepareTimer(ChapterInfo!.TickStart + ChapterInfo!.Length - CurrentTick - TickOffset);
            Helper.DrawTimer(
                (int)((UIAboveGameplay.Texture.Width - Helper.TimerTextureSize.X) / 2f),   // horizontally centered
                (int)(-(1 - appearTimer) * Helper.TimerTextureSize.Y + TimerDrop),           // slides in, dropped during spells
                (ChapterInfo.TickStart + ChapterInfo!.Length - CurrentTickWithOffset) < (ChapterInfo!.Length > 600 ? 300 : 600));
        }
        if (IsDialogActive)
            Dialog!.Draw(UIAboveGameplay);
        EndTextureMode();
        if (RenderChapterTitle)
        {
            (int total, int success) = GetSpellcardRecord(ChapterInfo!.SpellcardTitle);
            // Fades/slides in about 0.6s after the name starts appearing, over ~0.5s, so it settles in a beat
            // behind the title rather than popping in with it.
            float subtitleAppear = (float)Helper.ComputeObjectTimeStart(time, ChapterTitleAppear + 0.6f, 0.5);
            // ...and fades back OUT with the title once the card ends (ChapterTitleDisappear is stamped in
            // ShowChapterScore). Without this the bonus/attempt/score info hung on screen through the whole
            // between-chapters delay after the card was already over.
            float subtitleVisible = subtitleAppear *
                (1f - (float)Helper.ComputeObjectTimeStart(time, ChapterTitleDisappear, 0.5));
            // Hangs off the bottom-RIGHT of the name, following it as it slides and scales in.
            Helper.DrawSpellSubtitle(UIAboveGameplay, IsFailed ? -1 : ChapterScoreCurrent, total, success,
                (int)titleRightEdge, (int)(titlePosition.Y + titleDrawnHeight), SpellFailedText, subtitleVisible);
        }
        RbTrace("ui-above target done");
        if (IsUIUpdateRequired)
        {
            RbTrace("RedrawUI enter");
            RedrawUI();
            RbTrace("RedrawUI done");
            IsUIUpdateRequired = false;
        }
        RbTrace("exit");
        _rbTrace++;
#if DEBUG
        DebugStrings.Add($"PauseTimestamp: {PauseTimestamp}");
        DebugStrings.Add($"CountTimeFrom: {CountTimeFrom}");
        DebugStrings.Add($"Box Time: {GetTime()}");
        DebugStrings.Add($"Raylib Time: {GetTime()}");
#endif
    }
    #endregion
    #region UI
    static Rect HeartBombSource = new Rect(0, 0, 96, 96);
    public Rect ScoreSrc, ScoreDest, HiScoreSrc, HiScoreDest;
    public float ChapterTitleAppear = 0;
    public float ChapterTitleDisappear = float.MaxValue;
    public float TimerAppear = 0;
    public float TimerDisappear = float.MaxValue;
    // While a spell card is active the timer slides down (so it clears the spell name at the top); non-spell
    // boss chapters keep it at its usual height. TimerDrop is the eased current offset; LastTimerDropTime is the
    // game-time of the previous frame, for a frame-rate-independent ease.
    private float TimerDrop = 0;
    private float LastTimerDropTime = 0;
    /// <summary>How many ticks a freshly spawned bullet takes to bloom up to its full size.</summary>
    private const int BulletBloomTicks = 8;

    /// <summary>Ease-out-back: 0 → overshoots past 1 → settles at 1. The little overshoot makes bullets pop as
    /// they bloom in rather than just fading up.</summary>
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float t = x - 1f;
        return 1f + c3 * t * t * t + c1 * t * t;
    }
    public int MaxScoreContinue = 0;
    public int MaxScore = 100000;
    public bool IsUIUpdateRequired = false;
    TargetHandle HiScoreTexture = Helper.CreateScoreText("1.000.000", 16);
    TargetHandle ScoreTexture = Helper.CreateScoreText("1.000.000", 16);
    Rect HeartBombDest = new(0, 0, new Vector2(12 * Runtime.CurrentRuntime.ScaleF));
    TextureHandle StaffTexture = Runtime.CurrentRuntime.Textures["ingame-stuff.png"];
    float BombsY = 135 * Runtime.CurrentRuntime.ScaleF;
    float SizeOfRes = 12 * Runtime.CurrentRuntime.ScaleF;
    float ResX = 206 * Runtime.CurrentRuntime.ScaleF;
    float HeartsY = 97 * Runtime.CurrentRuntime.ScaleF;
    int hiScore = 0;
    int score = 0;
    int scoreTarget;
    /// <summary>Fallback score awarded for killing a non-boss enemy that has no authored "score when killed" value.</summary>
    const int EnemyKillScore = 10000;
    byte Continue = 0;
    bool RenderChapterTitle = false;
    bool RenderBossTitle = false;
    
    public int ChapterTick => CurrentTick + TickOffset - ChapterInfo.TickStart;
    public int CurrentTickWithOffset => CurrentTick + TickOffset;

    /// <summary>The run's current score (read-only view for the post-clear results screen).</summary>
    public int FinalScore => score;
    /// <summary>How many continues the player has used this run; 0 means a no-continue clear.</summary>
    public int ContinuesUsed => Continue;
    
    int Score
    {
        get => score;
        set
        {
            if (score == value)
                return;
            score = value;
            UpdateUI();
        }
    }
    
    public int ScoreTarget
    {
        get => scoreTarget;
        set
        {
            scoreTarget = value;
        }
    }
    
    #region Dialogs

    /// <summary>The conversation currently on screen, if any.</summary>
    public RuntimeDialog? Dialog;

    private bool isDialogActive;

    public bool IsDialogActive
    {
        get => isDialogActive;
        // The dialog no longer freezes the clock, so this is a plain flag now — the simulation keeps running
        // while the conversation is on screen.
        private set => isDialogActive = value;
    }

    /// <summary>Opens the chapter's conversation, if it has one. Called as the chapter starts.</summary>
    void StartDialog()
    {
        if (ChapterInfo is not { HasDialogs: true } || ChapterInfo.Dialogs.Length == 0)
            return;

        Dialog?.Unload();
        Dialog = new RuntimeDialog(ChapterInfo.Dialogs, Player.ProtogonistData, this);
        if (Dialog.Finished)
        {
            Dialog = null;
            return;
        }
        IsDialogActive = true;
        MagnetCollectablesToPlayer();
    }

    /// <summary>
    /// Hands the player every collectable left lying on the box. Nothing is dropping during a conversation and
    /// the player is not steering, so loot that is still falling would either be lost to the bottom edge or
    /// (stage 2) eaten by the mystical toilet while they are stuck reading. Flagged homing rather than collected
    /// outright, so the items visibly fly in — and because the flag sticks, ones that do not reach the player
    /// before the dialog ends finish the trip instead of dropping again mid-flight.
    /// </summary>
    void MagnetCollectablesToPlayer()
    {
        // The add queue too: items dropped by the last kill of the chapter we just left are still waiting to be
        // folded into BoxObjects (that happens at the top of the next tick), and they are exactly the loot the
        // player is most likely to still have coming.
        foreach (var obj in BoxObjects.Concat(ObjectsAddQueue))
            if ((obj.Header[0] & RuntimeObject.FlagIsCollectable) == RuntimeObject.FlagIsCollectable)
                obj.Header[0] |= RuntimeObject.FlagHomingCollectable;
    }

    void EndDialog()
    {
        IsDialogActive = false;
        Dialog?.Unload();
        Dialog = null;
        // The spell's time bonus is measured from when the fight actually begins, not from when the boss walked
        // on with the dialog still up. The chapter clock was held during the dialog, so restart the wall-clock
        // stopwatch here to match — otherwise the conversation's length would silently eat into the bonus.
        SpellcardStopwatch?.Restart();
    }

    #endregion

    /// <summary>This player's record on a spell card AT THE CURRENT DIFFICULTY: (attempts, successes). Each
    /// tier keeps its own counters, so a card cleared on Easy does not read as cleared on Max.</summary>
    (int Total, int Success) GetSpellcardRecord(string spellName) =>
        PlayerData.Instance.GetSpellcardRecord(ProtogonistId, Helper.SpellRecordKey(spellName, Difficulty), IsPractice);

    /// <summary>
    /// Bump the record for the spell card that just ended. Captured means the player neither died on it nor
    /// ran the timer out. Nothing in the game wrote these counters before, so every card read back as 00/00
    /// however many times it had been played.
    /// </summary>
    void RecordSpellAttempt(string spellName, bool captured)
    {
        if (IsReplay)
            return;
        string key = Helper.SpellRecordKey(spellName, Difficulty);
        PlayerData.Instance.AddSpellcardTry(ProtogonistId, key, captured, IsPractice);
        // Spell practice plays exactly one card, so the run's score is that card's score — record it as the card's
        // hi-score. (A full run's per-card score isn't isolated, so only practice contributes.)
        if (IsSpellPractice)
            PlayerData.Instance.RecordSpellcardScore(ProtogonistId, key, ScoreTarget);
    }

    /// <summary>
    /// How many of this boss's spell cards the player has already seen in an earlier run ("unlocked" — a
    /// recorded attempt in either normal or practice) and has still to fight in this encounter (the current
    /// chapter onward). Drawn as a row of stars under the spell/non-spell title, shrinking as the boss is
    /// worked through.
    /// </summary>
    private int UnlockedRemainingSpells()
    {
        if (StageInfo == null)
            return 0;
        int count = 0;
        for (int i = ChapterIndex; i < StageInfo.Chapters.Length; i++)
        {
            RuntimeChapter chapter = StageInfo.Chapters[i];
            if (chapter.Type != ChapterType.Spell)
                continue;
            // Records are per difficulty now, so "seen" means tried at ANY tier, in either normal or practice.
            bool seen = false;
            for (int d = 0; d < 5 && !seen; d++)
            {
                string key = Helper.SpellRecordKey(chapter.SpellcardTitle, d);
                seen = PlayerData.Instance.GetSpellcardRecord(ProtogonistId, key, false).Total > 0
                       || PlayerData.Instance.GetSpellcardRecord(ProtogonistId, key, true).Total > 0;
            }
            if (seen)
                count++;
        }
        return count;
    }

    public void UpdateUI()
    {
        IsUIUpdateRequired = true;
    }
    
    /// <summary>
    /// The bottom line of the stats strip: what the mystical toilet is currently holding — its own sprite, then
    /// the number of collectables it has swallowed, right-aligned like every other figure above it. Only drawn
    /// while a toilet is actually on screen, since killing it hands the whole hoard straight back; the row
    /// disappearing IS the message that the items are on their way.
    /// </summary>
    void DrawToiletHoard(float fontSize)
    {
        if (MysticalToilet == null)
            return;
        float sf = Runtime.CurrentRuntime.ScaleF;
        string held = ToiletHoardCount.ToString();
        var size = Helper.GetScoreTextureSize(held, fontSize);
        // Sits a row below the graze figure (y 236), in the strip's empty lower half.
        var position = new Vector2(206, 258) * sf - size;
        Helper.DrawScoreText(held, fontSize, position, Rgba.Violet);
        // The toilet's own sprite as the row's label — the art has no printed caption down here, and its
        // silhouette says what the number counts more directly than a word would.
        var icon = Runtime.CurrentRuntime.Textures["toilet-sprite.png"];
        float iconSize = 16 * sf;
        DrawTexturePro(icon, new Rect(0, 0, 96, 96),
            new Rect(position.X - iconSize - 4 * sf, position.Y + (size.Y - iconSize) / 2, iconSize, iconSize),
            Vector2.Zero, 0, Rgba.White);
    }

    void RedrawUI()
    {
        BeginTextureMode(UILeft);
        ClearBackground(Transparent);
        DrawTextureEx(Runtime.CurrentRuntime.Textures["rightside_info.png"], Vector2.Zero, 0, Runtime.CurrentRuntime.ScaleF/4,Rgba.White);
        DrawText(GetTime()+"", 16, 16, 24, Rgba.Red);
        for (int i = 0; i < 8; i++)
        {
            DrawTexturePro(StaffTexture, HeartBombSource with { Y = 96, 
                    X = 
                    i > Player.Bombs ? 384 : 
                    i < Player.Bombs ? 0 :
                    384 - (Player.BombsSpices * 96)
                },
                HeartBombDest with {Y = BombsY, X = ResX - SizeOfRes * (8-i) },
                Vector2.Zero, 0, Rgba.White);
            DrawTexturePro(StaffTexture, HeartBombSource with {  X = 
                    i > Player.HeartPoints ? 384 : 
                    i < Player.HeartPoints ? 0 :
                    384 - (Player.HeartSpices * 96)
                },
                HeartBombDest with {Y = HeartsY, X = ResX - SizeOfRes * (8-i) },
                Vector2.Zero, 0, Rgba.White);
        }
        const float fontSizeBig = 22;
        const float fontSizeSmall = 12;
        string scoreString = Helper.FormatScore(score, Continue);
        string hiScoreString = score > MaxScore ? scoreString : Helper.FormatScore(MaxScore, MaxScoreContinue);
        var positionHiScore = new Vector2(206, 64) * Runtime.CurrentRuntime.ScaleF - Helper.GetScoreTextureSize(hiScoreString,fontSizeBig);
        var positionScore = new Vector2(206, 86) * Runtime.CurrentRuntime.ScaleF - Helper.GetScoreTextureSize(scoreString, fontSizeBig);
        Helper.DrawScoreText(hiScoreString, fontSizeBig, positionHiScore, Rgba.LightGray);
        Helper.DrawScoreText(scoreString, fontSizeBig, positionScore, Rgba.White);
        Helper.DrawScoreText($"{Player.HeartSpices}/5", fontSizeSmall, new Vector2(175, 112) * Runtime.CurrentRuntime.ScaleF, Rgba.White);
        Helper.DrawScoreText($"{Player.BombsSpices}/5", fontSizeSmall, new Vector2(175, 152) * Runtime.CurrentRuntime.ScaleF, Rgba.White);
        var sizeH = Helper.GetScoreTextureSize("..", fontSizeBig);
        var sizeH2 = Helper.GetScoreTextureSize("...", fontSizeBig);
        var sizeL = Helper.GetScoreTextureSize("..", fontSizeSmall);
        var posPower1 = new Vector2(206, 200) * Runtime.CurrentRuntime.ScaleF -
                        new Vector2(sizeL.X * 2 + sizeH.X + sizeH2.X, sizeH.Y);
        Helper.DrawScoreText($"{Player.Power/100}.", fontSizeBig, posPower1, Rgba.Orange);
        Helper.DrawScoreText($"{Player.Power%100:00}", fontSizeSmall, posPower1 + new Vector2(sizeH.X, (sizeH.Y-sizeL.Y) * 0.8f), Rgba.Orange);
        Helper.DrawScoreText($"/4.", fontSizeBig, posPower1+new Vector2(sizeH.X+sizeL.X, 0), Rgba.Orange);
        Helper.DrawScoreText($"00", fontSizeSmall, posPower1 + new Vector2(sizeH.X+sizeH2.X+sizeL.X, (sizeH.Y-sizeL.Y) * 0.8f), Rgba.Orange);
        Helper.DrawScoreText($"10000", fontSizeBig, new Vector2(206, 218) * Runtime.CurrentRuntime.ScaleF - 
                                                    Helper.GetScoreTextureSize("10000", fontSizeBig), Rgba.SkyBlue);
        Helper.DrawScoreText(Player.Graze.ToString(), fontSizeBig, new Vector2(206, 236) * Runtime.CurrentRuntime.ScaleF -
                                                                   Helper.GetScoreTextureSize(Player.Graze.ToString(), fontSizeBig), Rgba.White);
        DrawToiletHoard(fontSizeBig);
        EndTextureMode();
    }
    #endregion
    #region Time
    public double CountTimeFrom = Gfx.GetTime() + 3d;
    float PauseTimestamp = 0;
    float GameoverTimestamp = 0;
    bool isPaused = false;
    bool isGameover = false;

    public float GetTime()
    {
        if (IsGameOver)
            return GameoverTimestamp;
        if (IsPaused)
            return PauseTimestamp;   // the game time frozen at the moment of pause
        // A dialog no longer freezes the clock — the simulation keeps ticking beneath it.
        return (float)(Gfx.GetTime() - CountTimeFrom);
    }
    
    public bool IsPaused
    {
        get => isPaused;
        set
        {
            if (value == isPaused)
                return;
            // Mirror the dialog-freeze pattern below (isDialogActive): read the clock BEFORE freezing it, then
            // resume by shifting the origin. The old code flipped isPaused first and only then read GetTime(),
            // which by then already took the paused branch and returned the not-yet-written PauseTimestamp — so
            // it froze at a large negative value. GameplayScreen.Render's `GetTime() < -.5` intro guard then read
            // the paused frame as "before the stage starts" and skipped drawing it, blanking the gameplay behind
            // the pause menu.
            if (value)
                PauseTimestamp = GetTime();
            else
                CountTimeFrom = Gfx.GetTime() - PauseTimestamp;
            isPaused = value;
            GameplayScreen.Paused = value;
        }
    }

    public bool IsGameOver
    {
        get => isGameover;
        set
        {
            if (value == isGameover)
                return;
            isGameover = value;
            if (value)
            {
                IsPaused = true;
                GameoverTimestamp = GetTime();
                if (!IsReplay)
                    PlayerData.Instance.SetMusicUnlocked(11, true);   // game-over theme
            }
            else if(Continue < MaxContinues)
            {
                IsPaused = false;
                Continue++;
            }
        }
    }
    #endregion

#if DEBUG
    public List<string> DebugStrings = new();
#endif
}
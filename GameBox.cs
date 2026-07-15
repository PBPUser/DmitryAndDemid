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
    public bool IsFailed = false;
    bool SpellTimedOut = false;

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
    bool IsSpellPractice;
    public bool IsPractice;
    bool IsReplay;   // playback (replay viewer / title demo): never mutates the player's progress
    /// <summary>True only for the title-screen attract demo (a subset of replays). Set on the owning screen via
    /// an object-initializer after construction, so read it lazily through here rather than caching it.</summary>
    public bool IsDemo => GameplayScreen.IsDemo;

    int ComputeCurrentTickFromStartingTime => (int)(GetTime() * TargetTPS);
    
    public GameBox(GameplayScreen screen, ProtogonistData data, FileStageInfo[] stages, int chapter, int difficulty, bool isPractice,
        PlayerControllerBase? controller = null, GameType mode = GameType.Default)
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
        LoadStage(stages[0], chapter, difficulty);
        SignalGameplayOverlay = new SignalGameplayOverlay(this);
        AddOverlay(SignalGameplayOverlay);
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

    /// <summary>How long (seconds) a normal last-stage clear takes to fade all gameplay to black.</summary>
    public const float ClearFadeDuration = 4f;

    /// <summary>Set when a normal (non-practice) run reaches the end of the last stage; starts the fade-out.</summary>
    public bool Cleared { get; private set; }
    private float ClearedAt;

    /// <summary>0 before/at the clear, ramping to 1 (fully black) over <see cref="ClearFadeDuration"/>.</summary>
    public float ClearFade => Cleared ? Math.Clamp((GetTime() - ClearedAt) / ClearFadeDuration, 0f, 1f) : 0f;
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

        // A dialog holds the whole simulation: no ticks, no spawns, no timer. GetTime() is frozen with it, so
        // the chapter resumes exactly where it stopped instead of fast-forwarding through the ticks the
        // conversation took.
        if (IsDialogActive)
        {
            Dialog!.Update();
            if (Dialog.Finished)
                EndDialog();
            return;
        }
        if (CurrentTick >= ComputeCurrentTickFromStartingTime)
            return;
        Score = (int)MathUtil.MoveTowards(Score, ScoreTarget, MathF.Max((ScoreTarget - Score) / 30f, 10));
        CurrentTick++;
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
        else
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
                AddOverlay(new TimerGameplayOverlay(this, "bonus-failed.png", ChapterInfo.Length, SpellcardStopwatch!.Elapsed.TotalSeconds, 0.5f, 3f));
                Helper.PlaySound(Runtime.CurrentRuntime.Sounds["fault"]);
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
                obj.Update();
            }
            obj.FloatingPoints[0x20] = obj.FloatingPoints[0x10] - x;
            obj.FloatingPoints[0x21] = obj.FloatingPoints[0x11] - y;
            obj.FloatingPoints[0x22] = obj.FloatingPoints[0x12] - z;
            obj.FloatingPoints[0x23] = obj.FloatingPoints[0x5] - r;
            // Lasers are exempt from the offscreen cull: a beam is commonly anchored at a screen edge (or fired
            // from just outside), and it removes itself when its life ends rather than by leaving the box.
            if ((obj.Header[0] & RuntimeObject.FlagIsLaser) != RuntimeObject.FlagIsLaser &&
                (obj.X < -32 || obj.Y < -32 || obj.X > 416 || obj.Y > 480))
            {
                RemoveObject(obj);
                continue;
            }
            if (InChapterDelay)
                continue;
            if (obj.Health <= 0)
            { 
                if ((obj.Header[0] & RuntimeObject.FlagIsBoss) == RuntimeObject.FlagIsBoss)
                {
                    TickOffset += ChapterInfo.TickStart+ChapterInfo.Length-CurrentTick;
                    int ticks = CurrentTickWithOffset - ChapterInfo.TickStart;
                    SpellcardStopwatch ??= new Stopwatch();
                    if (!IsFailed)
                    {
                        AddOverlay(new ScoreGameplayOverlay(this, ChapterScoreCurrent * 10, ticks,
                            SpellcardStopwatch!.Elapsed.TotalSeconds, .5f, 3));
                        ScoreTarget += ChapterScoreCurrent;
                    }
                    else
                        AddOverlay(new TimerGameplayOverlay(this, "bonus-failed.png", ticks,
                            SpellcardStopwatch!.Elapsed.TotalSeconds, 0.5f, 3f));
                    
                    SpellcardStopwatch = null;
                    obj.UpdateAction = null;
                }
                else
                {
                    obj.Header[0] |= RuntimeObject.FlagIsUsed;
                    obj.Header[0xa] = CurrentTick;
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
        float angle = MathF.PI;
        for (int i = 0; i < drop.DropPower; i++)
        {
            var obj = RuntimeObject.LoadFromFile(RuntimeObject.CollectableFEIs[0], this);
            obj.CollectableVelocity = Helper.GetDirection(angle+=MathF.PI / 6) * (rnd.NextSingle() + .5f);
            obj.Position = position;
            AddObject(obj);
        }
        for (int i = 0; i < drop.DropLargePower; i++)
        {
            var obj = RuntimeObject.LoadFromFile(RuntimeObject.CollectableFEIs[1], this);
            obj.CollectableVelocity = Helper.GetDirection(angle+=MathF.PI / 6) * (rnd.NextSingle() + .5f);
            obj.Position = position;
            AddObject(obj);
        }

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
    
    public void SpawnMysticalToilet()
    {
        if (MysticalToilet != null)
            return;
        var toilet = RuntimeObject.LoadFromFile(RuntimeObject.MagicalToilet, this);
        toilet.MaxHealth = toilet.Health = MathF.Pow(2, Difficulty) * 10 * Player.Signal + 150;
        MysticalToilet = toilet;
        AddObject(MysticalToilet);
        toilet.Header[0x55] = 120 / (Difficulty + 1);
        toilet.X = 192;
        toilet.Y = 64;
        AddOverlay(new MysticalToiletOverlay(this, 0.25f, 3));
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

        // `chapter` used to be accepted and then ignored: NextChapter() always advanced from -1 to 0, so a
        // spell-practice run could only ever start on the stage's first chapter. Start where we were asked to.
        ChapterIndex = chapter - 1;
        NextChapter();
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
                // Normal run reached the end of the last stage (Extra is unimplemented and IsSpellPractice is
                // never set, so getting here already means a Default run). Begin the slow fade-out of all
                // gameplay. Deliberately NOT setting IsPaused/IsGameOver: those freeze GetTime(), and the fade
                // is driven off it.
                if (!IsSpellPractice)
                {
                    Cleared = true;
                    ClearedAt = GetTime();
                }
            }
            return;
        }

        TimerAppear = GetTime();
        TimerDisappear = float.MaxValue;
        ChapterInfo = StageInfo.Chapters[ChapterIndex];
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
        }
        else if (ChapterInfo.Type == ChapterType.NonSpell)
        {
            RenderBossTitle = true;
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
    public void RenderBox()
    {
        float time = GetTime();
        int typeI = ChapterInfo != null ? (int)ChapterInfo!.Type : 0;
        if(typeI > 1 && !InChapterDelay)
            Helper.PrepareTimer(ChapterInfo!.TickStart + ChapterInfo!.Length - CurrentTick - TickOffset);
        float tickDelta = GetTime() - (CurrentTick / TargetTPS);
        foreach (var overlay in GameplayOverlays)
            overlay.Update();
        StageBackgroundObject.Draw(Background, CurrentTick, tickDelta);
        BeginTextureMode(Background);
        if (typeI == 3 && !InChapterDelay)
        {
            if (ChapterInfo!.ApplyShader)
            {
                SetShaderValue(ChapterInfo.SpellShader!.Value, ChapterInfo.LocPosition, [192f, 96f], UniformType.Vec2);
                SetShaderValue(ChapterInfo.SpellShader!.Value, ChapterInfo.LocTime, GetTime() / 8, UniformType.Float);
                BeginShaderMode(ChapterInfo.SpellShader.Value);
            }
            DrawTexture(ChapterInfo!.SpellcardTexture!.Value, 0,0,Rgba.White);
            EndShaderMode();
        }
        EndTextureMode();
        BeginTextureMode(Box);
        ClearBackground(Transparent);
        Player.Draw();
        foreach (var obj in BoxObjects)
        {
            if ((obj.Header[0] & RuntimeObject.FlagIsLaser) == RuntimeObject.FlagIsLaser)
            {
                DrawLaser(obj);
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
            DrawTexturePro(
                obj.Texture,
                obj.SourceRectangle,
                obj.TargetRectangle with { X = obj.TargetRectangle.X + obj.FloatingPoints[0x20] * tickDelta, Y = obj.TargetRectangle.Y + obj.FloatingPoints[0x21] * tickDelta },
                obj.Origin, (obj.RenderRotation + bulletDV) * 180 / MathF.PI + obj.FloatingPoints[0x23]*tickDelta   , Rgba.White
            );
            EndShaderMode();
        }
        float appear2 = MathF.Pow((float)Helper.ComputeObjectTimeStart(time,ChapterTitleAppear+1, 1),6);
        Player.Weapon.DrawTopLayer();
        EndTextureMode();
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
        if(typeI > 1 && !InChapterDelay)
            Helper.DrawTimer(
                (int)((UIAboveGameplay.Texture.Width - Helper.TimerTextureSize.X) / 2f),   // horizontally centered
                (int)(-(1 - appearTimer) * Helper.TimerTextureSize.Y),                       // slides down into place
                (ChapterInfo.TickStart + ChapterInfo!.Length - CurrentTickWithOffset) < (ChapterInfo!.Length > 600 ? 300 : 600));
        if (IsDialogActive)
            Dialog!.Draw(UIAboveGameplay);
        EndTextureMode();
        if (RenderChapterTitle)
        {
            (int total, int success) = GetSpellcardRecord(ChapterInfo!.SpellcardTitle);
            // Fades/slides in about 0.6s after the name starts appearing, over ~0.5s, so it settles in a beat
            // behind the title rather than popping in with it.
            float subtitleAppear = (float)Helper.ComputeObjectTimeStart(time, ChapterTitleAppear + 0.6f, 0.5);
            // Hangs off the bottom-RIGHT of the name, following it as it slides and scales in.
            Helper.DrawSpellSubtitle(UIAboveGameplay, IsFailed ? -1 : ChapterScoreCurrent, total, success,
                (int)titleRightEdge, (int)(titlePosition.Y + titleDrawnHeight), SpellFailedText, subtitleAppear);
        }
        if (IsUIUpdateRequired)
        {
            RedrawUI();
            IsUIUpdateRequired = false;
        }
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
    private float DialogTimestamp;

    public bool IsDialogActive
    {
        get => isDialogActive;
        private set
        {
            if (value == isDialogActive)
                return;
            if (value)
                // Read the clock before freezing it — GetTime() answers with DialogTimestamp from here on.
                DialogTimestamp = GetTime();
            else
                // Put the clock back where the dialog froze it: GetTime() is Gfx.GetTime() - CountTimeFrom.
                CountTimeFrom = Gfx.GetTime() - DialogTimestamp;
            isDialogActive = value;
        }
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
    }

    void EndDialog()
    {
        IsDialogActive = false;
        Dialog?.Unload();
        Dialog = null;
    }

    #endregion

    /// <summary>This player's record on a spell card: (attempts, successes). Zeroes if never tried.</summary>
    (int Total, int Success) GetSpellcardRecord(string spellName) =>
        PlayerData.Instance.GetSpellcardRecord(ProtogonistId, spellName, IsPractice);

    /// <summary>
    /// Bump the record for the spell card that just ended. Captured means the player neither died on it nor
    /// ran the timer out. Nothing in the game wrote these counters before, so every card read back as 00/00
    /// however many times it had been played.
    /// </summary>
    void RecordSpellAttempt(string spellName, bool captured)
    {
        if (IsReplay)
            return;
        PlayerData.Instance.AddSpellcardTry(ProtogonistId, spellName, captured, IsPractice);
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
            bool seen = PlayerData.Instance.GetSpellcardRecord(ProtogonistId, chapter.SpellcardTitle, false).Total > 0
                        || PlayerData.Instance.GetSpellcardRecord(ProtogonistId, chapter.SpellcardTitle, true).Total > 0;
            if (seen)
                count++;
        }
        return count;
    }

    public void UpdateUI()
    {
        IsUIUpdateRequired = true;
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
            return (float)(PauseTimestamp - CountTimeFrom);
        if (isDialogActive)
            return DialogTimestamp;
        return (float)(Gfx.GetTime() - CountTimeFrom);
    }
    
    public bool IsPaused
    {
        get => isPaused;
        set
        {
            if (value == isPaused)
                return;
            isPaused = value;
            GameplayScreen.Paused = value;
            if(value)
                PauseTimestamp = (float)GetTime();
            else
                CountTimeFrom += GetTime() - PauseTimestamp;
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
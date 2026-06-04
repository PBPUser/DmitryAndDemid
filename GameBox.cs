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
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid;

public class GameBox : IDisposable
{
    public RuntimeStageInfo? StageInfo;
    public RuntimeChapter? ChapterInfo;
    public string ProtogonistId;
    public int Difficulty;
    public Player Player;
    private GameplayScreen GameplayScreen;
    bool Practice;
    private bool SpellPractice;
    private SignalGameplayOverlay SignalGameplayOverlay;
    
    public GameBox(GameplayScreen screen, ProtogonistData data, FileStageInfo[] stages, int chapter, int difficulty, bool practice)
    {
        GameplayScreen = screen;
        Practice = practice;
        Player = new Player(this, data, new PlayerController());
        ProtogonistId = data.ID;
        Difficulty = difficulty;
        Stages = stages;
        PauseTimestamp = (float)(Raylib.GetTime() + 3);
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
        UpdateUI();
    }

    private int StageIndex = 0;
    private FileStageInfo[] Stages;
    public const float TargetTPS = 60;
    private float TickLength = 1f / TargetTPS;
    private bool RequiresRefresh = false;
    public bool IsFailed = false;

    private List<RuntimeObject> 
        ObjectsAddQueue = new(),
        ObjectsRemoveQueue = new();

    List<GameplayScreenEffect>
        ScreenEffectsToAdd = new(),
        ScreenEffectsToRemove = new();

    public List<RuntimeObject>  BoxObjects = new();

    public List<GameplayOverlay> 
        GameplayOverlays = new();

    List<GameplayOverlay> 
        GameplayOverlaysToAdd = new(),
        GameplayOverlaysToRemove = new();

    public int CurrentTick = 0;
    public bool InChapterDelay = false;
    private int CurrentTickCompute => (int)(GetTime() * TargetTPS);
    public int TickOffset = 0;
    
    #region Update

    public const int DelayBetweenChapters = 120;
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
        if (CurrentTick >= CurrentTickCompute)
            return;
        Score = (int)Raymath.MoveTowards(Score, ScoreTarget, MathF.Max((ScoreTarget - Score) / 30f, 10));
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
            }
        }
        #if DEBUG
        if (IsKeyDown(KeyboardKey.LeftControl))
        {
            if(IsKeyDown(KeyboardKey.B))
                if(!GameplayOverlays.Any(x => x is ScoreGameplayOverlay && GetTime() - x.TimeAppear < 0.5))
                    AddOverlay(new ScoreGameplayOverlay(this, GetRandomValue(0, int.MaxValue), 600, 1.4, .5f, 3f));
        }
        else if (IsKeyDown(KeyboardKey.RightShift) && CurrentTickCompute % TargetTPS == 0)
        {
            if(IsKeyDown(KeyboardKey.L))
                SpawnMysticalToilet();
            if (IsKeyDown(KeyboardKey.P))
            {
                var obj = RuntimeObject.LoadFromFile(RuntimeObject.CollectableFEIs[0], this);
                obj.X = Player.X;
                obj.Y = 64;
                AddObject(obj);
            }
            if (IsKeyDown(KeyboardKey.D))
                Player.HeartPoints++;
            if (IsKeyDown(KeyboardKey.F))
                Player.HeartPoints--;
            if (IsKeyDown(KeyboardKey.G))
                Player.HeartSpices++;
            if (IsKeyDown(KeyboardKey.H))
                Player.HeartSpices--;
            if(IsKeyDown(KeyboardKey.L))
                UpdateUI();
            if (IsKeyDown(KeyboardKey.Z))
                DarkStrengthPos = new Vector2(Player.X, Player.Y);
            if (IsKeyDown(KeyboardKey.X))
                if (!ScreenEffects.Any(x => x is DarkStrengthScreenEffect && GetTime() - x.TimeAppear < 1.5))
                {
                    var pos = new Vector2(Player.X, Player.Y);
                    var offset = DarkStrengthPos - pos;
                    offset *= new Vector2(Raymath.Sign(offset.X), Raymath.Sign(offset.Y));
                    AddScreenEffect(new DarkStrengthScreenEffect(this, offset, pos, 0b0000_1111, 20, GetTime(), GetTime()+2f));
                }
            if(IsKeyDown(KeyboardKey.C))
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
            if (obj.X < -32 || obj.Y < -32 || obj.X > 416 || obj.Y > 480)
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
                    if (Raymath.Vector2Distance(obj.Position, obj2.Position) <
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
            if(!Player.CollisionEnabled)
                continue;
            if ((bitMask & RuntimeObject.FlagDangerousRelatedToPlayer) ==
                RuntimeObject.FlagDangerousRelatedToPlayer)
            {
                var distance = Raymath.Vector2Distance(new(Player.X, Player.Y), obj.Position);
                var collision = Player.CollisionRadius + obj.CollisionScale * obj.FloatingPoints[0x13];
                if (distance < collision / 2)
                {
                    Player.Die();
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
                    // TODO: Play boss death
                    obj.Header[0] |= RuntimeObject.FlagIsDied;
                    AddScreenEffect(new BossDeathScreenEffect(this, obj.Position, 45, GetTime(), GetTime()+2f));
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
        PlayerData.Instance.SetStageUnlocked(stage.Header[1], true);
        StageInfo = RuntimeStageInfo.LoadFromFile(stage, difficulty, this);
        AddOverlay(new StageTitleOverlay(this, stage.Header[1]) { TimeAppear = GetTime() + 5f });
        NextChapter();
    }

    public void NextChapter()
    {
        ChapterIndex++;
        ChapterInfo?.Unload();
        if (StageInfo!.Chapters.Length <= ChapterIndex)
        {
            if (Practice)
            {
                IsGameOver = true;
            }
            else
            {
                
            }
            return;
        }

        TimerAppear = GetTime();
        TimerDisappear = float.MaxValue;
        ChapterInfo = StageInfo.Chapters[ChapterIndex];
        ChapterScoreShown = false;
        RenderChapterTitle = false;
        RenderBossTitle = false;
        if (ChapterInfo.Type == ChapterType.Spell)
        {
            // TODO: Play SpellCard Sound
            SpellcardStopwatch = new Stopwatch();
            SpellcardStopwatch.Start();
            RenderBossTitle = true;
            RenderChapterTitle = true;
            ChapterTitleAppear = GetTime();
            ChapterTitleDisappear = float.MaxValue;
            IsFailed = false;
            ChapterScoreCurrent = ScoreChapterMax = ChapterInfo.MaxScore;
            AddScreenEffect(new SpellCardAttackScreenEffect(this, Vector2.Zero, 0, GetTime(), GetTime()+2));
        }
        else if (ChapterInfo.Type == ChapterType.NonSpell)
        {
            RenderBossTitle = true;
        }
        InChapterDelay = false;
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
        TimerDisappear = GetTime()+.5f;
    }

    void ChapterEnd()
    {
        
    }
    #endregion
    #region Render
    private static StageBackground StageBackgroundObject = new DrogichinBackground();
    private static Color Transparent = Color.Black with { A = 0 };
    public List<GameplayScreenEffect> ScreenEffects = new();
    public RenderTexture2D Background;
    public RenderTexture2D Box;
    public RenderTexture2D UIAboveGameplay;
    public RenderTexture2D UILeft;
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
                SetShaderValue(ChapterInfo.SpellShader!.Value, ChapterInfo.LocPosition, [192f, 96f], ShaderUniformDataType.Vec2);
                SetShaderValue(ChapterInfo.SpellShader!.Value, ChapterInfo.LocTime, GetTime() / 8, ShaderUniformDataType.Float);
                BeginShaderMode(ChapterInfo.SpellShader.Value);
            }
            DrawTexture(ChapterInfo!.SpellcardTexture!.Value, 0,0,Color.White);
            EndShaderMode();
        }
        EndTextureMode();
        BeginTextureMode(Box);
        ClearBackground(Transparent);
        Player.Draw();
        foreach (var obj in BoxObjects)
        {
            #if DEBUG
            if(IsKeyDown(KeyboardKey.A))
                DrawRectangle((int)(obj.TargetRectangle.X-obj.Origin.X), (int)(obj.TargetRectangle.Y-obj.Origin.X), (int)obj.TargetRectangle.Width,
                    (int)obj.TargetRectangle.Height, Color.Magenta with {A = 64});
            #endif
            if ((obj.Header[0] & RuntimeObject.FlagApplyShader) == RuntimeObject.FlagApplyShader)
            {
                SetShaderValue(obj.Shader, obj.Header[0x40], obj.CreatedAt, ShaderUniformDataType.Int);
                SetShaderValue(obj.Shader, obj.Header[0x41], CurrentTick, ShaderUniformDataType.Int);
                SetShaderValue(obj.Shader, obj.Header[0x42], obj.TexturePosition, ShaderUniformDataType.Vec2); //3
                SetShaderValue(obj.Shader, obj.Header[0x43], obj.TextureSize, ShaderUniformDataType.Vec2); //6,32
                SetShaderValue(obj.Shader, obj.Header[0x44], obj.TotalTextureSize, ShaderUniformDataType.Vec2); //128
                SetShaderValue(obj.Shader, obj.Header[0x45], obj.Header[2], ShaderUniformDataType.Int);
                BeginShaderMode(obj.Shader);
            }

            float bulletDV = (obj.Header[0] & RuntimeObject.FlagIsBullet) == RuntimeObject.FlagIsBullet ?
                MathF.PI / 2 : 0;
            DrawTexturePro(
                obj.Texture,
                obj.SourceRectangle,
                obj.TargetRectangle with { X = obj.TargetRectangle.X + obj.FloatingPoints[0x20] * tickDelta, Y = obj.TargetRectangle.Y + obj.FloatingPoints[0x21] * tickDelta },
                obj.Origin, (obj.RenderRotation + bulletDV) * 180 / MathF.PI + obj.FloatingPoints[0x23]*tickDelta   , Color.White
            );
            EndShaderMode();
        }
        float appear2 = MathF.Pow((float)Helper.ComputeObjectTimeStart(time,ChapterTitleAppear+1, 1),6);
        Player.Weapon.DrawTopLayer();
        EndTextureMode();
        BeginTextureMode(UIAboveGameplay);
        float appearTimer = (float)Helper.ComputeObjectTime(time,TimerAppear, .5f, TimerDisappear, .5);
        ClearBackground(Transparent);
        if (RenderChapterTitle)
        {
            float appear1 = MathF.Pow((float)Helper.ComputeObjectTimeStart(time,ChapterTitleAppear, 1),2);
            float appear3 = (float)Helper.ComputeObjectTimeStart(time,ChapterTitleDisappear, 1);
            float scaling = (1 - appear1) * 9 + 1;
            DrawTextureEx(ChapterInfo!.ChapterTitleTexture!.Value.Texture, 
                new Vector2(
                    UIAboveGameplay.Texture.Width - (scaling * (1-appear3) * ChapterInfo!.ChapterTitleTexture!.Value.Texture.Width),
                    300 * Runtime.CurrentRuntime.ScaleF * (0.075f+1-appear2)
                    ),
                0, scaling,
                Color.White with {A = Helper.TimeToTransparency(appear1)});
            DrawText($"Score max: {ChapterScoreCurrent} of {ScoreChapterMax}", 0, 64, 24, Color.White);
        }
        foreach (var overlay in GameplayOverlays)
            overlay.DrawOverlay();
        if (RenderBossTitle)
            DrawTexture(ChapterInfo!.BossTitleTexture!.Value.Texture, (int)(Runtime.CurrentRuntime.ScaleF * 4),(int)(Runtime.CurrentRuntime.ScaleF * 4),Color.White);
        if(typeI > 1 && !InChapterDelay)
            Helper.DrawTimer((int)(UIAboveGameplay.Texture.Width - (appearTimer)*Helper.TimerTextureSize.X), 0, (ChapterInfo.TickStart + ChapterInfo!.Length - CurrentTickWithOffset) < (ChapterInfo!.Length > 600 ? 300 : 600));
        EndTextureMode();
        if(RenderChapterTitle )
            Helper.DrawSpellSubtitle(UIAboveGameplay, ChapterScoreCurrent, 0, 0, UIAboveGameplay.Texture.Width -  ChapterInfo!.ChapterTitleTexture!.Value.Texture.Width,(int)(300 * Runtime.CurrentRuntime.ScaleF * (0.063f+1-appear2)));
        if (IsUIUpdateRequired)
        {
            RedrawUI();
            IsUIUpdateRequired = false;
        }
        DebugStrings.Add($"PauseTimestamp: {PauseTimestamp}");
        DebugStrings.Add($"CountTimeFrom: {CountTimeFrom}");
        DebugStrings.Add($"Box Time: {GetTime()}");
        DebugStrings.Add($"Raylib Time: {Raylib.GetTime()}");
    }
    #endregion
    #region UI

    public float ChapterTitleAppear = 0;
    public float ChapterTitleDisappear = float.MaxValue;
    public float TimerAppear = 0;
    public float TimerDisappear = float.MaxValue;
    private bool RenderChapterTitle = false;
    private bool RenderBossTitle = false;
    
    private int score = 0;
    private int hiScore = 0;
    public byte Continue = 0;
    public Rectangle ScoreSrc, ScoreDest, HiScoreSrc, HiScoreDest;
    private static Rectangle HeartBombSource = new Rectangle(0, 0, 96, 96);
    private Rectangle HeartBombDest = new(0, 0, new Vector2(12 * Runtime.CurrentRuntime.ScaleF));
    float BombsY = 135 * Runtime.CurrentRuntime.ScaleF;
    float SizeOfRes = 12 * Runtime.CurrentRuntime.ScaleF;
    float ResX = 206 * Runtime.CurrentRuntime.ScaleF;
    float HeartsY = 97 * Runtime.CurrentRuntime.ScaleF;
    private Texture2D StaffTexture = Runtime.CurrentRuntime.Textures["ingame-stuff.png"];
    private RenderTexture2D HiScoreTexture = Helper.CreateScoreText("1.000.000", 16);
    private RenderTexture2D ScoreTexture = Helper.CreateScoreText("1.000.000", 16);
    public bool IsUIUpdateRequired = false;

    public int ChapterTick => CurrentTick + TickOffset - ChapterInfo.TickStart; 
    public int CurrentTickWithOffset => CurrentTick + TickOffset; 
    
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

    private int scoreTarget;
    
    public int ScoreTarget
    {
        get => scoreTarget;
        set
        {
            scoreTarget = value;
        }
    }

    public int MaxScore = 100000;
    public int MaxScoreContinue = 0;

    public void UpdateUI()
    {
        IsUIUpdateRequired = true;
    }
    
    void RedrawUI()
    {
        BeginTextureMode(UILeft);
        ClearBackground(Transparent);
        DrawTextureEx(Runtime.CurrentRuntime.Textures["rightside_info.png"], Vector2.Zero, 0, Runtime.CurrentRuntime.ScaleF/4,Color.White);
        DrawText(Raylib.GetTime()+"", 16, 16, 24, Color.Red);
        for (int i = 0; i < 8; i++)
        {
            DrawTexturePro(StaffTexture, HeartBombSource with { Y = 96, 
                    X = 
                    i > Player.Bombs ? 384 : 
                    i < Player.Bombs ? 0 :
                    384 - (Player.BombsSpices * 96)
                },
                HeartBombDest with {Y = BombsY, X = ResX - SizeOfRes * (8-i) },
                Vector2.Zero, 0, Color.White);
            DrawTexturePro(StaffTexture, HeartBombSource with {  X = 
                    i > Player.HeartPoints ? 384 : 
                    i < Player.HeartPoints ? 0 :
                    384 - (Player.HeartSpices * 96)
                },
                HeartBombDest with {Y = HeartsY, X = ResX - SizeOfRes * (8-i) },
                Vector2.Zero, 0, Color.White);
        }
        const float fontSizeBig = 22;
        const float fontSizeSmall = 12;
        string scoreString = Helper.FormatScore(score, Continue);
        string hiscoreString = score > MaxScore ? scoreString : Helper.FormatScore(MaxScore, MaxScoreContinue);
        var positionHiScore = new Vector2(206, 64) * Runtime.CurrentRuntime.ScaleF - Helper.GetScoreTextureSize(hiscoreString,fontSizeBig);
        var positionScore = new Vector2(206, 86) * Runtime.CurrentRuntime.ScaleF - Helper.GetScoreTextureSize(scoreString, fontSizeBig);
        Helper.DrawScoreText(hiscoreString, fontSizeBig, positionHiScore, Color.LightGray);
        Helper.DrawScoreText(scoreString, fontSizeBig, positionScore, Color.White);
        Helper.DrawScoreText($"{Player.HeartSpices}/5", fontSizeSmall, new Vector2(175, 112) * Runtime.CurrentRuntime.ScaleF, Color.White);
        Helper.DrawScoreText($"{Player.BombsSpices}/5", fontSizeSmall, new Vector2(175, 152) * Runtime.CurrentRuntime.ScaleF, Color.White);
        var sizeH = Helper.GetScoreTextureSize("..", fontSizeBig);
        var sizeH2 = Helper.GetScoreTextureSize("...", fontSizeBig);
        var sizeL = Helper.GetScoreTextureSize("..", fontSizeSmall);
        var posPower1 = new Vector2(206, 200) * Runtime.CurrentRuntime.ScaleF -
                        new Vector2(sizeL.X * 2 + sizeH.X + sizeH2.X, sizeH.Y);
        Helper.DrawScoreText($"{Player.Power/100}.", fontSizeBig, posPower1, Color.Orange);
        Helper.DrawScoreText($"{Player.Power%100:00}", fontSizeSmall, posPower1 + new Vector2(sizeH.X, (sizeH.Y-sizeL.Y) * 0.8f), Color.Orange);
        Helper.DrawScoreText($"/4.", fontSizeBig, posPower1+new Vector2(sizeH.X+sizeL.X, 0), Color.Orange);
        Helper.DrawScoreText($"00", fontSizeSmall, posPower1 + new Vector2(sizeH.X+sizeH2.X+sizeL.X, (sizeH.Y-sizeL.Y) * 0.8f), Color.Orange);
        Helper.DrawScoreText($"10000", fontSizeBig, new Vector2(206, 218) * Runtime.CurrentRuntime.ScaleF - 
                                                    Helper.GetScoreTextureSize("10000", fontSizeBig), Color.SkyBlue);
        Helper.DrawScoreText(Player.Graze.ToString(), fontSizeBig, new Vector2(206, 236) * Runtime.CurrentRuntime.ScaleF - 
                                                                   Helper.GetScoreTextureSize(Player.Graze.ToString(), fontSizeBig), Color.White);
        EndTextureMode();
    }
    #endregion
    #region Time
    public float GetTime()
    {
        if (IsGameOver)
            return GameoverTimestamp;
        if (IsPaused)
            return (float)(PauseTimestamp - CountTimeFrom);
        return (float)(Raylib.GetTime() - CountTimeFrom);
    }

    public double CountTimeFrom = Raylib.GetTime() + 3d;
    private float PauseTimestamp = 0;
    private float GameoverTimestamp = 0;

    private bool isGameover = false;
    private bool isPaused = false;
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
                PauseTimestamp = (float)Raylib.GetTime();
            else
                CountTimeFrom += Raylib.GetTime() - PauseTimestamp;
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
            }
            else if(Continue < 5)
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
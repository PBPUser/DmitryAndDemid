using System.Numerics;
using DmitryAndDemid.Backgrounds;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay;
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
    bool Practice;
    private bool SpellPractice;
    
    public GameBox(GameplayScreen screen, ProtogonistData data, FileStageInfo stage, int chapter, int difficulty, bool practice)
    {
        Practice = practice;
        Player = new Player(this, data, new PlayerController());
        ProtogonistId = data.ID;
        Difficulty = difficulty;
        PauseTimestamp = (float)(Raylib.GetTime() + 3);
        Background = LoadRenderTexture(384, 448);
        Box = LoadRenderTexture(384, 448);
        UIAboveGameplay = LoadRenderTexture(
            (int)(Runtime.CurrentRuntime.ScaleF * 384f),
            (int)(Runtime.CurrentRuntime.ScaleF * 448f)
        );
        LoadStage(stage, chapter, difficulty);
    }

    public const float TargetTPS = 60;
    private float TickLength = 1f / TargetTPS;
    private bool RequiresRefresh = false;

    private List<RuntimeObject> 
        ObjectsAddQueue = new(),
        ObjectsRemoveQueue = new();

    List<GameplayScreenEffect>
        ScreenEffets = new(),
        ScreenEffectsToAdd = new(),
        ScreenEffectsToRemove = new();

    public List<RuntimeObject>  BoxObjects = new();
    public int CurrentTick = 0;
    public bool InChapterDelay = false;
    private int CurrentTickCompute => (int)(GetTime() * TargetTPS);
    
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
    private int ChapterIndex = -1;
    private bool ChapterScoreShown = false;
    public const int Deathlength = 15;
    
    void BoxUpdate()
    {
        if (CurrentTick >= CurrentTickCompute)
            return;
        CurrentTick++;
        if (CurrentTick >= ChapterInfo.TickStart + ChapterInfo.Length)
        {
            if (!ChapterScoreShown)
            {
                ShowChapterScore();
                ClearAll(false);
                ChapterTitleDisappear = GetTime();
            }
            if (CurrentTick >= ChapterInfo.TickStart + ChapterInfo.Length + DelayBetweenChapters)
            {
                NextChapter();
            }
        }
        else
            ChapterInfo.UpdateScript?.Invoke(ChapterInfo);
        if (RequiresRefresh)
        {
            ScreenEffects.AddRange(ScreenEffectsToAdd);
            ScreenEffectsToRemove.RemoveAll(x => ScreenEffectsToRemove.Contains(x));
            BoxObjects.AddRange(ObjectsAddQueue);
            BoxObjects.RemoveAll(x => ObjectsRemoveQueue.Contains(x));
            ObjectsAddQueue.Clear();
            ObjectsRemoveQueue.Clear();
            ScreenEffectsToAdd.Clear();
            ScreenEffectsToRemove.Clear();
            RequiresRefresh = false;
        }

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
            obj.Update();
            obj.FloatingPoints[0x20] = obj.FloatingPoints[0x10] - x;
            obj.FloatingPoints[0x21] = obj.FloatingPoints[0x11] - y;
            obj.FloatingPoints[0x22] = obj.FloatingPoints[0x12] - z;
            obj.FloatingPoints[0x23] = obj.FloatingPoints[0x5] - r;
            if (obj.X < -32 || obj.Y < -32 || obj.X > 416 || obj.Y > 480)
            {
                RemoveObject(obj);
                continue;
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
                        obj2.FloatingPoints[0] -= obj.FloatingPoints[0x20];
                        if (obj2.FloatingPoints[0] <= 0)
                        {
                            obj2.Header[0] |=  RuntimeObject.FlagIsUsed;
                            obj2.Header[0xa] = CurrentTick;
                        }
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
                    Player.Die();
                if ((bitMask & RuntimeObject.FlagIsBullet) != RuntimeObject.FlagIsBullet)
                    continue;
                if ((bitMask & RuntimeObject.FlagIsGrazed) != RuntimeObject.FlagIsGrazed)
                    continue;
                if (distance > collision)
                    continue;
                Player.Graze++;
                obj.Header[0] |= RuntimeObject.FlagIsGrazed;
            }
        }
        Player.Update();
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

    public void ClearAll(bool drop)
    {
        
    }
    
    public void LoadStage(FileStageInfo stage, int chapter, int difficulty)
    {
        StageInfo = RuntimeStageInfo.LoadFromFile(stage, difficulty, this);
        NextChapter();
    }

    public void NextChapter()
    {
        ChapterIndex++;
        if(ChapterInfo!=null)
            ChapterInfo.Unload();
        if (StageInfo.Chapters.Length <= ChapterIndex)
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
            RenderBossTitle = true;
            RenderChapterTitle = true;
            ChapterTitleAppear = GetTime();
            ChapterTitleDisappear = float.MaxValue;
        }
        else if (ChapterInfo.Type == ChapterType.NonSpell)
        {
            RenderBossTitle = true;
        }
        InChapterDelay = false;
        ChapterInfo.CreateScript?.Invoke(ChapterInfo);
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
    public void RenderBox()
    {
        float time = GetTime();
        int typeI = ChapterInfo != null ? (int)ChapterInfo!.Type : 0;
        if(typeI > 1 && !InChapterDelay)
            Helper.PrepareTimer(ChapterInfo!.TickStart + ChapterInfo!.Length - CurrentTick);
        float tickDelta = GetTime() - (CurrentTick / TargetTPS);
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
                BeginShaderMode(obj.Shader);
            }
            DrawTexturePro(
                obj.Texture,
                obj.SourceRectangle,
                obj.TargetRectangle with { X = obj.TargetRectangle.X + obj.FloatingPoints[0x20] * tickDelta, Y = obj.TargetRectangle.Y + obj.FloatingPoints[0x21] * tickDelta },
                obj.Origin, obj.RenderRotation + obj.FloatingPoints[0x23]*tickDelta, Color.White
            );
            EndShaderMode();
        }
        Player.Draw();
        EndTextureMode();
        BeginTextureMode(UIAboveGameplay);
        float appearTimer = (float)Helper.ComputeObjectTime(time,TimerAppear, .5f, TimerDisappear, .5);
        ClearBackground(Transparent);
        if (RenderChapterTitle)
        {
            float appear1 = MathF.Pow((float)Helper.ComputeObjectTimeStart(time,ChapterTitleAppear, 1),2);
            float appear2 = MathF.Pow((float)Helper.ComputeObjectTimeStart(time,ChapterTitleAppear+1, 1),6);
            float appear3 = (float)Helper.ComputeObjectTimeStart(time,ChapterTitleDisappear, 1);
            float scaling = (1 - appear1) * 9 + 1;
            DrawTextureEx(ChapterInfo!.ChapterTitleTexture!.Value.Texture, 
                new Vector2(
                    UIAboveGameplay.Texture.Width - (scaling * (1-appear3) * ChapterInfo!.ChapterTitleTexture!.Value.Texture.Width),
                    300 * Runtime.CurrentRuntime.ScaleF * (0.075f+1-appear2)
                    ),
                0, scaling,
                Color.White with {A = Helper.TimeToTransparency(appear1)});
        }

        if (RenderBossTitle)
            DrawTexture(ChapterInfo.BossTitleTexture.Value.Texture, (int)(Runtime.CurrentRuntime.ScaleF * 4),(int)(Runtime.CurrentRuntime.ScaleF * 4),Color.White);
        if(typeI > 1 && !InChapterDelay)
            Helper.DrawTimer((int)(UIAboveGameplay.Texture.Width - (appearTimer)*Helper.TimerTextureSize.X), 0);
        EndTextureMode();
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
    public RenderTexture2D ScoreTexture;
    public RenderTexture2D HiScoreTexture;
    public Rectangle ScoreSrc, ScoreDest, HiScoreSrc, HiScoreDest;
    
    public int Score
    {
        get => score;
    }
    
    public void UpdateUI()
    {
    }
    #endregion
    #region Time
    public float GetTime()
    {
        if (IsGameOver)
            return GameoverTimestamp;
        if (IsPaused)
            return PauseTimestamp;
        return (float)(Raylib.GetTime() - PauseTimestamp);
    }

    public double CountTimeFrom = 0;
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

    public void Dispose()
    {
        UnloadRenderTexture(Background);
        UnloadRenderTexture(ScoreTexture);
        UnloadRenderTexture(HiScoreTexture);
        UnloadRenderTexture(Box);
        UnloadRenderTexture(UIAboveGameplay);
    }

    public RuntimeObject SpawnObject(int i)
    {
        var x = RuntimeObject.LoadFromFile(StageInfo.Entities[i], this);
        x.Header[0x17] = CurrentTick;
        AddObject(x);
        return x;
    }
}
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

        foreach(var  obj in BoxObjects)
        {
            obj.Update();
            if (obj.X < -32 || obj.Y < -32 || obj.X > 416 || obj.Y > 480)
            {
                RemoveObject(obj);
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
        StageInfo = RuntimeStageInfo.LoadFromFile(stage, difficulty);
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
        StageBackgroundObject.Draw(Background, CurrentTick);
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
            DrawRectangle((int)obj.TargetRectangle.X, (int)obj.TargetRectangle.Y, (int)obj.TargetRectangle.Width,
                (int)obj.TargetRectangle.Height, Color.Beige);
            DrawTexturePro(
                obj.Texture,
                obj.SourceRectangle,
                obj.TargetRectangle,
                obj.Origin, obj.RenderRotation, Color.White
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
}
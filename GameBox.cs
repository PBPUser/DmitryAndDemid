using DmitryAndDemid.Backgrounds;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Screens;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid;

public class GameBox : IDisposable
{
    private RuntimeStageInfo StageInfo;
    public string ProtogonistId;
    public int Difficulty;
    public Player Player;
    
    public GameBox(GameplayScreen screen, ProtogonistData data, FileStageInfo stage, int chapter, int difficulty, bool practice)
    {
        Player = new Player(this, data, new PlayerController());
        ProtogonistId = data.ID;
        Difficulty = difficulty;
        PauseTimestamp = (float)(Raylib.GetTime() + 3);
        Background = LoadRenderTexture(384, 448);
        Box = LoadRenderTexture(384, 448);
        LoadStage(stage, chapter, difficulty);
    }

    public const float TargetTPS = 60;
    private float TickLength = 1f / TargetTPS;
    private bool RequiresRefresh = false;
    private List<GameObject> 
        ObjectsAddQueue = new(),
        ObjectsRemoveQueue = new(),
        BoxObjects = new();
    public int CurrentTick = 0;
    private int CurrentTickCompute => (int)(GetTime() * TargetTPS);
    
    #region Update
    public void Update()
    {
        BoxUpdate();
    }

    public void ProcessInput()
    {
        
    }
    #endregion
    #region Managment
    void BoxUpdate()
    {
        if (CurrentTick >= CurrentTickCompute)
            return;
        CurrentTick++;
        
    }
    
    public void AddObject(GameObject obj)
    {
        ObjectsAddQueue.Add(obj);
        RequiresRefresh = true;
    }

    public void RemoveObject(GameObject obj)
    {
        ObjectsRemoveQueue.Add(obj);
        RequiresRefresh = true;
    }
    
    public void LoadStage(FileStageInfo stage, int chapter, int difficulty)
    {
        StageInfo = RuntimeStageInfo.LoadFromFile(stage, difficulty);
    }
    #endregion
    #region Render

    private static StageBackground StageBackgroundObject = new DrogichinBackground();
    private static Color Transparent = Color.Black with { A = 0 };
    public List<GameplayScreenEffect> ScreenEffects = new();
    public RenderTexture2D Background;
    public RenderTexture2D Box;
    public void RenderBox()
    {
        StageBackgroundObject.Draw(Background, CurrentTick);
        BeginTextureMode(Box);
        ClearBackground(Transparent);
        foreach (var obj in BoxObjects)
        {
            if (0x100 == (obj.Variables[0] & 0x100))
            {
                
            }
            DrawTexturePro(
                obj.Texture,
                obj.SourceRectangle,
                obj.DestinationRectangle,
                obj.Origin, obj.FloatingPoints[3], Color.White
            );
            EndShaderMode();
        }
        EndTextureMode();
    }
    #endregion
    #region UI
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
    }
}
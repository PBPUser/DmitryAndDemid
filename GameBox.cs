using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid;

public class GameBox : IDisposable
{
    public GameBox()
    {
        CountTimeFrom = Raylib.GetTime();
        TargetTexture = Raylib.LoadRenderTexture(384, 448);
    }

    public const int TargetTPS = 60;
    public RenderTexture2D TargetTexture;
    private List<GameObject> 
        ObjectsAddQueue = new(),
        ObjectsRemoveQueue = new(),
        ObjectsQueue = new(),
        BoxObjects = new();
    public int CurrentTick = 0;
    private int CurrentTickCompute => (int)(GetTime() / TargetTPS);
    
    public void LoadChapterInfo(CompiledChapterInformation chapterInfo)
    {
        
    }

    #region Update
    #endregion
    #region Render
    public void RenderBox()
    {
        BeginTextureMode(TargetTexture);
        ClearBackground(Color.Black with {A=0});
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
    #region Score
    private int score = 0;
    private int hiScore = 0;
    public byte Continue = 0;
    public RenderTexture2D ScoreTexture;
    public RenderTexture2D HiScoreTexture;
    
    public int Score
    {
        get => score;
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

    private double CountTimeFrom = 0;
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
        Raylib.UnloadRenderTexture(TargetTexture);
    }
}
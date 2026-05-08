using Raylib_cs;

namespace DmitryAndDemid;

public class GameBox
{
    public GameBox()
    {
        CountTimeFrom = Raylib.GetTime();
    }

    public const int TargetTPS = 60;



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
    
    #region TIME
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
}
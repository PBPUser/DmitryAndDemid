using System.Diagnostics;
using System.Numerics;
using Atk;
using DmitryAndDemid.Backgrounds;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Screens;
using DmitryAndDemid.Utils;
using Gdk;
using Gtk;
using Raylib_cs;
using static Raylib_cs.Raylib;
using Color = Raylib_cs.Color;
using Rectangle = Raylib_cs.Rectangle;

namespace DmitryAndDemid;

public class Game : IDisposable
{
    static Game()
    {
        StagesTitleTexture = Runtime.CurrentRuntime.Textures["stages.png"];
    }
    
    public const int TPS = 60;
    public const int ChapterDelay = 120;
    public const int DialogLength = 600;
    public GameplayScreen GameplayScreen;
    private const double BossArtAnimationLength = 2d;
    private static Rectangle BossRectangle = new Rectangle(0, 0, 384, 448);
    private static float ChapterBossTitleYFrom = 320;
    public int Difficulty = 1;
    public RenderTexture2D CurrentScoreTexture;
    
    public Rectangle CurrentScoreSource;
    public Rectangle CurrentScoreTarget;

    public long Score
    {
        get => score;
        set
        {
            if (score == value)
                return;
            score = value;
            var text = FormatScore(value);
            var measure = MeasureTextEx(Runtime.CurrentRuntime.Fonts["kodemono"],
                text, 16 * Runtime.CurrentRuntime.ScaleF, 0);
            Helper.DrawTextOnRenderTexture(ref CurrentScoreTexture, text, (int)(16 * Runtime.CurrentRuntime.ScaleF),
                (int)(0 * Runtime.CurrentRuntime.ScaleF), 
                (int)(0 * Runtime.CurrentRuntime.ScaleF), 
                (int)(0 * Runtime.CurrentRuntime.ScaleF),
                Runtime.CurrentRuntime.Fonts["kodemono"], Color.White, "gradient", Runtime.CurrentRuntime.ScaleF);
            CurrentScoreSource =
                new Rectangle(0, 0, CurrentScoreTexture.Texture.Width, 
                    CurrentScoreTexture.Texture.Height);
            CurrentScoreTarget =
                new Rectangle(622 * Runtime.CurrentRuntime.ScaleF - measure.X,
                    64 * Runtime.CurrentRuntime.ScaleF,
                    CurrentScoreTexture.Texture.Width,
                    CurrentScoreTexture.Texture.Height);
            
        }
    }

    private int Continue = 0;
    
    private string FormatScore(long _score)
    {
        int digitsCount = (int)Math.Log10(_score);
        string result = "";
        
        string score = $"{Continue}";
        if (_score > 0)
            score = $"{_score}{Continue}";
        for (int i = score.Length - 1; i >= 0; i--)
        {
            result = score[i] + result;
            if ((score.Length-i) % 3 == 0 && i > 0)
                result = "," + result;
        }
        return result;
    }
    
    private long score = -1;
    
    public Game(ProtogonistData protogonistData, Stage stage, GameplayScreen screen, int difficulty, Replay? replay = null)
    {
        DialogProtogonistTexture = Runtime.CurrentRuntime.Textures[protogonistData.DialogArtName];
        ProtogonistId = protogonistData.ID;
        Gameplay = LoadRenderTexture(384, 448);
        Background = LoadRenderTexture(384, 448);
        Dialog = LoadRenderTexture(Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height);
        Difficulty = difficulty;
        
        RectangleDialogProtogonistActive = Helper.Scale(new Rectangle(0, 224, 192, 256), Runtime.CurrentRuntime.Scale);
        RectangleDialogAntogonistActive = Helper.Scale(new Rectangle(256, 224, 192, 256), Runtime.CurrentRuntime.Scale);
        RectangleDialogProtogonistInactive = Helper.Scale(new Rectangle(-32, 288, 144, 192), Runtime.CurrentRuntime.Scale);
        RectangleDialogAntogonistInactive = Helper.Scale(new Rectangle(288, 288, 144, 192), Runtime.CurrentRuntime.Scale);
        RectangleDialogProtogonistPassive = Helper.Scale(new Rectangle(-32, 480, 144, 192), Runtime.CurrentRuntime.Scale);
        RectangleDialogAntogonistPassive = Helper.Scale(new Rectangle(288, 480, 144, 192), Runtime.CurrentRuntime.Scale);
        Player = new Player(this, protogonistData, replay == null ? new PlayerController() : new ReplayController(replay));
        Objects.Add(Player);
        StageInfo = stage;
        CurrentStage = new RuntimeStage(stage, this);
        StageBackground = CurrentStage.Background;
        Playing = true;
        CurrentScoreTexture = LoadRenderTexture((int)(200*Runtime.CurrentRuntime.ScaleF), (int)(40*Runtime.CurrentRuntime.ScaleF));
        GameplayScreen = screen;
        UITexture = LoadRenderTexture(
            (int)(186 * Runtime.CurrentRuntime.ScaleF),
            (int)(500 * Runtime.CurrentRuntime.ScaleF)
        );
        UIElementWidth = (int)(20 * Runtime.CurrentRuntime.ScaleF);
        UIElementHeight = (int)(20 * Runtime.CurrentRuntime.ScaleF);
        BombsY = (int)(36 * Runtime.CurrentRuntime.ScaleF);
        UIPositionX = (int)(436 * Runtime.CurrentRuntime.ScaleF);
        UIPositionY = (int)(97 * Runtime.CurrentRuntime.ScaleF);

        ScoreDifferenceFontSize = 12;
        var sdfs = (int)(12 * Runtime.CurrentRuntime.ScaleF);
        var measureScoreDif = MeasureTextEx(Runtime.CurrentRuntime.Fonts["kodemono"],
            "0", sdfs, 0);
        ScoreLetterHeight = (int)measureScoreDif[1];
        ScoreValuesTexture = LoadRenderTexture((int)(measureScoreDif.X * 11), (int)(measureScoreDif.Y * 10));
        ScoreLetterWidth = (int)measureScoreDif.X;
        ScoreTexture = LoadRenderTexture((int)(160 * Runtime.CurrentRuntime.ScaleF), (int)(112 * Runtime.CurrentRuntime.ScaleF));
        ScoreTransferTargetRectangle = new Rectangle(0, 22, ScoreValuesTexture.Texture.Width, measureScoreDif.Y);

        ScoreTextureSourceRC = Helper.GetFullSourceRenderTexture(ScoreTexture);
        ScoreTextureDestinationRC = new Rectangle(
            new Vector2(144, 186) * Runtime.CurrentRuntime.ScaleF,
            new Vector2(160, 112) * Runtime.CurrentRuntime.ScaleF
        );
        ScoreXBase = (int)(144 * Runtime.CurrentRuntime.ScaleF);
        BonusTargetRect = Helper.Scale(new Rectangle(32,96,384,128), Runtime.CurrentRuntime.Scale);
        
        Score = -1;
        Start();
        
    }

    public int UIPositionX;
    public int UIPositionY;
    
    public Stage StageInfo;
    RuntimeStage CurrentStage;

    public bool Playing
    {
        get => playing;
        set
        {
            if (playing == value)
                return;
            playing = value;
            GameplayScreen.Paused = !value;
            if (value)
            {
                var s = GetTime() - PauseTimestamp;
                PreviousTick += s;
                NextTickStamp += s;
                DialogAppearTime += s;
                DialogDisappearTime += s;
                BonusAppearTime += s;
                GameStartedTimestamp += s;
                return;
            }
            PauseTimestamp = GetTime();
        }
    }

    private bool playing = true;
    
    public RenderTexture2D Gameplay;
    public RenderTexture2D Dialog;
    public RenderTexture2D Background;
    

    public StageBackground StageBackground;

    public List<RuntimeObject> Objects = new();

    private int 
        ChapterBulletIndex = 0,
        ChapterEnemyIndex = 0;

    double GameStartedTimestamp = 0;
    double PreviousTick = 0;
    double NextTickStamp = 0;
    static double TickLength = 1d / TPS;
    double PauseTimestamp = 0;
    public int CurrentTick = 0;
    int ChapterSwitchTick = int.MaxValue;
    private bool ObjectsPending = false;
    
    public string ProtogonistId;
    private static Rectangle RectangleDialougePersonSource = new Rectangle(0, 0,768, 1024);
    Rectangle RectangleDialogProtogonistActive;
    Rectangle RectangleDialogAntogonistActive;
    Rectangle RectangleDialogProtogonistInactive;
    Rectangle RectangleDialogAntogonistInactive;
    Rectangle RectangleDialogProtogonistPassive;
    Rectangle RectangleDialogAntogonistPassive;
    private int DialogAntogonistIndex = 0;
    private int DialogProtogonistIndex = 0;

    List<RuntimeObject> ObjectsToAdd = new();

    public void AddObject(RuntimeObject obj)
    {
        ObjectsPending = true;
        ObjectsToAdd.Add(obj);
        obj.CreateScript?.Invoke(obj);
    }

    public void UpdateScoreFirstTime()
    {
        Score = -1;
        Score = 0;
    }
    
    public void UpdateToNext()
    {
        float time = (float)GetTime();
        if(DrawUpdateScoreCounter)
            UpdateScoreCounter();
        CurrentTick++;
        if (CurrentTick == 1932)
        {
            
        }
        PreviousTick = NextTickStamp;
        NextTickStamp = GameStartedTimestamp + (CurrentTick + 1) * TickLength;
        if (IsDied)
        {
            if (DiedTimestamp + DieAnimationLength < time)
                IsDied = false;
        }
        if (IsDialog)
        {
            if (NextDialogTick < CurrentTick)
            {
                dialogIndex++;
                SetDialogElement(CurrentDialog.Elements[dialogIndex]);
            }
        }
        else if (ChapterSwitchTick < CurrentTick + ChapterDelay)
        {
            ClearAll();
        }
        else
        {
            while (ChapterBulletIndex < CurrentChapter.Bullets.Length)
            {
                if (CurrentChapter.Bullets[ChapterBulletIndex].SpawnTick <= CurrentTick - TickChapterStart)
                {
                    CurrentChapter.Bullets[ChapterBulletIndex].CreateScript?.Invoke(CurrentChapter.Bullets[ChapterBulletIndex]);
                    Objects.Add(CurrentChapter.Bullets[ChapterBulletIndex]);
                    ChapterBulletIndex++;
                    continue;
                }
                break;
            }
            while (ChapterEnemyIndex < CurrentChapter.Enemies.Length)
            {
                if (CurrentChapter.Enemies[ChapterEnemyIndex].SpawnTick <= CurrentTick - TickChapterStart)
                {
                    CurrentChapter.Enemies[ChapterEnemyIndex].CreateScript?.Invoke(CurrentChapter.Enemies[ChapterBulletIndex]);
                    Objects.Add(CurrentChapter.Enemies[ChapterEnemyIndex]);
                    ChapterEnemyIndex++;
                    continue;
                }
                break;
            }
        }

        DrawStageTitle = time < TimestampDisappearStageTitle && time > TimestampAppearStageTitle;
        if (CurrentTick == ChapterSwitchTick)
            NextChapter();
        if (CurrentChapter != null)
        {
            if (CurrentChapter.Type != ChapterType.NonBoss)
            {
                int j = CurrentChapter.ChapterLength - CurrentTick + TickChapterStart;
                if (j == 0)
                {
                    SetBonus(true);
                    Helper.PlaySound(Runtime.CurrentRuntime.Sounds["fault"]);
                }
                else if (j > 0 && (CurrentChapter.ChapterLength > 600 && j < 601 || j < 301))
                    if(j % 60 == 0)
                        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["pre-timeout"]);
            }
        }
        
        foreach (var x in Objects)
        {
            x.Update();
            if(!x.UseVelocity)
                x.UpdateScript?.Invoke(x);
            if (!Helper.IsInArea(x.PositionTo, AreaStart, AreaEnd)) 
                ToRemove.Add(x);
        }
        if(NextClearAllTick == CurrentTick)
            ClearAll();
        if (ObjectsPending)
        {
            Objects.AddRange(ObjectsToAdd);
            ObjectsToAdd.Clear();
        }
        if (IsDialog)
            return;

        
        foreach (var x in ToRemove)
        {
            RemovedBullets.Add(new RemovedBullet(x.PositionTo, time));
            Objects.Remove(x);
        }
        ToRemove.Clear();
        for(int i = 0; i < RemovedBullets.Count; i++)
        {
            if (time - RemovedBullets[i].Time > 0.5)
            {
                RemovedBullets.RemoveAt(i);
                i--;
            }
        }
    }

    private List<RemovedBullet> RemovedBullets = new();
    private List<RuntimeObject> ToRemove = new List<RuntimeObject>();
    static Vector2 AreaStart = new Vector2(-100, -100);
    static Vector2 AreaEnd = new Vector2(484, 548);
    
    bool IsDialog = false;
    int NextDialogTick = 0;
    RuntimeDialog CurrentDialog;
    RuntimeDialogElement? CurrentElement;
    int dialogIndex = 0;

    Rectangle
        DialogRectangleSource,
        DialogRectangleTarget;

    Texture2D
        DialogProtogonistTexture,
        DialogAntogonistTexture,
        DialogMessageTexture;

    private double
        DialogAppearTime = double.MinValue,
        DialogDisappearTime = double.MaxValue,
        DialogCharatcterSwitchAppearTime = double.MaxValue,
        DialogCharatcterSwitchDisappearTime = double.MaxValue;

    void LoadNextStage()
    {
        TogglePause(true);
    }

    private Rectangle RectangleChapterTitleSource;
    private Rectangle RectangleChapterTitleDestination;
    
    void NextChapter()
    {
        if (CurrentChapter != null)
        {
            if (CurrentChapter.Type == ChapterType.Spell)
                UnloadRenderTexture(CurrentChapter.ChapterTitleTexture);
            CurrentChapter = null;
            SetScoreCounter();
        }
        ChapterIndex++;
        ChapterSwitchTick = int.MaxValue;
        if (ChapterIndex == CurrentStage.StageElements.Count)
        {
            LoadNextStage();
            return;
        }
        var s = CurrentStage.StageElements[ChapterIndex];
        if (s is RuntimeDialog r)
        {
            SetDialog(r);
        }
        else if (s is Chapter c)
        {
            StartChapter(c);
        }
    }

    void SetDialog(RuntimeDialog r)
    {
        CurrentElement = null;
        dialogIndex = 0;
        CurrentDialog = r;
        DialogAppearTime = GetTime();
        DialogDisappearTime = double.MaxValue;
        SetDialogElement(r.Elements[0]);
        IsDialog = true;
    }

    public RenderTexture2D ScoreTexture;
    public RenderTexture2D ScoreValuesTexture;

    private uint ScoreTicks = 0;
    private long PreviousScoreWhenScoreCounterUpdated = 0;
    private bool DrawUpdateScoreCounter = false;
    private float ScoreCounterAppearanceTimestamp = float.MaxValue;
    private float ScoreCounterDisappearanceTimestamp = float.MaxValue;
    private const float ScoreCounterShowDuration = 3f;
    private const float ScoreTickDuration = 1f;
    private int ScoreDifferenceFontSize;
    private uint ScoreTick = 0;
    private Rectangle ScoreTransferTargetRectangle;
    private int[] ScoreTransferTargetX = new int[10];
    private int ScoreLetterWidth = 0;
    private int ScoreLetterHeight = 0;
    private int ScoreXBase = 0;

    public Rectangle ScoreTextureSourceRC;
    public Rectangle ScoreTextureDestinationRC;
    
    void SetScoreCounter()
    {
        ScoreTick = uint.MaxValue;
        long scoreDifference = Score-PreviousScoreWhenScoreCounterUpdated;
        PreviousScoreWhenScoreCounterUpdated = Score;
        ScoreTicks = Math.Clamp((uint)Math.Log10(scoreDifference), 2, 10);
        float[] scoreTickValues = GetRandomSequence(ScoreTicks, 0f, 100f);
        float total = 0;
        for (int i = 0; i < ScoreTicks; i++)
        {
            total += scoreTickValues[i];
            scoreTickValues[i] += total;
        }
        scoreTickValues[0] = 0;
        scoreTickValues[ScoreTicks-1] = total;
        int t;
        var font = Runtime.CurrentRuntime.Fonts["kodemono"];
        BeginTextureMode(ScoreValuesTexture);
        for (int i = 0; i < ScoreTicks; i++)
        {
            t = (int)(scoreTickValues[i] / total * scoreDifference);
            ScoreTransferTargetX[i] = ScoreXBase - ScoreLetterWidth*((int)Math.Log10(t)+1);
            Helper.DrawTextOnRenderTextureWithoutReinitialization(ref ScoreValuesTexture, new Vector2(0,0), $"{t}0", ScoreDifferenceFontSize, 
                0, font, Color.White, "gradient", Runtime.CurrentRuntime.ScaleF);
            //DrawTextPro(font, new Vector2(0, i*ScoreTransferTargetRectangle.Y), Vector2.Zero,
                //ScoreDifferenceFontSize, Color.White);
        }
        EndTextureMode();
        DrawUpdateScoreCounter = true;
        ScoreCounterAppearanceTimestamp = (float)GetTime();
        ScoreCounterDisappearanceTimestamp = ScoreCounterAppearanceTimestamp + ScoreCounterShowDuration;
    }

    void UpdateScoreCounter()
    {
        float time = (float)GetTime();
        if (ScoreCounterDisappearanceTimestamp < time)
        {
            DrawUpdateScoreCounter = false;
            return;
        }
        uint currentScoreTick = (uint)(Math.Min((time - ScoreCounterAppearanceTimestamp) / ScoreTickDuration, 1) * ScoreTicks);
        if (currentScoreTick == ScoreTick)
            return;
        BeginTextureMode(ScoreTexture);
        ClearBackground(Color.White with {A=0});
        DrawTextureEx(
            Runtime.CurrentRuntime.Textures["chapter-finish-template.png"], Vector2.Zero, 0,
            Runtime.CurrentRuntime.ScaleF/4, Color.White
            );
        DrawTexturePro(ScoreValuesTexture.Texture, 
            new Rectangle(0, currentScoreTick * ScoreTransferTargetRectangle.Height, ScoreTransferTargetRectangle.Width, ScoreLetterWidth),
            ScoreTransferTargetRectangle with { X = 0 },
            Vector2.Zero, 0, Color.White
            );
        EndTextureMode();
        if (currentScoreTick == ScoreTicks)
        {
            //TODO: Play score complete sound
        }
        else
        {
            //TODO: Play score tick sound
        }
        ScoreTick = currentScoreTick;
    }
    
    void SetDialogElement(RuntimeDialogElement element)
    {
        if (dialogIndex + 1 == CurrentDialog.Elements.Count)
        {
            ChapterSwitchTick = CurrentTick + DialogLength;
            NextDialogTick = int.MaxValue;
            DialogDisappearTime = GetTime() + 0.5 + TickLength * DialogLength;
        }
        else
            NextDialogTick = CurrentTick + DialogLength;

        if (CurrentStage.Bosses.ContainsKey(element.ID))
        {
            var boss = CurrentStage.Bosses[element.ID];
            AddObject(boss);
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["boss-appear"]);
            boss.SetSpawnTick(CurrentTick);
        }
        bool? previousSpeaker = CurrentElement != null ? CurrentElement.AntogonistSpeak : null;
        CurrentElement = element;
        if (previousSpeaker == null)
        {
            if (element.AntogonistSpeak)
            {
                DialogCharatcterSwitchAppearTime = GetTime();
                DialogCharatcterSwitchDisappearTime = double.MaxValue;
            }
            else
            {
                DialogCharatcterSwitchDisappearTime = GetTime()+ 0.5;
                DialogCharatcterSwitchAppearTime = double.MinValue;
            }
        }
        if (element.AntogonistSpeak != previousSpeaker)
        {
            if (element.AntogonistSpeak)
            {
                DialogCharatcterSwitchAppearTime = GetTime();
                DialogCharatcterSwitchDisappearTime = double.MaxValue;
            }
            else
            {
                DialogCharatcterSwitchDisappearTime = GetTime() + 0.5;
                DialogCharatcterSwitchAppearTime = double.MinValue;
            }
        }
        DialogMessageTexture = element.DialogTexture.Texture;
        if (element.AntogonistSpeak)
        {
            DialogAntogonistTexture = element.Art;
            DialogAntogonistIndex = element.ArtIndex;
            DialogRectangleSource = Helper.GetFullSourceRenderTexture(element.DialogTexture);
        }
        else
        {
            DialogRectangleSource = Helper.GetFullSourceRenderTexture(element.DialogTexture);
            DialogProtogonistIndex = element.ArtIndex;
        }
        DialogRectangleTarget = Helper.Scale(Helper.ScaleByHeight(84, 320, DialogRectangleSource.Size / (float)Runtime.CurrentRuntime.Scale, (float)(-160 / Runtime.CurrentRuntime.Scale)),  Runtime.CurrentRuntime.Scale);
    }

    private int NextClearAllTick = -1;
    
    void ClearAll()
    {
        if(Objects.Count > 1)
            ToRemove.AddRange(Objects.Where(x => !x.ClearProtected));
    }

    private float BossAppearTimestamp = 0;
    private static Vector2 BossArtShift = new Vector2(768, 896);
    private static Vector2 BossStaticShift = new Vector2(192, 0);
    
    void StartChapter(Chapter chapter)
    {
        CurrentChapter = chapter;
        if (chapter.Type == ChapterType.Spell)
        {
            BossAppearTimestamp = (float)GetTime();
            RectangleChapterTitleSource = Helper.GetFullSource(CurrentChapter.ChapterTitleTexture.Texture);
            RectangleChapterTitleDestination = new Rectangle(384 - RectangleChapterTitleSource.Width, 0,
                RectangleChapterTitleSource.Size);
        }

        Boss boss;
        foreach (var action in CurrentChapter.BossActions)
        {
            boss = CurrentStage.Bosses[action.ID];
            boss.UpdateScript = Boss.Actions[action.MoveAction];
            boss.ShootScript = Boss.Actions[action.ShootAction];
            boss.CreateScript = Boss.Actions[action.StartAction];
            boss.CreateScript.Invoke(boss);
        }
        IsDialog = false;
        TickChapterStart = CurrentTick;
        ChapterBulletIndex = 0;
        ChapterSwitchTick = CurrentTick + CurrentChapter.ChapterLength + ChapterDelay;
    }

    private const double BonusTextDuration = 5;
    private const double BonusAppearDuration = 0.25;
    private static Rectangle BonusSourceRect = new Rectangle(0, 0, 1536, 512);
    private Rectangle BonusTargetRect;
    private double BonusAppearTime = 0;
    private double BonusDisappearTime = 0;
    private Texture2D BonusTexture;

    
    void SetBonus(bool isFailed, string bonus = "")
    {
        if (isFailed)
            BonusTexture = Runtime.CurrentRuntime.Textures["bonus-failed.png"];
        BonusAppearTime = GetTime();
        BonusDisappearTime = GetTime() + BonusTextDuration;
    }
    
    private const float BossAppearXAnimation = .75f;
    private const float BossAppearYAnimation = .75f;
    private const float BossAppearAnimationWait = .75f;
    
    public void RenderGame()
    {
        float time = (float)GetTime();
        StageBackground.Draw(Background, CurrentTick);
        float state = Helper.EaseInOutElasticF((float)((GetTime() - PreviousTick) / TickLength));
        (Rectangle rc, float rotation) info;
        BeginTextureMode(Gameplay);
        ClearBackground(Color.White with { A = 0 });
        int vy = 0;
        Player.RenderBottomLayer();
        foreach (var x in Objects)
        {
            info = x.GetRenderInfo(state);
            #if DEBUG
            if(IsKeyDown(KeyboardKey.F))
            DrawText($"source_rc: {x.SourceRect}, info_rc: {info.rc}", 0, vy+=8,8,Color.White);
            #endif
            DrawTexturePro(x.SourceTexture, x.SourceRect, info.rc, Vector2.Zero, info.rotation, Color.White);
        }
        Player.RenderTopLayer();
        Helper.DrawDeathPoints(RemovedBullets, "disappear_shoot");
        if (CurrentChapter != null)
        {
            if (CurrentChapter?.Type != ChapterType.NonBoss)
            {
                DrawText(""+((CurrentChapter.ChapterLength - CurrentTick) / 60), 0,0,24,Color.White);
            }
            if (CurrentChapter?.Type == ChapterType.Spell)  {
                float appear = .5f - Helper.BossAppearCurveF(time-BossAppearTimestamp, 5f);
                float titleAppearX = MathF.Pow(1-Raymath.Clamp((time-BossAppearTimestamp)/BossAppearXAnimation, 0, 1), 6);
                float titleAppearY = MathF.Pow(1-Raymath.Clamp((time-BossAppearXAnimation-BossAppearAnimationWait-BossAppearTimestamp)/BossAppearYAnimation, 0, 1), 6);
            
                DrawTexturePro(CurrentChapter.ChapterAttackTexture, 
                    BossRectangle, BossRectangle with { Position = BossRectangle.Position - BossArtShift * appear + BossStaticShift },
                    Vector2.Zero, appear, Color.White with {A=192});
                DrawTexturePro(CurrentChapter.ChapterTitleTexture.Texture,
                    RectangleChapterTitleSource,  
                    RectangleChapterTitleDestination with { X = RectangleChapterTitleDestination.X + (titleAppearX*RectangleChapterTitleDestination.Width), 
                        Y = ChapterBossTitleYFrom * titleAppearY },
                    Vector2.Zero, 0,
                    Color.White);
#if DEBUG
                if (Raylib.IsKeyDown(KeyboardKey.D))
                {
                    DrawText("Entity Rectangle: "+appear , 20, 370, 20, Color.White);
                    DrawText("Boss Appear: "+appear , 20, 370, 20, Color.White);
                    DrawText("X Appear: "+titleAppearX , 20, 390, 20, Color.Blue);
                    DrawText("Y Appear: "+titleAppearY , 20, 410, 20, Color.Red);
                }
#endif
            }
        }
        EndTextureMode();
        BeginTextureMode(Dialog);
        ClearBackground(Color.White with { A = 0 });
        float bonusSignState = (float)Helper.ComputeObjectTime(
            time, BonusAppearTime, BonusAppearDuration, BonusDisappearTime, BonusAppearDuration
        );
        float fullPowerState = (float)Helper.ComputeObjectTime(
            time, FullPowerAppearTimestamp, BonusAppearDuration, FullPowerDisappearTimestamp, BonusAppearDuration
        );
        if (IsDialog)
        {
            float statement = Helper.Pow2F((float)Helper.ComputeObjectTime(GetTime(), DialogCharatcterSwitchAppearTime, 0.5,
                DialogCharatcterSwitchDisappearTime, 0.5));
            float statementDialogAppear =
                Helper.Pow2F(
                    (float)Helper.ComputeObjectTime(GetTime(), DialogAppearTime, 0.5, DialogDisappearTime, 0.5));
            Color antoColor = Helper.Mix(Color.Black, Color.White, 0.5f + statement * 0.5f);
            Color protoColor = Helper.Mix(Color.Black, Color.White, 0.5f + (1-statement) * 0.5f);
            Rectangle
                antoRect = Helper.Mix(RectangleDialogAntogonistActive, RectangleDialogAntogonistInactive, 1-statement);
            Rectangle
                protoRect = Helper.Mix(RectangleDialogProtogonistActive, RectangleDialogProtogonistInactive, statement);
            antoRect.Y = Helper.Mix(Runtime.CurrentRuntime.Height, antoRect.Y, statementDialogAppear);
            protoRect.Y = Helper.Mix(Runtime.CurrentRuntime.Height, protoRect.Y, statementDialogAppear);
            DrawTexturePro(DialogAntogonistTexture,
                RectangleDialougePersonSource with {X = RectangleDialougePersonSource.Width * DialogAntogonistIndex },
                antoRect,
                Vector2.Zero, 0, antoColor);
            DrawTexturePro(DialogProtogonistTexture,
                RectangleDialougePersonSource with { X = RectangleDialougePersonSource.Width * DialogProtogonistIndex },
                protoRect,
                Vector2.Zero, 0, protoColor);
            if(DialogAppearTime > 0.9f)
                DrawTexturePro(DialogMessageTexture,
                    DialogRectangleSource,
                    DialogRectangleTarget,
                    Vector2.Zero, 0, Color.White);
        }

        if (DrawUpdateScoreCounter)
        {
            DrawTexturePro(ScoreTexture.Texture,
                ScoreTextureSourceRC,
                ScoreTextureDestinationRC,
                Vector2.Zero, 0,Color.White);
        }
        #if DEBUG
        if (Raylib.IsKeyDown(KeyboardKey.D))
        {
            DrawText("Time: "+ GetTime(), 20, 110, 32, Color.White);
            DrawText("Chapter Switch Tick: "+ ChapterSwitchTick, 400, 140, 32, Color.White);
            DrawText("Index: "+ChapterIndex, 20, 200, 64, Color.White);
            DrawText("Tick: "+CurrentTick, 20, 140, 32, Color.White);
            DrawText("TPS: "+ (CurrentTick / (GetTime() - GameStartedTimestamp)), 20, 250, 64, Color.Green);
            DrawText("Next Tick: "+ NextTickStamp, 20, 300, 64, Color.Green);
            DrawText("Tick length: "+ TickLength, 20, 350, 20, Color.Blue);
            DrawText("Objects count: "+ Objects.Count, 20, 370, 20, Color.Red);
            if (CurrentChapter != null)
            {
                DrawText("Chapter Length: "+ CurrentChapter.ChapterLength, 400, 110, 32, Color.White);
                DrawText("Bullets count: "+ CurrentChapter.Bullets.Length, 20, 390, 20, Color.Red);
                DrawText("Last bullets: "+ (CurrentChapter.Bullets.Length-ChapterBulletIndex), 20, 410, 20, Color.Red);
            }
            else if (IsDialog)
            {
                DrawText("Next dialog tick: "+ NextDialogTick, 400, 110, 32, Color.White);
            }
        }

        if (IsKeyDown(KeyboardKey.K))
        {
            DrawText($"ScoreTextureSourceRC: {ScoreTextureSourceRC}", 0, 0, 24, Color.White);
            DrawText($"ScoreTextureDestinationRC: {ScoreTextureDestinationRC}", 0, 24, 24, Color.White);
            DrawText($"Tick: {ScoreTick}", 0, 48, 24, Color.White);
            DrawText($"ScoreTransferTargetX: {string.Join(", ", ScoreTransferTargetX)}", 0, 72, 24, Color.White);
            DrawText($"ScoreTransferTargetRectangle: {ScoreTransferTargetRectangle}", 0, 96, 24, Color.White);
            DrawText($"ScoreXBase: {ScoreXBase}", 0, 120, 24, Color.White);
        }        
        if (Raylib.IsKeyDown(KeyboardKey.T))
        {
            DrawText("Tick: "+ time, 128, 512, 32, Color.White);
            DrawText("DrawStageTitle: "+ DrawStageTitle, 128, 0, 32, Color.White);
            DrawText("Appear: "+ TimestampAppearStageTitle, 128, 32, 32, Color.White);
            DrawText("Disappear: "+ TimestampDisappearStageTitle, 128, 64, 32, Color.White);
        }
        if (Raylib.IsKeyDown(KeyboardKey.R))
        {
            DrawText("Source: "+BonusSourceRect , 20, 0, 20, Color.White);
            DrawText("Target: "+BonusTargetRect , 20, 20, 20, Color.Blue);
            DrawText("State: "+bonusSignState , 20, 40, 20, Color.Blue);
        }
        #endif
        if (DrawStageTitle)
        {
            float appear;
            for (int i = 0; i < 4; i++)
            {
                appear = Helper.ComputeObjectTime(time, TimestampAppearStageTitle+.5f*i, TitleAppearLength, TimestampDisappearStageTitle, TitleAppearLength);
                DrawTexturePro(StagesTitleTexture,
                    StageTitleSources[i], StageTitleTargets[i], i==3? StageTitleTargetOrigin :Vector2.Zero, 
                    i == 3 ? 60 * (float)Helper.ComputeObjectTimeStart(time, TimestampAppearStageTitle+2f, 1f) : 0,
                    Color.White with {A = Helper.TimeToTransparency(appear)});
            }
        }
        DrawTexturePro(BonusTexture, BonusSourceRect, BonusTargetRect with { Height = bonusSignState * BonusTargetRect.Height }, Vector2.Zero, 0, Color.White);
        #if DEBUG
        if (Raylib.IsKeyDown(KeyboardKey.H))
        {
            DrawText("target RC: " + BonusTargetRect, 0, 0, 24, Color.White);
            DrawText("source RC: " + BonusSourceRect, 0, 24, 24, Color.White);
            DrawText("FullPowerAppearTimestamp: " + FullPowerAppearTimestamp, 0, 48, 24, Color.White);
            DrawText("FullPowerDisappearTimestamp: " + FullPowerDisappearTimestamp, 0, 72, 24, Color.White);
            DrawText("state: " + fullPowerState, 0, 96, 24, Color.White);
            DrawText("state: " + fullPowerState, 0, 120, 24, Color.White);
        }
        #endif 
        DrawTexturePro(Runtime.CurrentRuntime.Textures["full-power.png"], BonusSourceRect, BonusTargetRect with { Height = BonusTargetRect.Height * fullPowerState }, Vector2.Zero, 0, Color.White);
        EndTextureMode();
    }

    public Player Player;
    bool Disposed = false;

    public void Dispose()
    {
        Disposed = true;
        UnloadRenderTexture(Gameplay);
    }

    Chapter? CurrentChapter = null;
    int ChapterIndex = 0;
    int TickChapterStart = 0;

    public void Start()
    {
        CurrentTick = 0;
        GameStartedTimestamp = GetTime();
        NextTickStamp = GameStartedTimestamp + TickLength;
        ChapterIndex = -1;
        SetTitle(StageInfo.Index, StageInfo.TitleTick);
        NextChapter();
    }

    private Rectangle[] StageTitleSources = new Rectangle[4];
    private Rectangle[] StageTitleTargets = new Rectangle[4];
    private Vector2 StageTitleTargetOrigin;
    private float TimestampAppearStageTitle = float.MaxValue;
    float TimestampDisappearStageTitle = float.MaxValue;
    private const float TitleAppearLength = 0.25f;
    private const float TitleShowDuration = 5f;
    private bool ChapterTitleShow = false;
    private static Texture2D StagesTitleTexture;
    bool DrawStageTitle = false;
    
    public void SetTitle(int index, int tick)
    {
        TimestampAppearStageTitle = (float)(GameStartedTimestamp + (TickLength * tick));
        TimestampDisappearStageTitle = TimestampAppearStageTitle + TitleShowDuration;
        StageTitleTargetOrigin = new Vector2(-40) * Runtime.CurrentRuntime.ScaleF;
        StageTitleSources[0] = new Rectangle(0, 512*index, 1536, 80);
        StageTitleTargets[0] = Helper.Scale(new Rectangle(32, 96, 384, 20), Runtime.CurrentRuntime.ScaleF);
        StageTitleSources[1] = new Rectangle(0, 512*index+80, 1536, 316);
        StageTitleTargets[1] = Helper.Scale(new Rectangle(32, 116, 384, 79), Runtime.CurrentRuntime.ScaleF);
        StageTitleSources[2] = new Rectangle(0, 512*index+396, 1536, 116);
        StageTitleTargets[2] = Helper.Scale(new Rectangle(32, 215, 384, 29), Runtime.CurrentRuntime.ScaleF);
        StageTitleSources[3] = new Rectangle(1536, 640*index, 640, 640);
        StageTitleTargets[3] = Helper.Scale(new Rectangle(256, 110, 80, 80), Runtime.CurrentRuntime.ScaleF);
    }

    public void Update()
    {
        if (NextTickStamp > GetTime())
            return;
        if (!playing)
            return;
        UpdateToNext();
    }

    public void TogglePause(bool? state = null)
    {
        bool rS = state == null ? !Playing : state == true;
        if (rS == Playing)
            return;
        Playing = rS;
    }

    const double DialogSkipDelay = 0.25;
    double DialogSkipCooldownBefore = 0;

    public void ProcessInput()
    {
        if (IsKeyDown(KeyboardKey.V))
        {
            BossKilled();
        }
        if (!IsDialog)
            return;
        if (!CurrentElement.Skipable)
            return;
        if (GetTime() < DialogSkipCooldownBefore)
            return;
        if (IsKeyDown(KeyboardKey.Z))
        {
            DialogSkipCooldownBefore = GetTime() + DialogSkipDelay;
            NextDialogTick = CurrentTick + 1;
            if (dialogIndex + 1 == CurrentDialog.Elements.Count)
                ChapterSwitchTick = CurrentTick + 1;
        }
    }

    void BossKilled()
    {
        ClearAll();
        ChapterSwitchTick = CurrentTick + ChapterDelay;
    }

    public bool IsDied;
    public float DiedTimestamp;
    public Vector2 DiePosition;
    public const float DieAnimationLength = 1.6f;
    public const int DieClearAllDelay = 40;
    
    public void SetDied()
    {
        IsDied = true;
        DiePosition = Player.PositionTo;
        DiedTimestamp = (float)GetTime();
        NextClearAllTick = CurrentTick + DieClearAllDelay;
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["dead"]);
    }

    public void RemoveObject(RuntimeObject runtimeObject)
    {
        ToRemove.Add(runtimeObject);
    }

    private double FullPowerAppearTimestamp;
    private double FullPowerDisappearTimestamp;
    
    public void SetFullPower()
    {
        FullPowerAppearTimestamp = GetTime();
        FullPowerDisappearTimestamp = GetTime() + BonusTextDuration;
        
    }

    public RenderTexture2D UITexture;
    public int UIElementWidth, BombsY, UIElementHeight;
    
    public void UpdateUI()
    {
        var rc = new Rectangle(0, 0, UIElementWidth, UIElementHeight);
        var rc2 = new Rectangle(0, 0, 96, 96);
        BeginTextureMode(UITexture);
        ClearBackground(Color.White with {A=0});
        for(int i = 0; i < 8; i++)
            DrawTexturePro(Runtime.CurrentRuntime.Textures["ingame-stuff.png"],
                rc2 with { X= i<Player.HeartPoints? 0 : i == Player.HeartPoints ? 
                    384 - (Player.HeartSpices * 96)
                    : 384 },
                rc with { X = UIElementWidth * i },
                Vector2.Zero, 0, Color.White);
        rc.Y = BombsY;
        for(int i = 0; i < 8; i++)
            DrawTexturePro(Runtime.CurrentRuntime.Textures["ingame-stuff.png"],
                rc2 with { Y=96, X= i<Player.Bombs? 0 : i == Player.BombsSpices ? 
                    384 - (Player.BombsSpices * 96)
                    : 384 },
                rc with { X = UIElementWidth * i },
                Vector2.Zero, 0, Color.White);
        DrawText($"Power: {Player.Power}", 0, (int)(128 * Runtime.CurrentRuntime.ScaleF), (int)(16 * Runtime.CurrentRuntime.ScaleF), Color.White);
        DrawText($"Graze: {Player.Graze}", 0, (int)(160 * Runtime.CurrentRuntime.ScaleF), (int)(16 * Runtime.CurrentRuntime.ScaleF), Color.White);
        EndTextureMode();
        
    }

    public bool ForcedPause = false;
}

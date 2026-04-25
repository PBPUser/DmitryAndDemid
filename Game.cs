using System.Diagnostics;
using System.Numerics;
using Atk;
using DmitryAndDemid.Backgrounds;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Screens;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;
using Rectangle = Raylib_cs.Rectangle;

namespace DmitryAndDemid;

public class Game : IDisposable
{
    public const int TPS = 60;
    public const int ChapterDelay = 120;
    public const int DialogLength = 600;
    public GameplayScreen GameplayScreen;
    private const double BossArtAnimationLength = 2d;
    private static Rectangle BossRectangle = new Rectangle(0, 0, 384, 448);
    private static float ChapterBossTitleYFrom = 320;

    public Game(ProtogonistData protogonistData, Stage stage, GameplayScreen screen)
    {
        DialogProtogonistTexture = Runtime.CurrentRuntime.Textures[protogonistData.DialogArtName];
        ProtogonistId = protogonistData.ID;
        Gameplay = LoadRenderTexture(384, 448);
        Background = LoadRenderTexture(384, 448);
        Dialog = LoadRenderTexture(Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height);

        RectangleDialogProtogonistActive = Helper.Scale(new Rectangle(0, 224, 192, 256), Runtime.CurrentRuntime.Scale);
        RectangleDialogAntogonistActive = Helper.Scale(new Rectangle(256, 224, 192, 256), Runtime.CurrentRuntime.Scale);
        RectangleDialogProtogonistInactive = Helper.Scale(new Rectangle(-32, 288, 144, 192), Runtime.CurrentRuntime.Scale);
        RectangleDialogAntogonistInactive = Helper.Scale(new Rectangle(288, 288, 144, 192), Runtime.CurrentRuntime.Scale);
        RectangleDialogProtogonistPassive = Helper.Scale(new Rectangle(-32, 480, 144, 192), Runtime.CurrentRuntime.Scale);
        RectangleDialogAntogonistPassive = Helper.Scale(new Rectangle(288, 480, 144, 192), Runtime.CurrentRuntime.Scale);
        
        Player = new Player(this, protogonistData);
        Objects.Add(Player);
        StageInfo = stage;
        CurrentStage = new RuntimeStage(stage, this);
        StageBackground = CurrentStage.Background;
        Start();
        GameplayScreen = screen;
    }

    
    public Stage StageInfo;
    RuntimeStage CurrentStage;
    public bool Playing = false;
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
    
    public void UpdateToNext()
    {
        CurrentTick++;
        PreviousTick = NextTickStamp;
        NextTickStamp = GameStartedTimestamp + (CurrentTick + 1) * TickLength;
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
            
        if (CurrentTick == ChapterSwitchTick)
        {
            NextChapter();
        }
        foreach (var x in Objects)
        {
            x.Update();
            x.UpdateScript?.Invoke(x);
            if (!Helper.IsInArea(x.PositionTo, AreaStart, AreaEnd)) 
                ToRemove.Add(x);
        }

        if (ObjectsPending)
        {
            Objects.AddRange(ObjectsToAdd);
            ObjectsToAdd.Clear();
        }
        
        float time = (float)GetTime();
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

    void ClearAll()
    {
        if(Objects.Count > 1)
            ToRemove.AddRange(Objects[1..^0]);
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
        IsDialog = false;
        TickChapterStart = CurrentTick;
        ChapterBulletIndex = 0;
        ChapterSwitchTick = CurrentTick + CurrentChapter.ChapterLength + ChapterDelay;
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
        foreach (var x in Objects)
        {
            info = x.GetRenderInfo(state);
            #if DEBUG
            if(IsKeyDown(KeyboardKey.F))
            DrawText($"source_rc: {x.SourceRect}, info_rc: {info.rc}", 0, vy+=8,8,Color.White);
            #endif
            DrawTexturePro(x.SourceTexture, x.SourceRect, info.rc, Vector2.Zero, info.rotation, Color.White);
        }
        Helper.DrawDeathPoints(RemovedBullets, "disappear_shoot");
        if (CurrentChapter.Type == ChapterType.Spell)  {
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
        
        EndTextureMode();
        BeginTextureMode(Dialog);
        ClearBackground(Color.White with { A = 0 });
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
        #if DEBUG
        if (Raylib.IsKeyDown(KeyboardKey.D))
        {
            DrawText("Time: "+ GetTime(), 20, 110, 32, Color.White);
            DrawText("Chapter Length: "+ CurrentChapter.ChapterLength, 400, 110, 32, Color.White);
            DrawText("Chapter Switch Tick: "+ ChapterSwitchTick, 400, 140, 32, Color.White);
            DrawText("Index: "+ChapterIndex, 20, 200, 64, Color.White);
            DrawText("Tick: "+CurrentTick, 20, 140, 32, Color.White);
            DrawText("TPS: "+ (CurrentTick / (GetTime() - GameStartedTimestamp)), 20, 250, 64, Color.Green);
            DrawText("Next Tick: "+ NextTickStamp, 20, 300, 64, Color.Green);
            DrawText("Tick length: "+ TickLength, 20, 350, 20, Color.Blue);
            DrawText("Objects count: "+ Objects.Count, 20, 370, 20, Color.Red);
            if (CurrentChapter != null)
            {
                DrawText("Bullets count: "+ CurrentChapter.Bullets.Length, 20, 390, 20, Color.Red);
                DrawText("Last bullets: "+ (CurrentChapter.Bullets.Length-ChapterBulletIndex), 20, 410, 20, Color.Red);
            }
        }
        #endif
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
        NextChapter();
    }


    public void Update()
    {
        if (NextTickStamp > GetTime())
            return;
        UpdateToNext();
    }

    public void TogglePause(bool? state = null)
    {
        bool rS = state == null ? !Playing : state == true;
        if (rS == Playing)
            return;
        Playing = rS;
        if (!Playing)
        {
            PauseTimestamp = GetTime();
        }
        else
        {
            var s = GetTime() - PauseTimestamp;
            PreviousTick += s;
            NextTickStamp += s;
        }
    }

    const double DialogSkipDelay = 0.1;
    double DialogSkipCooldownBefore = 0;

    public void ProcessInput()
    {
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
}

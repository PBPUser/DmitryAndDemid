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

    public Game(ProtogonistData protogonistData, Stage stage, GameplayScreen screen)
    {
        Gameplay = LoadRenderTexture(384, 448);
        Background = LoadRenderTexture(384, 448);
        Dialog = LoadRenderTexture(Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height);
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

    private int ChapterBulletIndex = 0;

    double GameStartedTimestamp = 0;
    double PreviousTick = 0;
    double NextTickStamp = 0;
    static double TickLength = 1d / TPS;
    double PauseTimestamp = 0;
    int CurrentTick = 0;
    int ChapterSwitchTick = int.MaxValue;

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
            while (ChapterBulletIndex < CurrentChapter.Bullets.Length)
            {
                Console.WriteLine("tick: " + CurrentChapter.Bullets[ChapterBulletIndex].SpawnTick);
                if (CurrentChapter.Bullets[ChapterBulletIndex].SpawnTick <= CurrentTick - TickChapterStart)
                {
                    CurrentChapter.Bullets[ChapterBulletIndex].CreateScript?.Invoke(CurrentChapter.Bullets[ChapterBulletIndex]);
                    Objects.Add(CurrentChapter.Bullets[ChapterBulletIndex]);
                    ChapterBulletIndex++;
                    continue;
                }
                break;
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

        foreach (var x in ToRemove)
        {
            Objects.Remove(x);
            RemovedBullets.Add(new RemovedBullet(x.PositionTo, CurrentTick));
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
        DialogRectangleSourceProtogonist,
        DialogRectangleSourceAntogonist,
        DialogRectangleTargetInactiveProtogonist,
        DialogRectangleTargetInactiveAntogonist,
        DialogRectangleTargetActiveProtogonist,
        DialogRectangleTargetActiveAntogonist,
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
    
    void NextChapter()
    {
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
        if (element.AntogonistSpeak)
        {
            DialogAntogonistTexture = element.Art;
            DialogRectangleSourceAntogonist = Helper.GetFullSource(element.Art);
            DialogRectangleTargetActiveAntogonist = Helper.Scale(Helper.ScaleByHeight(320, 120, DialogRectangleSourceAntogonist.Size, 360), Runtime.CurrentRuntime.Scale);
            DialogRectangleTargetInactiveAntogonist = Helper.Scale(Helper.ScaleByHeight(328, 160, DialogRectangleSourceAntogonist.Size, 320), Runtime.CurrentRuntime.Scale);
            DialogRectangleSource = Helper.GetFullSourceRenderTexture(element.DialogTexture);
            DialogMessageTexture = element.DialogTexture.Texture;
            DialogRectangleTarget = Helper.Scale(Helper.ScaleByHeight(84, 320, DialogRectangleSource.Size / (float)Runtime.CurrentRuntime.Scale, (float)(-160 / Runtime.CurrentRuntime.Scale)),  Runtime.CurrentRuntime.Scale);
        }
        else
        {
            DialogProtogonistTexture = element.Art;
            DialogRectangleSourceProtogonist = Helper.GetFullSource(element.Art);
            DialogRectangleTargetActiveProtogonist = Helper.Scale(Helper.ScaleByHeight(24, 120, DialogRectangleSourceProtogonist.Size, 360), Runtime.CurrentRuntime.Scale);
            DialogRectangleTargetInactiveProtogonist = Helper.Scale(Helper.ScaleByHeight(16, 160, DialogRectangleSourceProtogonist.Size, 320), Runtime.CurrentRuntime.Scale);
            DialogRectangleSource = Helper.GetFullSourceRenderTexture(element.DialogTexture);
            DialogMessageTexture = element.DialogTexture.Texture;
            DialogRectangleTarget = Helper.Scale(Helper.ScaleByHeight(84, 320, DialogRectangleSource.Size/ (float)Runtime.CurrentRuntime.Scale, (float)(-160 / Runtime.CurrentRuntime.Scale)),  Runtime.CurrentRuntime.Scale);
        }
    }

    void ClearAll()
    {
        ToRemove.AddRange(Objects[0..^0]);
    }

    void StartChapter(Chapter chapter)
    {
        IsDialog = false;
        CurrentChapter = chapter;
        TickChapterStart = CurrentTick;
        ChapterBulletIndex = 0;
        ChapterSwitchTick = CurrentTick + CurrentChapter.ChapterLength + ChapterDelay;
    }
    
    public void RenderGame()
    {
        StageBackground.Draw(Background, CurrentTick);
        
        float state = Helper.EaseInOutElasticF((float)((GetTime() - PreviousTick) / TickLength));
    
        (Rectangle rc, float rotation) info;
        BeginTextureMode(Gameplay);
        ClearBackground(Color.White with { A = 0 });
        foreach (var x in Objects)
        {
            info = x.GetRenderInfo(state);
            DrawTexturePro(x.SourceTexture, x.SourceRect, info.rc, Vector2.Zero, info.rotation, Color.White);
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
            Color antoColor = Helper.Mix(Color.Black, Color.White, 0.5f + (statement * 0.5f));
            Color protoColor = Helper.Mix(Color.Black, Color.White, 0.5f + ((1-statement) * 0.5f));
            Rectangle
                antoRect = Helper.Mix(DialogRectangleTargetActiveAntogonist, DialogRectangleTargetInactiveAntogonist, 1-statement);
            Rectangle
                protoRect = Helper.Mix(DialogRectangleTargetActiveProtogonist, DialogRectangleTargetInactiveProtogonist, statement);
            antoRect.Y = Helper.Mix(Runtime.CurrentRuntime.Height, antoRect.Y, statementDialogAppear);
            protoRect.Y = Helper.Mix(Runtime.CurrentRuntime.Height, protoRect.Y, statementDialogAppear);
            DrawTexturePro(DialogAntogonistTexture,
                DialogRectangleSourceAntogonist,
                antoRect,
                Vector2.Zero, 0, antoColor);
            DrawTexturePro(DialogProtogonistTexture,
                DialogRectangleSourceProtogonist,
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
            DrawText("Tick length: "+ TickLength, 20, 350, 64, Color.Green);
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

    Chapter CurrentChapter;
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

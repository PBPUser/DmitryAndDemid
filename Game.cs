using System.Numerics;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Screens;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

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
        Dialog = LoadRenderTexture(Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height);
        Player = new Player(this, protogonistData);
        Objects.Add(Player);
        StageInfo = stage;
        CurrentStage = new RuntimeStage(stage);
        Start();
        GameplayScreen = screen;
    }


    public Stage StageInfo;
    RuntimeStage CurrentStage;
    public bool Playing = false;
    public RenderTexture2D Gameplay;
    public RenderTexture2D Dialog;

    public List<RuntimeObject> Objects = new();



    double GameStartedTimestamp = 0;
    double PreviousTick = 0;
    double NextTickStamp = 0;
    static double TickLength = 1 / TPS;
    double PauseTimestamp = 0;
    int CurrentTick = 0;
    int ChapterSwitchTick = int.MaxValue;

    public void UpdateToNext()
    {
        CurrentTick++;
        if (IsDialog)
        {
            if (NextDialogTick < CurrentTick)
            {
                dialogIndex++;
                SetDialogElement(CurrentDialog.Elements[dialogIndex]);
            }
        }
        else if (CurrentTick + ChapterDelay < ChapterSwitchTick)
        {
            ClearAll();
            //CurrentChapter = null;
        }
        if (CurrentTick == ChapterSwitchTick)
        {
            NextChapter();
        }
        PreviousTick = NextTickStamp;
        NextTickStamp = GameStartedTimestamp + ((CurrentTick + 1) * TickLength);
        foreach (var x in Objects)
        {
            x.Update();
            x.UpdateScript?.Invoke(x);
        }
    }

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
        DialogRectangleTargetActiveAntogonist;

    Texture2D
        DialogProtogonistTexture,
        DialogAntogonistTexture;

    private double
        DialogAppearTime = double.MaxValue,
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
        SetDialogElement(r.Elements[0]);
        IsDialog = true;
        DialogAppearTime = GetTime();
    }

    void SetDialogElement(RuntimeDialogElement element)
    {
        if (dialogIndex + 1 == CurrentDialog.Elements.Count)
        {
            ChapterSwitchTick = CurrentTick + DialogLength;
            NextDialogTick = int.MaxValue;
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
            DialogRectangleTargetActiveAntogonist = Helper.Scale(Helper.ScaleByHeight(420, 120, DialogRectangleSourceAntogonist.Size, 360), Runtime.CurrentRuntime.Scale);
            DialogRectangleTargetInactiveAntogonist = Helper.Scale(Helper.ScaleByHeight(428, 160, DialogRectangleSourceAntogonist.Size, 320), Runtime.CurrentRuntime.Scale);
        }
        else
        {
            DialogProtogonistTexture = element.Art;
            DialogRectangleSourceProtogonist = Helper.GetFullSource(element.Art);
            DialogRectangleTargetActiveProtogonist = Helper.Scale(Helper.ScaleByHeight(24, 120, DialogRectangleSourceProtogonist.Size, 360), Runtime.CurrentRuntime.Scale);
            DialogRectangleTargetInactiveProtogonist = Helper.Scale(Helper.ScaleByHeight(16, 160, DialogRectangleSourceProtogonist.Size, 320), Runtime.CurrentRuntime.Scale);
        }
    }

    void ClearAll()
    {
        for (int i = 1; Objects.Count > 1; i++)
        {
            Objects.RemoveAt(i);
        }
    }

    void StartChapter(Chapter chapter)
    {
        IsDialog = false;
        CurrentChapter = chapter;
        TickChapterStart = CurrentTick;
        ChapterSwitchTick = CurrentTick + CurrentChapter.ChapterLength + ChapterDelay;
    }
    
    public void RenderGame()
    {
        float state = Helper.EaseInOutElasticF((float)((GetTime() - PreviousTick) / TickLength));

        (Rectangle rc, float rotation) info;
        BeginTextureMode(Gameplay);
        ClearBackground(Color.Black);
        foreach (var x in Objects)
        {
            info = x.GetRenderInfo(state);
            DrawText("a", (int)x.PositionTo.X, (int)x.PositionTo.Y, 32, Color.White);
            DrawTexturePro(x.SourceTexture, x.SourceRect, info.rc, Vector2.Zero, info.rotation, Color.White);
        }
        EndTextureMode();

        BeginTextureMode(Dialog);
        ClearBackground(Color.White with { A = 0 });
        if (IsDialog)
        {
            float statement = (float)Helper.ComputeObjectTime(GetTime(), DialogCharatcterSwitchAppearTime, 0.5,
                DialogCharatcterSwitchDisappearTime, 0.5);
            DrawText("Appear: "+DialogCharatcterSwitchAppearTime, 20, 50, 32, Color.White);
            DrawText("Disappear: "+DialogCharatcterSwitchDisappearTime, 20, 80, 32, Color.White);
            Rectangle
                antoRect = Helper.Mix(DialogRectangleTargetActiveAntogonist, DialogRectangleTargetInactiveAntogonist, 1-statement);
            Rectangle
                protoRect = Helper.Mix(DialogRectangleTargetActiveProtogonist, DialogRectangleTargetInactiveProtogonist, statement);
            DrawTexturePro(DialogAntogonistTexture,
                DialogRectangleSourceAntogonist,
                antoRect,
                Vector2.Zero, 0, Color.White);
            DrawTexturePro(DialogProtogonistTexture,
                DialogRectangleSourceProtogonist,
                protoRect,
                Vector2.Zero, 0, Color.White);
        }
        DrawText("Time: "+ GetTime(), 20, 110, 32, Color.White);
        DrawText("Tick: "+CurrentTick, 20, 140, 32, Color.White);
        DrawText("Index: "+ChapterIndex, 20, 200, 64, Color.White);

        EndTextureMode();
    }

    Player Player;
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

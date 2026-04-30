using System.Numerics;
using System.Runtime.CompilerServices;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Screens;

public class DifficultyScreen : MenuScreen
{
    private GameType GameType;
    
    public DifficultyScreen(GameType gameType) : base()
    {
        SetTitle(Runtime.CurrentRuntime.Textures["rang_select.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        LoopList = false;
        GameType = gameType;

        HorizontalDirectionNavigation = true;

        RectangleSelectionTarget = Helper.Scale(new Rectangle(310, 220, 220, 120), Runtime.CurrentRuntime.Scale);
        TargetHeight = RectangleSelectionTarget.Height;

        RectangleDestinationDifficultySelect = Helper.Scale(new Rectangle(200, 170, 240, 120), Runtime.CurrentRuntime.Scale);
        RectangleDestinationDifficultySelected = Helper.Scale(new Rectangle(220, 400, 160, 80), Runtime.CurrentRuntime.Scale);
        RectangleShift = new Vector2(300, 40) * (float)Runtime.CurrentRuntime.Scale;
    }

    private float TargetHeight;
    
    private static Rectangle RectangleSelectionSource = new Rectangle(0, 0, 200, 200);
    private Rectangle RectangleSelectionTarget;

    private static Rectangle RectangleSourceDifficulty = new Rectangle(0, 0, 960, 480);
    private Rectangle RectangleDestinationDifficultySelect;
    private Rectangle RectangleDestinationDifficultySelected;
    private Vector2 RectangleShift;
    
    public override void CreateMenu()
    {
        if (GameType.HasFlag(GameType.Default))
        {
            Menu["Easy"] = (a, b) => OpenNext(0);
            Menu["Normal"] = (a, b) => OpenNext(1);
            Menu["Hard"] = (a, b) => OpenNext(2);
            Menu["Max"] = (a, b) => OpenNext(3);
        }
        if(GameType.HasFlag(GameType.Extra))
            Menu["Extra"] = (a, b) => OpenNext(4);
    }

    private bool IsFirst = true;
    
    public override void Activated()
    {
        TimeAppearItems = (float)GetTime();
        TimeDisappearItems = float.MaxValue;
        if (!IsFirst)
            TimeAppearSelected = (float)GetTime();
        else
            IsFirst = false;
        TimeDisappearSelected = float.MaxValue;
        base.Activated();
    }
    
    public override void Deactivated()
    {
        TimeDisappearSelected = (float)GetTime() + 0.5f;
        TimeDisappearItems = (float)GetTime() + 0.5f;
        base.Deactivated();
    }

    public override void Exiting()
    {
        TimeDisappearSelected = float.MaxValue;
        TimeDisappearItems = (float)GetTime() + 0.5f;
        base.Exiting();
    }

    void OpenNext(int difficulty)
    {
                Runtime.CurrentRuntime.AddScreen(new PersonSelectScreen(GameType, difficulty));
    }

    private float TimeAppearItems = float.MinValue;
    private float TimeDisappearItems = float.MaxValue;
    private float TimeAppearSelected = float.MinValue;
    private float TimeDisappearSelected = float.MaxValue;
    
    public override void Render()
    {
        DrawBackground();
        float appearState = (float)Helper.ComputeObjectTime(Raylib.GetTime(), TimeAppearItems, .5f, TimeDisappearItems, .5f);
        float appearSelected = (float)Helper.ComputeObjectTime(Raylib.GetTime(), TimeAppear, .5f, TimeDisappear, .5f);
        float appearSelected2 = (float)Helper.ComputeObjectTime(Raylib.GetTime(), TimeAppearSelected, .5f, TimeDisappearSelected, .5f);
        float index = (float)ComputeAnimationIndex();
        float angle = 15f - (index * 15f);
        float elasticAppear = Helper.EaseInOutElasticF(appearState);
        
        RectangleSelectionTarget.Height = TargetHeight * Helper.EaseInOutElasticF(appearState);
        
        
        int sourceIndex = GameType == GameType.Extra ? 4 : 0;
        int x = 0;
        DrawTexturePro(
            Runtime.CurrentRuntime.Textures["MenuItemSelectionGradient1"],
            RectangleSelectionSource, RectangleSelectionTarget, 
            Helper.Half * RectangleSelectionTarget.Size, angle+((1-appearState)*60f), 
            Color.White);
        for (; sourceIndex < Menu.Count; sourceIndex++)
        {
            if (x != SelectedIndex)
            {
                Helper.BeginContrastShader(1.0f, appearState);
                DrawTexturePro(
                    Runtime.CurrentRuntime.Textures["difficulties.png"],
                    RectangleSourceDifficulty with { Y = 480 * sourceIndex },
                    RectangleDestinationDifficultySelect with
                    {
                        Position = RectangleDestinationDifficultySelect.Position - (RectangleShift * (index-x) * elasticAppear) 
                    }, 
                    Vector2.Zero, 0f, 
                    Color.White);
                EndShaderMode();
            }
                
            x++;
        }

        var xindex = (SelectedIndex > index ? index % 1 - 1 : index % 1);
        
        var rectangleSelected = Helper.Mix(RectangleDestinationDifficultySelected,RectangleDestinationDifficultySelect with
        {
            Position = RectangleDestinationDifficultySelect.Position - RectangleShift * xindex * elasticAppear
        },  Helper.EaseInOutElasticF(appearSelected2));
        
        Helper.BeginContrastShader(MathF.Abs(SelectedIndex-index), appearSelected);
        DrawTexturePro(
            Runtime.CurrentRuntime.Textures["difficulties.png"],
            RectangleSourceDifficulty with { Y = GameType == GameType.Extra ? 1920 : 480 * SelectedIndex },
            rectangleSelected, 
            Vector2.Zero, 0f, 
            Color.White);
        EndShaderMode();
        DrawTitle();
    }
}
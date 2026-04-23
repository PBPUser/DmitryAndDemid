using System.Numerics;
using System.Runtime.CompilerServices;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Screens;

public class DifficultyScreen : MenuScreen
{
    bool IsDefault;
    bool IsExtra;
    /// <summary>
    /// 0 - Default game
    /// 1 - Extra
    /// 2 - Practice
    /// </summary>
    int Action;
    
    public DifficultyScreen(int action) : base()
    {
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        Action = action;
        IsDefault = action == 0 || action == 2;
        IsExtra = action == 1 || action == 2;
        LoopList = false;

        RectangleSelectionTarget = Helper.Scale(new Rectangle(310, 220, 220, 120), Runtime.CurrentRuntime.Scale);
        TargetHeight = RectangleSelectionTarget.Height;

        RectangleDestinationDifficultySelect = Helper.Scale(new Rectangle(200, 170, 240, 120), Runtime.CurrentRuntime.Scale);
        RectangleDestinationDifficultySelected = Helper.Scale(new Rectangle(260, 400, 120, 60), Runtime.CurrentRuntime.Scale);
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
        if (IsDefault)
        {
            Menu["Easy"] = (a, b) => OpenNext(0);
            Menu["Normal"] = (a, b) => OpenNext(1);
            Menu["Hard"] = (a, b) => OpenNext(2);
            Menu["Max"] = (a, b) => OpenNext(3);
        }
        if(IsExtra)
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
    }
    
    public override void Deactivated()
    {
        TimeDisappearSelected = (float)GetTime() + 0.5f;
        TimeDisappearItems = (float)GetTime() + 0.5f;
    }

    public override void Exiting()
    {
        TimeDisappearSelected = float.MaxValue;
        TimeDisappearItems = (float)GetTime() + 0.5f;
        base.Exiting();
    }

    void OpenNext(int difficulty)
    {
        switch (Action)
        {
            case 0:
                Runtime.CurrentRuntime.AddScreen(new PersonSelectScreen(false));
                break;
            case 1:
                Runtime.CurrentRuntime.AddScreen(new PersonSelectScreen(false));
                break;
            case 2:
                Runtime.CurrentRuntime.AddScreen(new PersonSelectScreen(true));
                break;
        }
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
        
        
        int sourceIndex = !IsDefault & IsExtra ? 4 : 0;
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
            RectangleSourceDifficulty with { Y = !IsDefault & IsExtra ? 1920 : 480 * SelectedIndex },
            rectangleSelected, 
            Vector2.Zero, 0f, 
            Color.White);
        EndShaderMode();
        DrawText($"SelectedIndex: {SelectedIndex},XIndex:{xindex},Index:{index},AppearSelected:{appearSelected},Contrast:{MathF.Abs(SelectedIndex-index)}", 0, 20, 20, Color.DarkBlue);
    }
}
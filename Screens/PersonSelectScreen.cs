using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Screens;

public class PersonSelectScreen : MenuScreen
{
    public GameType GameType;
    private int Difficulty;

    public PersonSelectScreen(GameType gameType, int difficulty) : base()
    {
        Difficulty = difficulty;
        HorizontalDirectionNavigation = true;
        VerticalDirectionNavigation = false;
        GameType = gameType;
        ArtDestination = Helper.Scale(new Rectangle(40, 80, 200, 400), Runtime.CurrentRuntime.Scale);
        DescriptionDestination = Helper.Scale(new Rectangle(320, 100, 280, 200), Runtime.CurrentRuntime.Scale);
        ArtShift = (float)(Runtime.CurrentRuntime.Scale * 40f);
        DescriptionShift = (float)(Runtime.CurrentRuntime.Scale * 30f);
        SetTitle(Runtime.CurrentRuntime.Textures["hero_select.png"]);
    }

    private static Rectangle RectangleSelectionSource = new Rectangle(0, 0, 200, 200);
    private static Rectangle ArtSource = new Rectangle(0, 0, 800, 1600);
    Rectangle ArtDestination;
    Rectangle DescriptionDestination;
    private float ArtShift;
    private float DescriptionShift;
    
    private Texture2D[] ArtTextures;
    private Texture2D[] DescriptionTextures;
    
    string[] Files;

    public override void CreateMenu()
    {
        Files = Directory.GetFiles("Assets/Data/PlayablePersons", "*.json");
        ArtTextures = new Texture2D[Files.Length];
        DescriptionTextures = new Texture2D[Files.Length];
        int i = 0;
        foreach (var x in Files)
        {
            Menu[Path.GetFileNameWithoutExtension(x)] = (a, b) => OpenNext();
            var json = JsonSerializer.Deserialize<ProtogonistData>(File.ReadAllText(x));
            ArtTextures[i] = Runtime.CurrentRuntime.Textures[json.ArtName];
            DescriptionTextures[i] = Runtime.CurrentRuntime.Textures[json.Description];
            i++;
        }
    }

    public override void Activated()
    {
        
        base.Activated();
    }
    
    public override void Deactivated()
    {
        base.Deactivated();
    }

    public override void Render()
    {
        float appear = (float)Helper.ComputeObjectTime(Raylib.GetTime(), TimeAppear, .5f, TimeDisappear, .5f);
        float invertedAppearElastic = Helper.EaseInOutElasticF(1 - appear);
        float index = (float)ComputeAnimationIndexLoop();
        for(int j = 0; j < Menu.Count; j++)
        {
            float position = ((index + 1 - j + Menu.Count) % Menu.Count)-1;
            float transparency = 1-Math.Abs(position);
            DrawTexturePro(
                Runtime.CurrentRuntime.Textures["MenuItemSelectionGradient1"], 
                RectangleSelectionSource, 
                DescriptionDestination with
                {
                    X = Raymath.Lerp(DescriptionDestination.X + position * ArtShift, (Runtime.CurrentRuntime.Width+DescriptionDestination.Width) / 2, invertedAppearElastic),
                    Width = Raymath.Lerp(DescriptionDestination.Width, 0,invertedAppearElastic)
                }, 
                Vector2.Zero, 
                (1-transparency) * 10f, 
                Color.White with {A = Helper.TimeToTransparency(appear * transparency)});
            DrawTexturePro(
                ArtTextures[j], 
                ArtSource, 
                ArtDestination with
                {
                    X = Raymath.Lerp(ArtDestination.X + position * ArtShift, (Runtime.CurrentRuntime.Width-ArtDestination.Width) / 2, invertedAppearElastic),
                    Width = Raymath.Lerp(ArtDestination.Width, 0,invertedAppearElastic)
                }, 
                Vector2.Zero, 
                0, 
                Color.White with {A = Helper.TimeToTransparency(appear * transparency)});
        }
        DrawTitle();
    }

    public override void Exiting()
    {
        TimeDisappear = (float)(0.5 + GetTime());
        base.Exiting();
    }

    void OpenNext()
    {
        string data = File.ReadAllText(Files[SelectedIndex]);
        var json = JsonSerializer.Deserialize<ProtogonistData>(data);
        if (json == null)
            throw new Exception();
        if (GameType == GameType.Practice)
            Runtime.CurrentRuntime.AddScreen(new PracticeScreen(json, Difficulty));
        else if (GameType == GameType.Default)
        {
            var gamePlayScreen = new GameplayScreen(json, Game.Stages[0], Difficulty);
            gamePlayScreen.Game.ContinueAfterStageEnds = true;
            Runtime.CurrentRuntime.AddScreen(gamePlayScreen);
        }
        else if (GameType == GameType.Extra)
        {
            var gamePlayScreen = new GameplayScreen(json, Game.Stages[3], Difficulty);
            gamePlayScreen.Game.ContinueAfterStageEnds = true;
            Runtime.CurrentRuntime.AddScreen(gamePlayScreen);
        }
                
        
    }
}

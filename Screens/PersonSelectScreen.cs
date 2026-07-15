using DmitryAndDemid.Rendering;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

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
        ArtDestination = Helper.Scale(new Rect(40, 80, 200, 400), Runtime.CurrentRuntime.Scale);
        DescriptionDestination = Helper.Scale(new Rect(320, 100, 280, 200), Runtime.CurrentRuntime.Scale);
        ArtShift = (float)(Runtime.CurrentRuntime.Scale * 40f);
        DescriptionShift = (float)(Runtime.CurrentRuntime.Scale * 30f);
        SetTitle(Runtime.CurrentRuntime.Textures["hero_select.png"]);
        if(gameType == GameType.SpellPractice)
            SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
    }

    private static Rect RectangleSelectionSource = new Rect(0, 0, 200, 200);
    private static Rect ArtSource = new Rect(0, 0, 800, 1600);
    Rect ArtDestination;
    Rect DescriptionDestination;
    private float ArtShift;
    private float DescriptionShift;
    
    private TextureHandle[] ArtTextures;
    private TextureHandle[] DescriptionTextures;
    
    string[] Files;

    public override void CreateMenu()
    {
        Files = Assets.Files("Assets/Data/PlayablePersons/", "*.json");
        ArtTextures = new TextureHandle[Files.Length];
        DescriptionTextures = new TextureHandle[Files.Length];
        int i = 0;
        foreach (var x in Files)
        {
            MenuItems.Add(new MenuItem(Path.GetFileNameWithoutExtension(x),"", a => OpenNext()));
            var json = JsonSerializer.Deserialize<ProtogonistData>(File.ReadAllText(x));
            ArtTextures[i] = Runtime.CurrentRuntime.Textures[json.ArtName];
            DescriptionTextures[i] = Runtime.CurrentRuntime.Textures[json.Description];
            i++;
        }
    }

    public override void Render()
    {
        DrawBackground();
        float appear = (float)Helper.ComputeObjectTime(GetTime(), TimeAppear, .5f, TimeDisappear, .5f);
        float invertedAppearElastic = Helper.EaseInOutElasticF(1 - appear);
        float index = (float)ComputeAnimationIndexLoop();
        for(int j = 0; j < MenuItems.Count; j++)
        {
            float position = ((index + 1 - j + MenuItems.Count) % MenuItems.Count)-1;
            float transparency = 1-Math.Abs(position);
            DrawTexturePro(
                Runtime.CurrentRuntime.Textures["MenuItemSelectionGradient1"], 
                RectangleSelectionSource, 
                DescriptionDestination with
                {
                    X = MathUtil.Lerp(DescriptionDestination.X + position * ArtShift, (Runtime.CurrentRuntime.Width+DescriptionDestination.Width) / 2, invertedAppearElastic),
                    Width = MathUtil.Lerp(DescriptionDestination.Width, 0,invertedAppearElastic)
                }, 
                Vector2.Zero, 
                (1-transparency) * 10f, 
                Rgba.White with {A = Helper.TimeToTransparency(appear * transparency)});
            DrawTexturePro(
                ArtTextures[j], 
                ArtSource, 
                ArtDestination with
                {
                    X = MathUtil.Lerp(ArtDestination.X + position * ArtShift, (Runtime.CurrentRuntime.Width-ArtDestination.Width) / 2, invertedAppearElastic),
                    Width = MathUtil.Lerp(ArtDestination.Width, 0,invertedAppearElastic)
                }, 
                Vector2.Zero, 
                0, 
                Rgba.White with {A = Helper.TimeToTransparency(appear * transparency)});
        }
        DrawTitle();
    }

    public override void Exiting()
    {
        TimeDisappear = (float)(0.5 + GetTime());
        base.Exiting();
    }

    /// <summary>
    /// Fade the art and description out when another screen (spell practice, practice, gameplay) opens on top
    /// of us. Without this only the title faded — ScreenWithTitle handles that — and the character art stayed
    /// at full opacity behind the screen above. Activated() on the way back resets TimeDisappear, so escaping
    /// back here fades it in again.
    /// </summary>
    public override void Deactivated()
    {
        TimeDisappear = (float)GetTime() + DisappearingTime;
        base.Deactivated();
    }

    void OpenNext()
    {
        string data = File.ReadAllText(Files[SelectedIndex]);
        var protogonistData = JsonSerializer.Deserialize<ProtogonistData>(data);
        if (protogonistData == null)
            throw new Exception();
        bool UseTLS = false;
        if (GameType == GameType.Practice)
            Runtime.CurrentRuntime.AddScreen(new PracticeScreen(protogonistData, Difficulty));
        else if (GameType == GameType.Default)
        {
            string stagePath = Assets.Files("Assets/Data/SpellCards")[0];
            var bitPackage = BitPackage.OpenStreamReadPackage(stagePath);
            UseTLS = true;
            var gamePlayScreen = new GameplayScreen(protogonistData, Difficulty, [FileStageInfo.Load(ref bitPackage)], 0, false);
            Runtime.CurrentRuntime.AddScreen(gamePlayScreen);
            bitPackage.Dispose();
        }
        else if (GameType == GameType.Extra)
        {
        }
        else
        {
            // SetProtogonistData existed but was never called — the spell-practice screen had no idea who
            // the player had picked.
            SpellPracticeScreen.Instance.SetProtogonistData(protogonistData, Difficulty);
            Runtime.CurrentRuntime.AddScreen(SpellPracticeScreen.Instance);
        }

        if (UseTLS)
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["swap"]);
            TiledLoadingScreen? tls = null;
            tls = new TiledLoadingScreen(3, 0.5, () =>
            {
                Runtime.CurrentRuntime.RemoveScreen(tls);
            }, true, 0);
            Runtime.CurrentRuntime.AddScreen(tls);
        }
    }
}

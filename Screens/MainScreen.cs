using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Screens;

public class MainScreen : MenuScreen
{

    int TitleIndex = 0;
    int selectedIndex = 0;
    Texture2D SelectedPerson;

    Rectangle
        RCPersonSource,
        RCPersonTarget1,
        RCPersonTarget2;

    static string[] PersonImageNames = { "demid_2.png", "dima.png" };

    public override void Unload()
    {

    }

    public MainScreen() : base()
    {
        AllowExitWithEscape = false;

        Size = (int)(Runtime.CurrentRuntime.Scale * 32);
        AppearTime = GetTime();
        LogoTargetLeft = new Rectangle(0, 0, (float)(130 * Runtime.CurrentRuntime.Scale), (float)(95 * Runtime.CurrentRuntime.Scale));
        LogoTargetRight = new Rectangle(Runtime.CurrentRuntime.Width - (float)(135 * Runtime.CurrentRuntime.Scale), 0, (float)(135 * Runtime.CurrentRuntime.Scale), (float)(52 * Runtime.CurrentRuntime.Scale));

        Configuration.Config.FastLoading = true;
        Configuration.Config.Save();
        
        TitleIndex = new Random().Next(0, PersonImageNames.Count());
        SelectedPerson = Runtime.CurrentRuntime.Textures[PersonImageNames[TitleIndex]];

        int titlePersonHeight = (int)(360 * Runtime.CurrentRuntime.Scale);
        int titlePersonY = (int)(120 * Runtime.CurrentRuntime.Scale);
        int titlePersonWidth = (int)(360 * SelectedPerson.Height / SelectedPerson.Height * Runtime.CurrentRuntime.Scale);

        RCPersonTarget1 = new Rectangle(Runtime.CurrentRuntime.Width, titlePersonY, titlePersonWidth, titlePersonHeight);
        RCPersonTarget2 = new Rectangle(Runtime.CurrentRuntime.Width - titlePersonWidth, titlePersonY, titlePersonWidth, titlePersonHeight);
        RCPersonSource = new Rectangle(0, 0, SelectedPerson.Width, SelectedPerson.Height);

        int logoCenterWidth = (int)(250 * Runtime.CurrentRuntime.Scale);
        int logoCenterHeight = (int)(100 * Runtime.CurrentRuntime.Scale);
        int logoCenterX = (int)((Runtime.CurrentRuntime.Width - logoCenterWidth) * 0.45f);
        LogoTargetCenter1 = new Rectangle(logoCenterX, -logoCenterHeight, logoCenterWidth, logoCenterHeight * 0.7f);
        LogoTargetCenter2 = new Rectangle(logoCenterX, (int)(Runtime.CurrentRuntime.Scale * 32), logoCenterWidth, logoCenterHeight);
        CurrentY = (int)(192 * Runtime.CurrentRuntime.Scale);
    }

    static Color DarkRed = new Color(0.7f, 0, 0, 1);
    static Rectangle LogoSourceLeft = new Rectangle(0, 0, 260, 190);
    static Rectangle LogoSourceRight = new Rectangle(810, 0, 270, 105);
    static Rectangle LogoSourceCenter = new Rectangle(0, 0, 1000, 400);


    Rectangle LogoTargetLeft;
    Rectangle LogoTargetRight;
    Rectangle LogoTargetCenter1;
    Rectangle LogoTargetCenter2;

    int Size;
    double AppearTime;

    double
        TimeAppearMenu = 5.5, TimeDisappearMenu = 999999999;

    public override void Activated()
    {
        TimeAppearMenu = Math.Max(5.5, GetTime() - AppearTime);
        TimeDisappearMenu = 99999999999;
    }

    public override void Deactivated()
    {
        TimeDisappearMenu = GetTime() - AppearTime + 0.5;
    }

    public override void Render()
    {
        var z = GetTime() - AppearTime;
        float
            appear1 = (float)Helper.ComputeObjectTimeStart(z, 5, 1),
            appear2 = (float)Helper.ComputeObjectTimeStart(z, 1, 1),
            appear25 = (float)Helper.ComputeObjectTimeStart(z, 2, 0.5),
            appear3 = (float)Helper.ComputeObjectTime(z, TimeAppearMenu, 0.9, TimeDisappearMenu, 0.5),
            appear4 = (float)Helper.ComputeObjectTime(z, TimeAppearMenu, 0.25, TimeDisappearMenu, 0.5),
            appear5 = (float)Helper.ComputeObjectTime(z, TimeAppearMenu, 0.5, TimeDisappearMenu, 0.5);
        ;
        float time = (float)GetTime();
#if DEBUG
        var bg = Color.White;
#else
        var bg = Helper.Mix(Color.Black, Color.White, (float)appear2);
#endif
        DrawRectangle(0, 0, Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height, bg);
        var color1 = Helper.Mix(Color.Black, Color.Red, (float)appear2);
        var color2 = Helper.Mix(Color.Black, DarkRed, (float)appear2);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["game_logo.png"], LogoSourceCenter, Helper.Mix(LogoTargetCenter1, LogoTargetCenter2, Helper.EaseInOutElasticF(appear3)), Vector2.Zero, 0f, Color.White);
        CurrentX = (int)((16 - (Helper.Pow2F(1 - appear5) * 384)) * Runtime.CurrentRuntime.Scale);
#if DEBUG
        CurrentX = 0;
#endif
        DrawMenu();
        Helper.DrawWave(color1, MathF.Sin(time) + 1.5f, -0.7f - Helper.EaseInOutElasticF(appear1) * 1.5f, 1.5f, 1.5f, Runtime.CurrentRuntime.FullScreenRect);
        Helper.DrawWave(color2, MathF.Sin(time + 0.1f) + 1.5f, -0.7f - Helper.EaseInOutElasticF(appear3) * 1.5f, 1.5f, 1.5f, Runtime.CurrentRuntime.FullScreenRect);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["telecom.png"], LogoSourceLeft, LogoTargetLeft, Vector2.Zero, 0f, Color.White with { A = Helper.TimeToTransparency(appear25) });
        DrawTexturePro(Runtime.CurrentRuntime.Textures["telecom.png"], LogoSourceRight, LogoTargetRight, Vector2.Zero, 0f, Color.White with { A = Helper.TimeToTransparency(appear25) });
        DrawTexturePro(SelectedPerson, RCPersonSource, Helper.Mix(RCPersonTarget1, RCPersonTarget2, appear4), Vector2.Zero, 0f, Color.White);
        #if DEBUG
        var hp = Helper.GetDirection(new Vector2(640, 260), GetMousePosition());
        DrawLine(640, 260, 640+(int)(hp.X * 500), 260+(int)(hp.Y * 500), Color.Blue);
        DrawText($"Test: {hp}", 0, 220, 20, Color.Red);
        DrawText($"Coords: {hp*500}", 0, 240, 20, Color.Red);
        #endif
    }

    public override void CreateMenu()
    {
        Menu["menu.start"] = (a, b) =>
        {
            Runtime.CurrentRuntime.AddAction(() => Runtime.CurrentRuntime.AddScreen(new DifficultyScreen(0)));
        };
        Menu["menu.extra"] = (a, b) => 
        {
            Runtime.CurrentRuntime.AddAction(() => Runtime.CurrentRuntime.AddScreen(new DifficultyScreen(1)));
        };
        Menu["menu.practice"] = (a, b) =>
        {
            Runtime.CurrentRuntime.AddAction(() => Runtime.CurrentRuntime.AddScreen(new DifficultyScreen(2)));
        };
        Menu["menu.trophy"] = (a, b) =>
        {
            Runtime.CurrentRuntime.AddAction(() => Runtime.CurrentRuntime.AddScreen(new DifficultyScreen(3)));
        };
        Menu["menu.music"] = (a, b) =>
        {
            Runtime.CurrentRuntime.AddAction(() => Runtime.CurrentRuntime.AddScreen(new DifficultyScreen(3)));
        };
        Menu["menu.replay"] = (a, b) =>
        {
            Runtime.CurrentRuntime.AddAction(() => Runtime.CurrentRuntime.AddScreen(new DifficultyScreen(3)));
        };
        Menu["menu.stats"] = (a, b) =>
        {
            Runtime.CurrentRuntime.AddAction(() => Runtime.CurrentRuntime.AddScreen(new DifficultyScreen(3)));
        };
#if DEBUG
        Menu["Gameplay Editor"] = (a, b) => { };
#endif
        Menu["menu.spell"] = (a, b) =>
        {
            //Runtime.CurrentRuntime.AddAction(() => Runtime.CurrentRuntime.AddScreen(new PersonSelectScreen(true)));
        };
        Menu["menu.settings"] = (a, b) =>
        {
            Runtime.CurrentRuntime.AddAction(() => Runtime.CurrentRuntime.AddScreen(new SettingsScreen()));
        };
        Menu["menu.exit"] = (a, b) => { Environment.Exit(0); };
    }

    public override void PreRender(double delta)
    {
        if (GetTime() - AppearTime < 6)
            return;
    }
}

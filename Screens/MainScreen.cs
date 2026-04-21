using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Screens;

public class MainScreen : Screen
{
    Dictionary<string, EventHandler<int>> Menu = new();

    int selectedIndex = 0;
    RenderTexture2D?[] MenuItems;

    public override void Unload()
    {

    }

    public MainScreen()
    {
        Size = (int)(Runtime.CurrentRuntime.Scale * 32);
        AppearTime = GetTime();
        LogoTargetLeft = new Rectangle(0, 0, (float)(130 * Runtime.CurrentRuntime.Scale), (float)(95 * Runtime.CurrentRuntime.Scale));
        LogoTargetRight = new Rectangle(Runtime.CurrentRuntime.Width - (float)(135 * Runtime.CurrentRuntime.Scale), 0, (float)(135 * Runtime.CurrentRuntime.Scale), (float)(52 * Runtime.CurrentRuntime.Scale));

        Menu[""] = (a, b) => Environment.Exit(0);
        Menu[""] = (a, b) => Environment.Exit(0);

        MenuItems = new RenderTexture2D?[Menu.Count()];

        for (int i = 0; i < Menu.Count(); i++)
        {
            MenuItems[i] = DrawMenuItem(Menu.Keys.ElementAt(i));
        }
    }

    static RenderTexture2D? DrawMenuItem(string text)
    {
        return Helper.DrawText(text, 8, 8);
    }

    static Color DarkRed = new Color(0.7f, 0, 0, 1);
    static Rectangle LogoSourceLeft = new Rectangle(0, 0, 260, 190);
    static Rectangle LogoSourceRight = new Rectangle(810, 0, 270, 105);

    Rectangle LogoTargetLeft;
    Rectangle LogoTargetRight;

    int Size;
    double AppearTime;

    public override void Render()
    {
        var z = GetTime() - AppearTime;
        float
            appear1 = (float)Helper.ComputeObjectTimeStart(z, 5, 1),
            appear2 = (float)Helper.ComputeObjectTimeStart(z, 1, 1),
            appear25 = (float)Helper.ComputeObjectTimeStart(z, 2, 0.5),
            appear3 = (float)Helper.ComputeObjectTimeStart(z, 5, 0.9),
            appear4 = (float)Helper.ComputeObjectTimeStart(z, 5.75, 0.25)
        ;
        float time = (float)GetTime();
        var bg = Helper.Mix(Color.Black, Color.White, (float)appear2);
        DrawRectangle(0, 0, Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height, bg);
        var color1 = Helper.Mix(Color.Black, Color.Red, (float)appear2);
        var color2 = Helper.Mix(Color.Black, DarkRed, (float)appear2);
        Helper.DrawWave(color1, MathF.Sin(time) + 1.5f, -0.7f - Helper.EaseInOutElasticF(appear1) * 1.5f, 1.5f, 1.5f, Runtime.CurrentRuntime.FullScreenRect);
        Helper.DrawWave(color2, MathF.Sin(time + 0.1f) + 1.5f, -0.7f - Helper.EaseInOutElasticF(appear3) * 1.5f, 1.5f, 1.5f, Runtime.CurrentRuntime.FullScreenRect);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["telecom.png"], LogoSourceLeft, LogoTargetLeft, Vector2.Zero, 0f, Color.White with { A = Helper.TimeToTransparency(appear25) });
        DrawTexturePro(Runtime.CurrentRuntime.Textures["telecom.png"], LogoSourceRight, LogoTargetRight, Vector2.Zero, 0f, Color.White with { A = Helper.TimeToTransparency(appear25) });

        selectedIndex = 0;
        foreach (var x in MenuItems)
        {

        }
    }

    public override void PreRender(double delta)
    {
        if (GetTime() - AppearTime < 6)
            return;

    }
}

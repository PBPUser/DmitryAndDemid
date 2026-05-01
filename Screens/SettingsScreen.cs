using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Screens;

public class SettingsScreen : MenuScreen
{
    public SettingsScreen()
    {
        
    }

    public override void Exiting()
    {
        Configuration.Config.SFXVolume = Runtime.CurrentRuntime.SFXVolume;
        Configuration.Config.MusicVolume = Runtime.CurrentRuntime.MusicVolume;
        Configuration.Config.Save();
        base.Exiting();
    }

    public override void CreateMenu()
    {
        SetTitle(Runtime.CurrentRuntime.Textures["settings.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        MenuItems.Add(new MenuItem("settings.sfx", "", a => {}));
        MenuItems.Add(new MenuItem("settings.music", "", a => {}));
        MenuItems.Add(new MenuItem("settings.fullscreen", "", a => {}));
        MenuItems.Add(new MenuItem("settings.vsync", "", a => {}));
        MenuItems.Add(new MenuItem("settings.controller", "", a => {}));
        MenuItems.Add(new MenuItem("settings.default", "", a => {}));
        MenuItems.Add(new MenuItem("ingame.exit", "", a => {}));
        CurrentX = (int)(Runtime.CurrentRuntime.Scale * 32);
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 192);
    }

    public override void Render()
    {
        float time = (float)Raylib.GetTime();
        CurrentY = (int)(Runtime.CurrentRuntime.Height*(1 - Helper.EaseInOutElasticF((float)(Helper.ComputeObjectTime(time, TimeAppear, 1f, TimeDisappear, 1f)*0.5))));
        DrawBackground();
        DrawMenu();
        DrawTitle();
    }
}
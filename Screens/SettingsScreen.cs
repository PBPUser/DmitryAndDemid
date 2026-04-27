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
        Menu["settings.sfx_down"] = (a, b) => Runtime.CurrentRuntime.SFXVolume = Math.Max(Runtime.CurrentRuntime.SFXVolume-0.1f, 0);
        Menu["settings.sfx_up"] = (a, b) => Runtime.CurrentRuntime.SFXVolume = Math.Min(Runtime.CurrentRuntime.SFXVolume+0.1f, 1);
        Menu["settings.music_down"] = (a, b) =>
        {
            Runtime.CurrentRuntime.MusicVolume = Math.Max(Runtime.CurrentRuntime.SFXVolume - 0.1f, 0);
            Helper.UpdatePlayingMusic();
        };
        Menu["settings.music_up"] = (a, b) =>
        {
            Runtime.CurrentRuntime.MusicVolume -= Math.Min(Runtime.CurrentRuntime.SFXVolume + 0.1f, 1);
            Helper.UpdatePlayingMusic();
        };
        CurrentX = (int)(Runtime.CurrentRuntime.Scale * 32);
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 256);
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
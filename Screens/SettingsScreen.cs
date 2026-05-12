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

    public override void Deactivated()
    {
        TimeDisappear = (float)Raylib.GetTime() + 1f;
        base.Deactivated();
    }

    public override void CreateMenu()
    {
        SetTitle(Runtime.CurrentRuntime.Textures["settings.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        MenuItems.Add(new MenuItem("settings.sfx", $"{Configuration.Config.SFXVolume * 100:00}", a => {}));
        MenuItems.Add(new MenuItem("settings.music", $"{Configuration.Config.MusicVolume * 100:00}", a => {}));
        MenuItems.Add(new MenuItem("settings.fullscreen", $"{Configuration.Config.FullScreenType}", a => {}));
        MenuItems.Add(new MenuItem("settings.vsync", $"{Configuration.Config.UseVSYNC}",
            a =>
            {
                Configuration.Config.UseVSYNC = !Configuration.Config.UseVSYNC;
                Configuration.Config.Save();
                if(Configuration.Config.UseVSYNC)
                    Raylib.SetWindowState(ConfigFlags.VSyncHint);
                else
                    Raylib.ClearWindowState(ConfigFlags.VSyncHint);
                MenuItems[3].Replace = $"{Configuration.Config.UseVSYNC}";
            }));
        MenuItems.Add(new MenuItem("settings.framerate", $"{Configuration.Config.FrameCap}", a =>
        {
            
        }));
        MenuItems.Add(new MenuItem("settings.controller", "", a => Runtime.CurrentRuntime.AddScreen(new GamepadSettingsScreen())));
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

    private double LastTimeShootSoundTestPlayed = 0;
    private const double TimeShootSoundTestDelay = 0.75;
    
    public override void TopUpdate()
    {
        base.TopUpdate();
        double time = Raylib.GetTime();
        if (SelectedIndex == 0 && LastTimeShootSoundTestPlayed + TimeShootSoundTestDelay < time)
        {
            LastTimeShootSoundTestPlayed = time;
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["dead"]);
        }

        if (time > PreviousKeyTimestamp + MenuSwitchCooldown)
        {
            float delta = 0;
            if (Controller.IsButtonDown(GamepadButton.LeftFaceLeft) || Raylib.IsKeyDown(KeyboardKey.Left))
                delta -= .05f;
            if (Controller.IsButtonDown(GamepadButton.LeftFaceRight) || Raylib.IsKeyDown(KeyboardKey.Right))
                delta += .05f;
            if (delta == 0)
                return;
            AnimationStartedAt = PreviousKeyTimestamp = time;
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
            switch (SelectedIndex)
            {
                case 0:
                    Runtime.CurrentRuntime.SFXVolume = Configuration.Config.SFXVolume = Math.Clamp(Runtime.CurrentRuntime.SFXVolume + delta, 0, 1);  
                    MenuItems[0].Replace = $"{Configuration.Config.SFXVolume*100:00}";
                    Configuration.Config.Save();
                    break;
                case 1:
                    Runtime.CurrentRuntime.MusicVolume = Configuration.Config.MusicVolume = Math.Clamp(Runtime.CurrentRuntime.MusicVolume + delta, 0, 1);  
                    MenuItems[1].Replace = $"{Configuration.Config.MusicVolume*100:00}";
                    Configuration.Config.Save();
                    break;
                case 4:
                    delta *= 600;
                    Configuration.Config.FrameCap = (int)(Configuration.Config.FrameCap + delta);
                    if (Configuration.Config.FrameCap < 1)
                        Configuration.Config.FrameCap = -1;
                    else if(Configuration.Config.FrameCap > 1 && Configuration.Config.FrameCap < 30)
                        Configuration.Config.FrameCap = 30;
                    Raylib.SetTargetFPS(Configuration.Config.FrameCap);
                    MenuItems[4].Replace = $"{Configuration.Config.FrameCap}";
                    Configuration.Config.Save();
                    Runtime.CurrentRuntime.IsFrameCap240 =  Configuration.Config.FrameCap == 240;
                    break;
            }
        }
    }
}
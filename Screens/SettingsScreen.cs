using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;

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
        TimeDisappear = (float)GetTime() + 1f;
        base.Deactivated();
    }

    public override void CreateMenu()
    {
        SetTitle(Runtime.CurrentRuntime.Textures["settings.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        MenuItems.Add(new MenuItem("settings.sfx", $"{Configuration.Config.SFXVolume * 100:00}", a => {}));
        MenuItems.Add(new MenuItem("settings.music", $"{Configuration.Config.MusicVolume * 100:00}", a => {}));
        MenuItems.Add(new MenuItem("settings.fullscreen", $"{Configuration.Config.FullScreenType}",
            a => CycleWindowMode(1)));
        MenuItems.Add(new MenuItem("settings.vsync", $"{Configuration.Config.UseVSYNC}",
            a =>
            {
                Configuration.Config.UseVSYNC = !Configuration.Config.UseVSYNC;
                Configuration.Config.Save();
                Engine.Platform.SetVSync(Configuration.Config.UseVSYNC);
                MenuItems[3].Replace = $"{Configuration.Config.UseVSYNC}";
            }));
        MenuItems.Add(new MenuItem("settings.framerate", $"{Configuration.Config.FrameCap}", a =>
        {

        }));
        // Left/Right picks the renderer; Enter restarts into it. It cannot be swapped live — every texture,
        // shader and render target is owned by the backend, and the window itself is created by it.
        MenuItems.Add(new MenuItem("settings.renderer", RendererLabel(), a => ApplyRenderer()));
        MenuItems.Add(new MenuItem("settings.controller", "", a => Runtime.CurrentRuntime.AddScreen(new GamepadSettingsScreen())));
        MenuItems.Add(new MenuItem("settings.default", "", a => {}));
        MenuItems.Add(new MenuItem("ingame.exit", "", a => Exit()));
        CurrentX = (int)(Runtime.CurrentRuntime.Scale * 32);
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 192);
    }

    public override void Render()
    {
        float time = (float)GetTime();
        CurrentY = (int)(Runtime.CurrentRuntime.Height*(1 - Helper.EaseInOutElasticF((float)(Helper.ComputeObjectTime(time, TimeAppear, 1f, TimeDisappear, 1f)*0.5))));
        DrawBackground();
        DrawMenu();
        DrawTitle();
    }

    private static readonly FullScreenType[] WindowModes =
    [
        FullScreenType.Window,
        FullScreenType.Borderless,
        FullScreenType.BorderlessDotByDot,
        FullScreenType.Exclusive,
    ];

    /// <summary>Steps through the presentation modes and applies the new one immediately.</summary>
    void CycleWindowMode(int direction)
    {
        int index = Array.IndexOf(WindowModes, Configuration.Config.FullScreenType);
        if (index < 0)
            index = 0;
        index = (index + direction + WindowModes.Length) % WindowModes.Length;

        Runtime.CurrentRuntime.SetWindowMode(WindowModes[index]); // persists to config itself
        MenuItems[2].Replace = $"{WindowModes[index]}";
    }

    private const int RendererItemIndex = 5;

    /// <summary>
    /// Config value -> display name. Keep the KEYS in step with Engine.Create; the names are for the player.
    /// They go through Helper.Translate, so the Cyrillic gets transliterated into the game's usual lettering.
    /// </summary>
    private static readonly (string Key, string Display)[] Renderers =
    [
        ("raylib", "ЛУЧЕПРОВОД"),      // Raylib — "ray pipeline"
        ("silk", "ШЁЛКОВЫЙ ПУТЬ"),      // Silk.NET/OpenGL — "the Silk Road"
        ("vulkan", "ВИЛКАН"),           // Vulkan — the fork
    ];

    /// <summary>The real backend names come from the registry — the launcher builds itself from the same one.</summary>
    private static (string Key, string Name)[] RendererBackends => Engine.Available;

    /// <summary>
    /// Shows the selected renderer, marked with * when it differs from the one currently running — i.e.
    /// when a restart is pending.
    /// </summary>
    static string RendererLabel()
    {
        string key = Configuration.Config.Renderer;
        string display = Renderers.FirstOrDefault(r => r.Key == key).Display ?? key;

        // Compare against the REAL backend name, not the joke one.
        string real = RendererBackends.FirstOrDefault(r => r.Key == key).Name ?? key;
        bool restartPending = !string.Equals(real, Engine.BackendName, StringComparison.OrdinalIgnoreCase);
        return restartPending ? display + " *" : display;
    }

    void CycleRenderer(int direction)
    {
        int index = Array.FindIndex(Renderers, r => r.Key == Configuration.Config.Renderer);
        if (index < 0)
            index = 0;
        index = (index + direction + Renderers.Length) % Renderers.Length;

        Configuration.Config.Renderer = Renderers[index].Key;
        Configuration.Config.Save();
        MenuItems[RendererItemIndex].Replace = RendererLabel();
    }

    /// <summary>
    /// Restarts the process on the selected renderer. The backend owns the window and every GPU resource,
    /// so switching in-place would mean tearing down and reloading all of them; relaunching is both simpler
    /// and what the player expects from a renderer switch.
    /// </summary>
    void ApplyRenderer()
    {
        if (RendererLabel().EndsWith("*") == false)
            return; // already running the selected renderer — nothing to do

        string? exe = Environment.ProcessPath;
        if (exe == null)
            return;

        Configuration.Config.Save();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
        {
            Arguments = $"--renderer={Configuration.Config.Renderer}",
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
        });
        Environment.Exit(0);
    }

    private double LastTimeShootSoundTestPlayed = 0;
    private const double TimeShootSoundTestDelay = 0.75;
    
    public override void TopUpdate()
    {
        base.TopUpdate();
        double time = GetTime();
        if (SelectedIndex == 0 && LastTimeShootSoundTestPlayed + TimeShootSoundTestDelay < time)
        {
            LastTimeShootSoundTestPlayed = time;
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["dead"]);
        }

        if (time > PreviousKeyTimestamp + MenuSwitchCooldown)
        {
            float delta = 0;
            if (Controller.IsButtonDown(PadButton.LeftFaceLeft) || IsKeyDown(KeyCode.Left))
                delta -= .05f;
            if (Controller.IsButtonDown(PadButton.LeftFaceRight) || IsKeyDown(KeyCode.Right))
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
                case 2:
                    CycleWindowMode(delta > 0 ? 1 : -1);
                    break;
                case RendererItemIndex:
                    CycleRenderer(delta > 0 ? 1 : -1);
                    break;
                case 4:
                    delta *= 600;
                    Configuration.Config.FrameCap = (int)(Configuration.Config.FrameCap + delta);
                    if (Configuration.Config.FrameCap < 1)
                        Configuration.Config.FrameCap = -1;
                    else if(Configuration.Config.FrameCap > 1 && Configuration.Config.FrameCap < 30)
                        Configuration.Config.FrameCap = 30;
                    SetTargetFPS(Configuration.Config.FrameCap);
                    MenuItems[4].Replace = $"{Configuration.Config.FrameCap}";
                    Configuration.Config.Save();
                    Runtime.CurrentRuntime.IsFrameCap240 =  Configuration.Config.FrameCap == 240;
                    break;
            }
        }
    }
}
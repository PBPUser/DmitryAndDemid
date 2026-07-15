using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

public class SettingsScreen : MenuScreen
{
    // The adjustable rows, referenced directly so the left/right handler and the label updates do not depend
    // on positions that shift between platforms. Window/renderer are null on Android (those rows are absent).
    private MenuItem? SfxItem, MusicItem, WindowItem, RendererItem, FramerateItem;

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

        // Items are matched by REFERENCE below, not by index — the rows present differ per platform (Android
        // has no window mode and no renderer switch), and hard indices silently broke when a row was dropped.
        SfxItem = new MenuItem("settings.sfx", $"{Configuration.Config.SFXVolume * 100:00}", a => {});
        MenuItems.Add(SfxItem);
        MusicItem = new MenuItem("settings.music", $"{Configuration.Config.MusicVolume * 100:00}", a => {});
        MenuItems.Add(MusicItem);
#if !ANDROID
        // The window mode is meaningless on Android — the game always owns the full surface — so this row and
        // the renderer switch (which relaunches the process) are desktop-only.
        WindowItem = new MenuItem("settings.fullscreen", $"{Configuration.Config.FullScreenType}",
            a => CycleWindowMode(1));
        MenuItems.Add(WindowItem);
#endif
        MenuItem vsyncItem = new("settings.vsync", $"{Configuration.Config.UseVSYNC}", null);
        vsyncItem.Action = a =>
        {
            Configuration.Config.UseVSYNC = !Configuration.Config.UseVSYNC;
            Configuration.Config.Save();
            Engine.Platform.SetVSync(Configuration.Config.UseVSYNC);
            vsyncItem.Replace = $"{Configuration.Config.UseVSYNC}";
        };
        MenuItems.Add(vsyncItem);
        FramerateItem = new MenuItem("settings.framerate", $"{Configuration.Config.FrameCap}", a => {});
        MenuItems.Add(FramerateItem);
#if !ANDROID
        // Left/Right picks the renderer; Enter restarts into it. It cannot be swapped live — every texture,
        // shader and render target is owned by the backend, and the window itself is created by it.
        RendererItem = new MenuItem("settings.renderer", RendererLabel(), a => ApplyRenderer());
        MenuItems.Add(RendererItem);
#endif
        MenuItems.Add(new MenuItem("settings.controller", "", a => Runtime.CurrentRuntime.AddScreen(new GamepadSettingsScreen())));
        MenuItems.Add(new MenuItem("settings.default", "", a => {}));
        MenuItems.Add(new MenuItem("ingame.exit", "", a => Exit()));
        CurrentX = (int)(Runtime.CurrentRuntime.Scale * 32);
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 192);
    }

    private TargetHandle RendererNotice;

    public override void Render()
    {
        float time = (float)GetTime();
        CurrentY = (int)(Runtime.CurrentRuntime.Height*(1 - Helper.EaseInOutElasticF((float)(Helper.ComputeObjectTime(time, TimeAppear, 1f, TimeDisappear, 1f)*0.5))));
        DrawBackground();
        DrawMenu();
        DrawTitle();

        // Attention when the renderer was just changed: it only takes effect on restart, so flash a red notice
        // for a few seconds rather than let the change pass silently.
        float since = time - RendererChangedNotice;
        if (since is >= 0 and < 5f)
        {
            if (RendererNotice.Id == 0)
                RendererNotice = Helper.DrawTextScaled(Helper.Translate("settings.renderer_restart"), 18, 6, 4, 2,
                    Runtime.CurrentRuntime.Fonts["newsreader"], "outline");
            float blink = 0.5f + 0.5f * MathF.Sin(time * 8f);
            var tex = RendererNotice.Texture;
            DrawTexture(tex,
                (Runtime.CurrentRuntime.Width - tex.Width) / 2,
                (int)(Runtime.CurrentRuntime.Height - tex.Height - 24 * Runtime.CurrentRuntime.ScaleF),
                new Rgba(255, 60, 60, (byte)(255 * blink)));
        }
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
        if (WindowItem != null)
            WindowItem.Replace = $"{WindowModes[index]}";
    }

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
        if (RendererItem != null)
            RendererItem.Replace = RendererLabel();
        RendererChangedNotice = (float)GetTime();   // flag the "restart required" attention line
    }

    /// <summary>When the renderer was last changed, so Render can flash a "restart required" notice.</summary>
    private float RendererChangedNotice = float.MinValue;

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
        MenuItem selected = SelectedIndex >= 0 && SelectedIndex < MenuItems.Count ? MenuItems[SelectedIndex] : null!;

        if (selected == SfxItem && LastTimeShootSoundTestPlayed + TimeShootSoundTestDelay < time)
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

            if (selected == SfxItem)
            {
                Runtime.CurrentRuntime.SFXVolume = Configuration.Config.SFXVolume = Math.Clamp(Runtime.CurrentRuntime.SFXVolume + delta, 0, 1);
                SfxItem.Replace = $"{Configuration.Config.SFXVolume * 100:00}";
                Configuration.Config.Save();
            }
            else if (selected == MusicItem)
            {
                Runtime.CurrentRuntime.MusicVolume = Configuration.Config.MusicVolume = Math.Clamp(Runtime.CurrentRuntime.MusicVolume + delta, 0, 1);
                MusicItem!.Replace = $"{Configuration.Config.MusicVolume * 100:00}";
                Configuration.Config.Save();
            }
            else if (selected == WindowItem)
                CycleWindowMode(delta > 0 ? 1 : -1);
            else if (selected == RendererItem)
                CycleRenderer(delta > 0 ? 1 : -1);
            else if (selected == FramerateItem)
            {
                delta *= 600;
                Configuration.Config.FrameCap = (int)(Configuration.Config.FrameCap + delta);
                if (Configuration.Config.FrameCap < 1)
                    Configuration.Config.FrameCap = -1;
                else if (Configuration.Config.FrameCap > 1 && Configuration.Config.FrameCap < 30)
                    Configuration.Config.FrameCap = 30;
                SetTargetFPS(Configuration.Config.FrameCap);
                FramerateItem!.Replace = $"{Configuration.Config.FrameCap}";
                Configuration.Config.Save();
                Runtime.CurrentRuntime.IsFrameCap240 = Configuration.Config.FrameCap == 240;
            }
        }
    }
}
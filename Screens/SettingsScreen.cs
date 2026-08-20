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
    private MenuItem? SfxItem, MusicItem, WindowItem, RendererItem, FramerateItem, ResolutionItem;

    /// <summary>4:3 internal resolutions offered in-game. The configurator offers the same set.</summary>
    private static readonly string[] Resolutions =
        ["640x480", "800x600", "960x720", "1280x960", "1600x1200", "1920x1440", "2560x1920"];

    /// <summary>
    /// The resolutions actually offered on this device. On Switch the chosen value is the internal 4:3 backbuffer
    /// that gets letterboxed onto the panel, so offering more than the panel shows is pointless: cap it at 720
    /// tall handheld and 960 tall docked (a 4:3 960-tall frame still fits a 1080p dock output). Dock state comes
    /// from the real drawable height the backend reports (≈720 handheld / ≈1080 docked). Desktop keeps the full set.
    /// </summary>
    private static IEnumerable<string> AllowedResolutions()
    {
#if SWITCH
        int maxHeight = Engine.Platform.MonitorHeight >= 1000 ? 960 : 720;
        return Resolutions.Where(r => int.TryParse(r.Split('x')[1], out int h) && h <= maxHeight);
#else
        return Resolutions;
#endif
    }

    // The slider glyphs: a fixed-width bar like <====----->, so the row width never jumps as the value moves.
    private const int BarSegments = 16;

    private static string Bar(float fraction)
    {
        int filled = (int)MathF.Round(Math.Clamp(fraction, 0f, 1f) * BarSegments);
        return "<" + new string('=', filled) + new string('-', BarSegments - filled) + ">";
    }

    /// <summary>Opens a list to pick a resolution; applies on restart (a live change means reloading fonts).</summary>
    private void OpenResolutionList()
    {
        Runtime.CurrentRuntime.AddScreen(new ListSelectScreen(
            Runtime.CurrentRuntime.Textures["settings.png"],
            AllowedResolutions().Select(r => (r, (System.Action)(() =>
            {
                if (r == Configuration.Config.Resolution)
                    return;
                Configuration.Config.Resolution = r;
                Configuration.Config.Save();
                if (ResolutionItem != null)
                    ResolutionItem.Replace = r;
                RestartNotice = (float)GetTime();
            }))), windowed: true, headerKey: "settings.resolution.title"));
    }

    /// <summary>Opens a list to pick a renderer; applies on the next launch (the backend owns the window).</summary>
    private void OpenRendererList()
    {
        Runtime.CurrentRuntime.AddScreen(new ListSelectScreen(
            Runtime.CurrentRuntime.Textures["settings.png"],
            Renderers.Select(r => (Helper.Translate(r.Display), (System.Action)(() =>
            {
                if (r.Key == Configuration.Config.Renderer)
                    return;
                Configuration.Config.Renderer = r.Key;
                Configuration.Config.Save();
                if (RendererItem != null)
                    RendererItem.Replace = RendererLabel();
                RestartNotice = (float)GetTime();
            }))), windowed: true, headerKey: "settings.renderer.title"));
    }

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
        EnableScrolling = true;   // the settings list is longer than the screen — scroll to follow the cursor
        SetTitle(Runtime.CurrentRuntime.Textures["settings.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);

        // Items are matched by REFERENCE below, not by index — the rows present differ per platform (Android
        // has no window mode and no renderer switch), and hard indices silently broke when a row was dropped.
        // The list is grouped into four categories with non-selectable header rows (see AddHeader): Sound,
        // Controls, Graphics, Other. Navigation skips the disabled headers.

        // ---- SOUND ----
        AddHeader("settings.cat.sound");
        SfxItem = new MenuItem("settings.sfx", Bar(Configuration.Config.SFXVolume), a => {});
        MenuItems.Add(SfxItem);
        MusicItem = new MenuItem("settings.music", Bar(Configuration.Config.MusicVolume), a => {});
        MenuItems.Add(MusicItem);

        // ---- CONTROLS ----
        AddHeader("settings.cat.controls");
        // On-screen touch controls (playfield drag + BOMB/FOCUS). Live — no restart.
        MenuItem touchItem = new("settings.touch", $"{Configuration.Config.TouchControls}", null);
        touchItem.Action = a =>
        {
            Configuration.Config.TouchControls = !Configuration.Config.TouchControls;
            Configuration.Config.Save();
            touchItem.Replace = $"{Configuration.Config.TouchControls}";
        };
        MenuItems.Add(touchItem);
        // Hold-shoot-to-focus (auto slowdown). Live — no restart.
        MenuItem autoSlowItem = new("settings.autoslow", $"{Configuration.Config.AutoSlowdownOnShoot}", null);
        autoSlowItem.Action = a =>
        {
            Configuration.Config.AutoSlowdownOnShoot = !Configuration.Config.AutoSlowdownOnShoot;
            Configuration.Config.Save();
            autoSlowItem.Replace = $"{Configuration.Config.AutoSlowdownOnShoot}";
        };
        MenuItems.Add(autoSlowItem);
        // Reposition the on-screen controls and toggle the stick / shoot button. Opens a drag editor.
        MenuItems.Add(new MenuItem("settings.touch_layout", "",
            a => Runtime.CurrentRuntime.AddScreen(new TouchLayoutScreen())));
        MenuItems.Add(new MenuItem("settings.controller", "", a => Runtime.CurrentRuntime.AddScreen(new GamepadSettingsScreen())));

        // ---- GRAPHICS ----
        AddHeader("settings.cat.graphics");
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
        // Graphics quality: High draws every shader, Low turns off the spell-card + background shaders. Live.
        MenuItem graphicsItem = new("settings.graphics_quality", GraphicsQualityLabel(), null);
        graphicsItem.Action = a =>
        {
            Configuration.Config.HighGraphics = !Configuration.Config.HighGraphics;
            Configuration.Config.Save();
            graphicsItem.Replace = GraphicsQualityLabel();
        };
        MenuItems.Add(graphicsItem);
        // Portrait/vertical presentation. Changing it re-sizes the backbuffer and re-lays every screen, so it
        // applies on restart.
        MenuItem verticalItem = new("settings.vertical", $"{Configuration.Config.Vertical}", null);
        verticalItem.Action = a =>
        {
            Configuration.Config.Vertical = !Configuration.Config.Vertical;
            Configuration.Config.Save();
            verticalItem.Replace = $"{Configuration.Config.Vertical}";
            RestartNotice = (float)GetTime();
        };
        MenuItems.Add(verticalItem);
        FramerateItem = new MenuItem("settings.framerate", FramerateBar(), a => {});
        MenuItems.Add(FramerateItem);
        // Resolution and renderer are a fixed set of discrete choices, so Enter opens a list to pick from
        // rather than nudging a slider. Both apply on restart (the backbuffer/backend can't be rebuilt live).
        ResolutionItem = new MenuItem("settings.resolution", Configuration.Config.Resolution, a => OpenResolutionList());
        MenuItems.Add(ResolutionItem);
#if !ANDROID
        RendererItem = new MenuItem("settings.renderer", RendererLabel(), a => OpenRendererList());
        MenuItems.Add(RendererItem);
#endif

        // ---- OTHER ----
        AddHeader("settings.cat.other");
        // Point-of-collection hint line at the start of a run. Live — no restart; applies to the next run.
        MenuItem itemLineItem = new("settings.itemline", $"{Configuration.Config.ShowItemLineHint}", null);
        itemLineItem.Action = a =>
        {
            Configuration.Config.ShowItemLineHint = !Configuration.Config.ShowItemLineHint;
            Configuration.Config.Save();
            itemLineItem.Replace = $"{Configuration.Config.ShowItemLineHint}";
        };
        MenuItems.Add(itemLineItem);
        MenuItems.Add(new MenuItem("settings.menulag", $"{Configuration.Config.IsMenuLagEnabled}", a => { Configuration.Config.IsMenuLagEnabled = !Configuration.Config.IsMenuLagEnabled; }));
        MenuItems.Add(new MenuItem("settings.benchmark", "", a => Runtime.CurrentRuntime.AddScreen(new BenchmarkScreen())));
        MenuItems.Add(new MenuItem("settings.default", "", a => {}));
        MenuItems.Add(new MenuItem("ingame.exit", "", a => Exit()));

        // Start the cursor on the first real (enabled) row, not the Sound header.
        SelectedIndex = MenuItems.FindIndex(i => i.Enabled);
        CurrentX = (int)(Runtime.CurrentRuntime.Scale * 32);
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 192);
    }

    /// <summary>Adds a non-selectable category header row (disabled, so navigation skips it).</summary>
    private void AddHeader(string key) => MenuItems.Add(new MenuItem(key, "", null) { Enabled = false });

    /// <summary>The graphics-quality row's value: the translated HIGH / LOW label.</summary>
    private static string GraphicsQualityLabel() =>
        Helper.Translate(Configuration.Config.HighGraphics ? "graphics.high" : "graphics.low");

    private TargetHandle RestartNoticeTexture;

    public override void Render()
    {
        float time = (float)GetTime();
        CurrentY = (int)(Runtime.CurrentRuntime.Height*(1 - Helper.EaseInOutElasticF((float)(Helper.ComputeObjectTime(time, TimeAppear, 1f, TimeDisappear, 1f)*0.5))));
        DrawBackground();
        DrawMenu();
        DrawTitle();

        // Attention when a restart-only setting (renderer, resolution) was just changed: it takes effect on
        // restart, so flash a red notice for a few seconds rather than let the change pass silently.
        float since = time - RestartNotice;
        if (since is >= 0 and < 5f)
        {
            if (RestartNoticeTexture.Id == 0)
                RestartNoticeTexture = Helper.DrawTextScaled(Helper.Translate("settings.restart_required"), 18, 6, 4, 2,
                    Runtime.CurrentRuntime.Fonts["newsreader"], "outline");
            float blink = 0.5f + 0.5f * MathF.Sin(time * 8f);
            var tex = RestartNoticeTexture.Texture;
            DrawTexture(tex,
                (Runtime.CurrentRuntime.Width - tex.Width) / 2,
                (int)(Runtime.CurrentRuntime.Height - tex.Height - 24 * Runtime.CurrentRuntime.ScaleF),
                new Rgba(255, 60, 60, (byte)(255 * blink)));
        }
    }

    // The frame-rate presets a slider snaps to (-1 = uncapped).
    private static readonly int[] FrameCaps = [-1, 30, 60, 120, 144, 240];

    /// <summary>The frame-rate row's value: the bar, plus the fps for a real cap (uncapped shows the bar only).</summary>
    private static string FramerateBar()
    {
        int i = Array.IndexOf(FrameCaps, Configuration.Config.FrameCap);
        float frac = i < 0 ? 0.5f : i / (float)(FrameCaps.Length - 1);
        return Configuration.Config.FrameCap < 1 ? Bar(frac) : $"{Bar(frac)} {Configuration.Config.FrameCap}";
    }

    /// <summary>
    /// The value-nudge rows and how to read/write them as a 0..1 fraction, so the <c>&lt;===---&gt;</c> bars
    /// and touch-drag drive them uniformly. Resolution and renderer are NOT here — they are pick-from-a-list.
    /// </summary>
    private IEnumerable<(MenuItem? Item, Func<float> Get, Action<float> Set)> Sliders()
    {
        yield return (SfxItem, () => Configuration.Config.SFXVolume, f =>
        {
            Runtime.CurrentRuntime.SFXVolume = Configuration.Config.SFXVolume = f;
            SfxItem!.Replace = Bar(f);
            Configuration.Config.Save();
        });
        yield return (MusicItem, () => Configuration.Config.MusicVolume, f =>
        {
            Runtime.CurrentRuntime.MusicVolume = Configuration.Config.MusicVolume = f;
            MusicItem!.Replace = Bar(f);
            Configuration.Config.Save();
        });
        yield return (FramerateItem, () =>
        {
            int i = Array.IndexOf(FrameCaps, Configuration.Config.FrameCap);
            return i < 0 ? 0.5f : i / (float)(FrameCaps.Length - 1);
        }, f =>
        {
            int i = (int)MathF.Round(f * (FrameCaps.Length - 1));
            Configuration.Config.FrameCap = FrameCaps[Math.Clamp(i, 0, FrameCaps.Length - 1)];
            SetTargetFPS(Configuration.Config.FrameCap);
            FramerateItem!.Replace = FramerateBar();
            Runtime.CurrentRuntime.IsFrameCap240 = Configuration.Config.FrameCap == 240;
            Configuration.Config.Save();
        });
    }

    // A tall touch strip over each slider row, so dragging anywhere along it sets the value. In unscaled units.
    private const float SliderWidth = 340f;

    /// <summary>Drives the slider under the finger (only when touch is the input method).</summary>
    private void UpdateTouchSliders()
    {
        // Stand down while a finger is drag-scrolling the list: a vertical scroll that passes over a slider row
        // must not also drag its value.
        if (IsManualScrolling)
            return;
        if (!TouchActive || !TryGetTouchPoint(out System.Numerics.Vector2 p))
            return;
        float barW = SliderWidth * Runtime.CurrentRuntime.ScaleF;
        foreach ((MenuItem? item, Func<float> _, Action<float> set) in Sliders())
        {
            if (item == null)
                continue;
            Rect b = ItemBounds(MenuItems.IndexOf(item));
            if (b.Width <= 0)
                continue;
            if (p.Y >= b.Y && p.Y <= b.Y + b.Height && p.X >= CurrentX && p.X <= CurrentX + barW)
            {
                set(Math.Clamp((p.X - CurrentX) / barW, 0f, 1f));
                PreviousKeyTimestamp = GetTime();
                return;
            }
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

    /// <summary>When a restart-only setting was last changed, so Render can flash a "restart required" notice.</summary>
    private float RestartNotice = float.MinValue;

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
        UpdateTouchSliders();
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

            // Keyboard/pad left-right nudges the same rows the touch bars do, and shows the same <===---> bar.
            if (selected == SfxItem)
            {
                Runtime.CurrentRuntime.SFXVolume = Configuration.Config.SFXVolume = Math.Clamp(Runtime.CurrentRuntime.SFXVolume + delta, 0, 1);
                SfxItem.Replace = Bar(Configuration.Config.SFXVolume);
                Configuration.Config.Save();
            }
            else if (selected == MusicItem)
            {
                Runtime.CurrentRuntime.MusicVolume = Configuration.Config.MusicVolume = Math.Clamp(Runtime.CurrentRuntime.MusicVolume + delta, 0, 1);
                MusicItem!.Replace = Bar(Configuration.Config.MusicVolume);
                Configuration.Config.Save();
            }
            else if (selected == WindowItem)
                CycleWindowMode(delta > 0 ? 1 : -1);
            else if (selected == FramerateItem)
            {
                // Step through the same presets the slider snaps to.
                int i = Array.IndexOf(FrameCaps, Configuration.Config.FrameCap);
                if (i < 0)
                    i = 2;
                i = Math.Clamp(i + (delta > 0 ? 1 : -1), 0, FrameCaps.Length - 1);
                Configuration.Config.FrameCap = FrameCaps[i];
                SetTargetFPS(Configuration.Config.FrameCap);
                FramerateItem!.Replace = FramerateBar();
                Runtime.CurrentRuntime.IsFrameCap240 = Configuration.Config.FrameCap == 240;
                Configuration.Config.Save();
            }
            // Resolution and renderer are opened as lists (Enter/tap), not nudged here.
        }
    }
}
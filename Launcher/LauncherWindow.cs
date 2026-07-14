using DmitryAndDemid.Data;
using DmitryAndDemid.Rendering;
using Gtk;

namespace DmitryAndDemid.Launcher;

/// <summary>
/// The standalone configurator ("config", shipped beside the game). It does one thing: edit the config.json
/// the game reads. It does not launch the game and never loads a graphics backend.
///
/// Laid out like the classic Touhou custom.exe: loose checkboxes across the top, then two columns of framed
/// groups — settings on the left, the renderer choice with an explanation panel on the right — and the
/// buttons at the bottom right.
///
/// Configuration and RendererRegistry are source-linked from the game, so the schema and the renderer list
/// cannot drift apart.
/// </summary>
public sealed class LauncherWindow
{
    public readonly Window Window;

    // NOT field initialisers: those run before the constructor body, i.e. before Application.Init(), and
    // constructing a GTK widget before gtk_init() segfaults.
    private readonly List<(string Value, RadioButton Button)> ResolutionButtons = [];
    private readonly List<(FullScreenType Mode, RadioButton Button)> WindowModeButtons = [];
    private readonly List<(int Cap, RadioButton Button)> FrameCapButtons = [];
    private readonly List<(int Depth, RadioButton Button)> ColorDepthButtons = [];
    private readonly List<(string Key, RadioButton Button)> RendererButtons = [];
    private readonly Scale SensitivityScale;
    private readonly CheckButton VSyncCheck;
    private readonly CheckButton AskCheck;
    private readonly Label RendererDescription;

    private static readonly (FullScreenType Mode, string Label)[] WindowModes =
    [
        (FullScreenType.BorderlessDotByDot, "Borderless, dot by dot (recommended)"),
        (FullScreenType.Borderless, "Borderless fullscreen"),
        (FullScreenType.Exclusive, "Exclusive fullscreen"),
        (FullScreenType.Window, "Windowed"),
    ];

    private static readonly (int Cap, string Label)[] FrameCaps =
    [
        (-1, "Unlimited"),
        (60, "60"),
        (120, "120"),
        (144, "144"),
        (240, "240"),
    ];

    private static readonly Dictionary<string, string> RendererNotes = new()
    {
        ["raylib"] =
            "The original renderer.\n\n"
            + "The most tested path, and the only one that can show the\n"
            + "in-game debug and editor windows.",
        ["silk"] =
            "OpenGL 3.3, through Silk.NET.\n\n"
            + "Runs the game's shaders unchanged.\n"
            + "No debug or editor windows.",
        ["vulkan"] =
            "Vulkan, through Silk.NET.\n\n"
            + "Shaders are precompiled to SPIR-V ahead of time.\n"
            + "No debug or editor windows.",
    };

    private const double MinSensitivity = 0.25;
    private const double MaxSensitivity = 3.0;
    private const double DefaultSensitivity = 1.0;

    public LauncherWindow()
    {
        Application.Init();
        Application application = new("co.sugar.DmitryAndDemid.Config", GLib.ApplicationFlags.None);
        application.Register(GLib.Cancellable.Current);

        Window = new Window("AAG2 — Configuration") { Resizable = false, BorderWidth = 8 };
        Window.DeleteEvent += (_, _) => Application.Quit();

        VSyncCheck = new CheckButton("Vertical sync") { Active = Configuration.Config.UseVSYNC };
        AskCheck = new CheckButton("Show this window on every start") { Active = Configuration.Config.AlwaysAsk };
        RendererDescription = new Label { Xalign = 0, Yalign = 0, Justify = Justification.Left };
        SensitivityScale = new Scale(Orientation.Horizontal, MinSensitivity, MaxSensitivity, 0.05);

        Box root = new(Orientation.Vertical, 8);

        // Top: the loose checkboxes, as in the reference dialog.
        root.Add(VSyncCheck);
        root.Add(AskCheck);

        // Middle: two columns of framed groups.
        Box columns = new(Orientation.Horizontal, 12);
        Box left = new(Orientation.Vertical, 8);
        Box right = new(Orientation.Vertical, 8);

        left.Add(BuildResolutionFrame());
        left.Add(BuildWindowModeFrame());
        left.Add(BuildColorDepthFrame());
        left.Add(BuildFrameCapFrame());
        left.Add(BuildSensitivityFrame());

        right.Add(BuildRendererFrame());
        right.Add(BuildDescriptionFrame());

        columns.Add(left);
        columns.Add(right);
        root.Add(columns);

        // Bottom right: the buttons.
        Box buttons = new(Orientation.Horizontal, 8) { Halign = Align.End };

        Button close = new("Close") { WidthRequest = 96 };
        close.Clicked += (_, _) => Application.Quit();
        buttons.Add(close);

        Button save = new("Save") { WidthRequest = 96 };
        save.Clicked += (_, _) =>
        {
            Apply();
            Application.Quit();
        };
        buttons.Add(save);

        root.Add(buttons);
        Window.Add(root);

        UpdateRendererDescription();

    }

    private static Frame Group(string title, Widget child)
    {
        Frame frame = new(title);
        child.MarginStart = child.MarginEnd = 6;
        child.MarginTop = child.MarginBottom = 4;
        frame.Add(child);
        return frame;
    }

    /// <summary>Radio list, first entry becomes the group leader (GTK's radio groups work by member, not by container).</summary>
    private static RadioButton AddRadio(Box box, ref RadioButton? first, string label, bool active)
    {
        RadioButton button = first == null ? new RadioButton(label) : new RadioButton(first, label);
        first ??= button;
        button.Active = active;
        box.Add(button);
        return button;
    }

    private Frame BuildResolutionFrame()
    {
        Box box = new(Orientation.Vertical, 2);
        RadioButton? first = null;

        foreach (string resolution in BuildResolutions())
            ResolutionButtons.Add((resolution,
                AddRadio(box, ref first, resolution, resolution == Configuration.Config.Resolution)));

        // A config naming a resolution this monitor cannot show would otherwise leave nothing selected.
        if (ResolutionButtons.Count > 0 && ResolutionButtons.All(r => !r.Button.Active))
            ResolutionButtons[0].Button.Active = true;

        return Group("Resolution", box);
    }

    private Frame BuildWindowModeFrame()
    {
        Box box = new(Orientation.Vertical, 2);
        RadioButton? first = null;

        foreach ((FullScreenType mode, string label) in WindowModes)
            WindowModeButtons.Add((mode,
                AddRadio(box, ref first, label, mode == Configuration.Config.FullScreenType)));

        return Group("Display mode", box);
    }

    /// <summary>
    /// The colour-depth switch from the original dialog. It is deliberately non-functional: all three
    /// backends render RGBA8 to an 8-bit-per-channel swapchain, and nothing reads Configuration.ColorDepth.
    /// The label says so rather than pretending otherwise.
    /// </summary>
    private Frame BuildColorDepthFrame()
    {
        Box box = new(Orientation.Vertical, 4);

        Box radios = new(Orientation.Horizontal, 8);
        RadioButton? first = null;
        foreach ((int depth, string label) in new[] { (32, "32 Bits (recommended)"), (16, "16 Bits") })
            ColorDepthButtons.Add((depth,
                AddRadio(radios, ref first, label, depth == Configuration.Config.ColorDepth)));

        if (ColorDepthButtons.All(d => !d.Button.Active))
            ColorDepthButtons[0].Button.Active = true;

        box.Add(radios);

        return Group("Color depth", box);
    }

    private Frame BuildFrameCapFrame()
    {
        Box box = new(Orientation.Horizontal, 6);
        RadioButton? first = null;

        foreach ((int cap, string label) in FrameCaps)
            FrameCapButtons.Add((cap, AddRadio(box, ref first, label, cap == Configuration.Config.FrameCap)));

        if (FrameCapButtons.All(f => !f.Button.Active))
            FrameCapButtons[0].Button.Active = true;   // an unrecognised cap in config -> Unlimited

        return Group("Frame rate limit", box);
    }

    private Frame BuildSensitivityFrame()
    {
        Box box = new(Orientation.Vertical, 4);
        box.Add(new Label($"Scales the gamepad stick reading. Higher values reach\nthe movement threshold sooner.\n(default: {DefaultSensitivity:0.00})")
        {
            Xalign = 0,
            Justify = Justification.Left,
        });

        SensitivityScale.Value = Math.Clamp(Configuration.Config.GamepadSensitivity, MinSensitivity, MaxSensitivity);
        SensitivityScale.DrawValue = true;
        SensitivityScale.ValuePos = PositionType.Right;
        SensitivityScale.Digits = 2;
        SensitivityScale.WidthRequest = 240;
        box.Add(SensitivityScale);

        return Group("Gamepad sensitivity", box);
    }

    private Frame BuildRendererFrame()
    {
        Box box = new(Orientation.Vertical, 2);
        RadioButton? first = null;

        foreach ((string key, string name) in RendererRegistry.Available)
        {
            RadioButton button = AddRadio(box, ref first, name, key == Configuration.Config.Renderer);
            button.Toggled += (_, _) => UpdateRendererDescription();
            RendererButtons.Add((key, button));
        }

        if (RendererButtons.Count > 0 && RendererButtons.All(r => !r.Button.Active))
            RendererButtons[0].Button.Active = true;

        return Group("Renderer", box);
    }

    private Frame BuildDescriptionFrame()
    {
        RendererDescription.WidthRequest = 320;
        RendererDescription.HeightRequest = 130;
        return Group("About this renderer", RendererDescription);
    }

    private void UpdateRendererDescription() =>
        RendererDescription.Text = RendererNotes.GetValueOrDefault(SelectedRenderer(), "");

    private string SelectedRenderer() =>
        RendererButtons.FirstOrDefault(r => r.Button.Active).Key ?? Configuration.Config.Renderer;

    /// <summary>Every 4:3 resolution that fits on at least one monitor. The game's canvas is 640x480-based.</summary>
    private static List<string> BuildResolutions()
    {
        Gdk.Display? display = Gdk.Display.Default;
        SortedSet<double> multipliers = [];

        if (display != null)
            for (int i = 0; i < display.NMonitors; i++)
            {
                Gdk.Rectangle geometry = display.GetMonitor(i).Geometry;
                for (double multiplier = 1;
                     640 * multiplier <= geometry.Width && 480 * multiplier <= geometry.Height;
                     multiplier += .5)
                    multipliers.Add(multiplier);
            }

        if (multipliers.Count == 0)
            multipliers.Add(1);

        return multipliers.Select(m => $"{(int)(640 * m)}x{(int)(480 * m)}").ToList();
    }

    private void Apply()
    {
        foreach ((string value, RadioButton button) in ResolutionButtons)
            if (button.Active)
                Configuration.Config.Resolution = value;

        foreach ((FullScreenType mode, RadioButton button) in WindowModeButtons)
            if (button.Active)
                Configuration.Config.FullScreenType = mode;

        foreach ((int cap, RadioButton button) in FrameCapButtons)
            if (button.Active)
                Configuration.Config.FrameCap = cap;

        foreach ((string key, RadioButton button) in RendererButtons)
            if (button.Active)
                Configuration.Config.Renderer = key;

        foreach ((int depth, RadioButton button) in ColorDepthButtons)
            if (button.Active)
                Configuration.Config.ColorDepth = depth;   // persisted; nothing consumes it

        Configuration.Config.GamepadSensitivity = (float)SensitivityScale.Value;
        Configuration.Config.UseVSYNC = VSyncCheck.Active;
        Configuration.Config.AlwaysAsk = AskCheck.Active;
        Configuration.Config.Save();
    }

    public void Open()
    {
        Window.ShowAll();
        Application.Run();
    }
}

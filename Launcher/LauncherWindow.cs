using DmitryAndDemid.Data;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Rendering.Upscaling;
using Gtk;
using System.Text.Json;

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
/// cannot drift apart. Every label is the game's own wording too, read through <see cref="LauncherText"/>
/// from translation.json, so the configurator and the in-game settings never say a setting two ways.
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
    private readonly CheckButton LagCheck;
    private readonly CheckButton AskCheck;
    private readonly CheckButton TouchCheck;
    private readonly CheckButton VerticalCheck;
    private readonly Label RendererDescription;

    // Upscaling / frame generation / Reflex — the same settings the in-game menu has (Configuration.Upscaler
    // and friends), with the same availability rules, source-linked from the game's Upscalers catalogue.
    private readonly List<(UpscalerKind Kind, RadioButton Button)> UpscalerButtons = [];
    private readonly List<(int Quality, RadioButton Button)> QualityButtons = [];
    private readonly List<(int Factor, RadioButton Button)> FrameGenButtons = [];
    private readonly List<(int Mode, RadioButton Button)> ReflexButtons = [];
    private readonly Scale SharpnessScale;
    private readonly Label ReflexNote;

    // DLSS 5 Neural Rendering: live only while that upscaler is the picked one.
    private readonly List<(int Preset, RadioButton Button)> NeuralPresetButtons = [];
    private readonly Scale NeuralDenoiseScale;
    private readonly CheckButton NeuralRayReconstruction;
    private readonly CheckButton NeuralTextureCompression;
    private readonly CheckButton NeuralAutoExposure;
    private readonly CheckButton NeuralHdr;
    private readonly List<Widget> NeuralWidgets = [];

    private static readonly (int Factor, string Label)[] FrameGenFactors =
    [
        (1, LauncherText.T("settings.framegen.off")),
        (2, "x2"),
        (3, "x3"),
        (4, "x4"),
        (5, "x5"),
        (6, "x6"),
    ];

    private static readonly (int Mode, string Label)[] ReflexModes =
    [
        (0, LauncherText.T("settings.reflex.off")),
        (1, LauncherText.T("settings.reflex.on")),
        (2, LauncherText.T("settings.reflex.boost")),
    ];

    private static readonly (FullScreenType Mode, string Label)[] WindowModes =
    [
        (FullScreenType.BorderlessDotByDot, LauncherText.T("launcher.mode.dotbydot")),
        (FullScreenType.Borderless, LauncherText.T("launcher.mode.borderless")),
        (FullScreenType.Exclusive, LauncherText.T("launcher.mode.exclusive")),
        (FullScreenType.Window, LauncherText.T("launcher.mode.window")),
    ];

    private static readonly (int Cap, string Label)[] FrameCaps =
    [
        (-1, LauncherText.T("launcher.framecap.unlimited")),
        (60, "60"),
        (120, "120"),
        (144, "144"),
        (240, "240"),
    ];

    /// <summary>Renderer key -> the translation key of its blurb; the text itself lives in translation.json.</summary>
    private static readonly Dictionary<string, string> RendererNotes = new()
    {
        ["raylib"] = "launcher.renderer.raylib",
        ["silk"] = "launcher.renderer.silk",
        ["vulkan"] = "launcher.renderer.vulkan",
    };

    private const double MinSensitivity = 0.25;
    private const double MaxSensitivity = 3.0;
    private const double DefaultSensitivity = 1.0;

    public LauncherWindow()
    {
        Application.Init();
        Application application = new("co.sugar.DmitryAndDemid.Config", GLib.ApplicationFlags.None);
        application.Register(GLib.Cancellable.Current);

        Window = new Window($"AAG2 — {LauncherText.T("launcher.title")}") { Resizable = false, BorderWidth = 8 };
        Window.DeleteEvent += (_, _) => Application.Quit();

        // The configurator's own icon. Resolved next to the executable, which is where the game's assets are
        // copied (custom and aag2 share an output directory).
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Textures", "custom.png");
        if (File.Exists(iconPath))
            Window.SetIconFromFile(iconPath);

        VSyncCheck = new CheckButton(LauncherText.Title("settings.vsync")) { Active = Configuration.Config.UseVSYNC };
        LagCheck = new CheckButton(LauncherText.Title("settings.menulag")) { Active = Configuration.Config.IsMenuLagEnabled };
        AskCheck = new CheckButton(LauncherText.T("launcher.always_ask")) { Active = Configuration.Config.AlwaysAsk };
        TouchCheck = new CheckButton(LauncherText.T("launcher.touch"))
        {
            Active = Configuration.Config.TouchControls,
        };
        VerticalCheck = new CheckButton(LauncherText.Title("settings.vertical")) { Active = Configuration.Config.Vertical };
        RendererDescription = new Label { Xalign = 0, Yalign = 0, Justify = Justification.Left };
        SensitivityScale = new Scale(Orientation.Horizontal, MinSensitivity, MaxSensitivity, 0.05);
        SharpnessScale = new Scale(Orientation.Horizontal, 0, 100, 5);
        ReflexNote = new Label { Xalign = 0, Justify = Justification.Left, Wrap = true, MaxWidthChars = 38 };
        NeuralDenoiseScale = new Scale(Orientation.Horizontal, 0, 100, 5);
        NeuralRayReconstruction = new CheckButton(LauncherText.Title("settings.dlssnr.rr")) { Active = Configuration.Config.DLSSNRRayReconstruction };
        NeuralTextureCompression = new CheckButton(LauncherText.Title("settings.dlssnr.ntc")) { Active = Configuration.Config.DLSSNRTextureCompression };
        NeuralAutoExposure = new CheckButton(LauncherText.Title("settings.dlssnr.autoexposure")) { Active = Configuration.Config.DLSSNRAutoExposure };
        NeuralHdr = new CheckButton(LauncherText.Title("settings.dlssnr.hdr")) { Active = Configuration.Config.DLSSNRHdr };

        Box root = new(Orientation.Vertical, 8);

        // Top: the loose checkboxes, as in the reference dialog.
        root.Add(VSyncCheck);
        root.Add(LagCheck);
        root.Add(TouchCheck);
        root.Add(VerticalCheck);
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

        // Third column: the picture pipeline — upscaler, frame generation, Reflex.
        Box extra = new(Orientation.Vertical, 8);
        extra.Add(BuildUpscalingFrame());
        extra.Add(BuildFrameGenFrame());
        extra.Add(BuildReflexFrame());

        // Fourth column: DLSS 5 Neural Rendering's own settings — a column of their own, or the dialog grows
        // taller than a 1080p screen. Built after the upscaler frame, whose choice greys these.
        Box neural = new(Orientation.Vertical, 8);
        neural.Add(BuildNeuralFrame());

        columns.Add(left);
        columns.Add(right);
        columns.Add(extra);
        columns.Add(neural);
        root.Add(columns);

        // Bottom right: the buttons.
        Box buttons = new(Orientation.Horizontal, 8) { Halign = Align.End };

        Button close = new(LauncherText.T("launcher.close")) { WidthRequest = 96 };
        close.Clicked += (_, _) => Application.Quit();
        buttons.Add(close);

        Button save = new(LauncherText.T("launcher.save")) { WidthRequest = 96 };
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

        return Group(LauncherText.Title("settings.resolution"), box);
    }

    private Frame BuildWindowModeFrame()
    {
        Box box = new(Orientation.Vertical, 2);
        RadioButton? first = null;

        foreach ((FullScreenType mode, string label) in WindowModes)
            WindowModeButtons.Add((mode,
                AddRadio(box, ref first, label, mode == Configuration.Config.FullScreenType)));

        return Group(LauncherText.Title("settings.fullscreen"), box);
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
        foreach ((int depth, string label) in new[]
                 {
                     (32, LauncherText.T("launcher.colordepth.32")),
                     (16, LauncherText.T("launcher.colordepth.16")),
                 })
            ColorDepthButtons.Add((depth,
                AddRadio(radios, ref first, label, depth == Configuration.Config.ColorDepth)));

        if (ColorDepthButtons.All(d => !d.Button.Active))
            ColorDepthButtons[0].Button.Active = true;

        box.Add(radios);

        return Group(LauncherText.Title("launcher.colordepth"), box);
    }

    private Frame BuildFrameCapFrame()
    {
        Box box = new(Orientation.Horizontal, 6);
        RadioButton? first = null;

        foreach ((int cap, string label) in FrameCaps)
            FrameCapButtons.Add((cap, AddRadio(box, ref first, label, cap == Configuration.Config.FrameCap)));

        if (FrameCapButtons.All(f => !f.Button.Active))
            FrameCapButtons[0].Button.Active = true;   // an unrecognised cap in config -> Unlimited

        return Group(LauncherText.Title("settings.framerate"), box);
    }

    private Frame BuildSensitivityFrame()
    {
        Box box = new(Orientation.Vertical, 4);
        box.Add(new Label(LauncherText.T("launcher.sensitivity.note").Replace("%s", $"{DefaultSensitivity:0.00}"))
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

        return Group(LauncherText.Title("launcher.sensitivity"), box);
    }

    /// <summary>
    /// The upscaler list (every mode listed, the ones this machine cannot provide greyed with the reason —
    /// the DLSS 5 Neural Rendering files, the Streamline runtime, XeSS, Windows), the quality preset that
    /// sets the internal resolution, and the RCAS sharpness.
    /// </summary>
    private Frame BuildUpscalingFrame()
    {
        Box box = new(Orientation.Vertical, 2);
        var probe = Upscalers.Probe();
        UpscalerKind current = Upscalers.Parse(Configuration.Config.Upscaler);
        RadioButton? first = null;
        foreach (var entry in Upscalers.All)
        {
            string? why = Upscalers.Unavailable(entry.Kind, probe.IsWindows, probe.Streamline, probe.Neural, probe.Xess);
            string label = UpscalerLabel(entry.Kind) + (why == null ? "" : $"  — {UnavailableText(why)}");
            RadioButton button = AddRadio(box, ref first, label, entry.Kind == current && why == null);
            button.Sensitive = why == null;   // greyed out, and cannot be picked
            button.Toggled += (_, _) => UpdateNeuralAvailability();   // the DLSS NR frame follows this choice
            UpscalerButtons.Add((entry.Kind, button));
        }
        if (UpscalerButtons.All(u => !u.Button.Active))
            UpscalerButtons[0].Button.Active = true;   // a mode that is not available here -> Off

        box.Add(new Label(LauncherText.Title("settings.upscaler.quality") + ":") { Xalign = 0, MarginTop = 6 });
        box.Add(new Label(LauncherText.T("settings.upscaler.quality.note")) { Xalign = 0, Justify = Justification.Left });
        RadioButton? firstQuality = null;
        int quality = Upscalers.ClampQuality(Configuration.Config.UpscalerQuality);
        for (int i = 0; i < Upscalers.Qualities.Length; i++)
            QualityButtons.Add((i, AddRadio(box, ref firstQuality, QualityLabel(i), i == quality)));

        box.Add(new Label(LauncherText.Title("settings.sharpness") + ", %:") { Xalign = 0, MarginTop = 6 });
        SharpnessScale.Value = Math.Clamp(Configuration.Config.Sharpness, 0f, 1f) * 100;
        SharpnessScale.DrawValue = true;
        SharpnessScale.ValuePos = PositionType.Right;
        SharpnessScale.Digits = 0;
        SharpnessScale.WidthRequest = 240;
        box.Add(SharpnessScale);

        return Group(LauncherText.Title("settings.upscaler"), box);
    }

    // The names, presets and reasons are the game's own strings (translation.json), so the launcher and the
    // in-game settings say the same thing; the catalogue's Display fields and Unavailable() results ARE the keys.
    private static string UpscalerLabel(UpscalerKind kind) => LauncherText.T(Upscalers.DisplayOf(kind));

    private static string QualityLabel(int quality) =>
        LauncherText.T(Upscalers.Qualities[Upscalers.ClampQuality(quality)].Display);

    private static string UnavailableText(string key) => LauncherText.T(key);

    /// <summary>Frame generation: x2..x4 presented frames per capped frame. Every one is a real interpolated
    /// render of the 60 TPS simulation, so it only makes sense with a frame cap set.</summary>
    private Frame BuildFrameGenFrame()
    {
        Box box = new(Orientation.Vertical, 2);
        box.Add(new Label(LauncherText.T("settings.framegen.note"))
        {
            Xalign = 0,
            Justify = Justification.Left,
            Wrap = true,
            MaxWidthChars = 40,
        });
        RadioButton? first = null;
        int factor = Math.Clamp(Configuration.Config.FrameGeneration, 1, 6);
        foreach ((int f, string label) in FrameGenFactors)
            FrameGenButtons.Add((f, AddRadio(box, ref first, label, f == factor)));
        return Group(LauncherText.Title("settings.framegen"), box);
    }

    /// <summary>NVIDIA Reflex: the Vulkan backend's VK_NV_low_latency2. The launcher cannot ask the driver, so
    /// the rows follow the renderer choice and the game confirms the extension at start.</summary>
    private Frame BuildReflexFrame()
    {
        Box box = new(Orientation.Vertical, 2);
        RadioButton? first = null;
        int mode = Math.Clamp(Configuration.Config.Reflex, 0, 2);
        foreach ((int m, string label) in ReflexModes)
            ReflexButtons.Add((m, AddRadio(box, ref first, label, m == mode)));
        box.Add(ReflexNote);
        UpdateReflexAvailability();
        return Group(LauncherText.Title("settings.reflex"), box);
    }

    private void UpdateReflexAvailability()
    {
        bool vulkan = SelectedRenderer() == "vulkan";
        foreach ((_, RadioButton button) in ReflexButtons)
            button.Sensitive = vulkan;
        ReflexNote.Text = vulkan
            ? LauncherText.T("settings.reflex.note")
            : LauncherText.T("settings.reflex.vulkan_only");
    }

    /// <summary>DLSS 5 Neural Rendering's own settings: model preset, denoise strength, ray reconstruction,
    /// neural texture compression, auto exposure, HDR. Greyed unless that upscaler is the picked one.</summary>
    private Frame BuildNeuralFrame()
    {
        Box box = new(Orientation.Vertical, 2);
        Label note = new(LauncherText.T("settings.dlssnr.note")) { Xalign = 0, Justify = Justification.Left, Wrap = true, MaxWidthChars = 38 };
        box.Add(note);

        Label presetLabel = new(LauncherText.Title("settings.dlssnr.preset") + ":") { Xalign = 0, MarginTop = 4 };
        box.Add(presetLabel);
        NeuralWidgets.Add(presetLabel);
        RadioButton? first = null;
        int preset = Upscalers.ClampNeuralPreset(Configuration.Config.DLSSNRPreset);
        for (int i = 0; i < Upscalers.NeuralRenderingPresets.Length; i++)
        {
            string name = Upscalers.NeuralRenderingPresets[i];
            string label = name.StartsWith("settings.") ? LauncherText.T(name) : name;
            RadioButton button = AddRadio(box, ref first, label, i == preset);
            NeuralPresetButtons.Add((i, button));
            NeuralWidgets.Add(button);
        }

        Label denoiseLabel = new(LauncherText.Title("settings.dlssnr.denoise") + ", %:") { Xalign = 0, MarginTop = 4 };
        box.Add(denoiseLabel);
        NeuralWidgets.Add(denoiseLabel);
        NeuralDenoiseScale.Value = Math.Clamp(Configuration.Config.DLSSNRDenoise, 0f, 1f) * 100;
        NeuralDenoiseScale.DrawValue = true;
        NeuralDenoiseScale.ValuePos = PositionType.Right;
        NeuralDenoiseScale.Digits = 0;
        NeuralDenoiseScale.WidthRequest = 240;
        box.Add(NeuralDenoiseScale);
        NeuralWidgets.Add(NeuralDenoiseScale);

        foreach (CheckButton check in new[] { NeuralRayReconstruction, NeuralTextureCompression, NeuralAutoExposure, NeuralHdr })
        {
            box.Add(check);
            NeuralWidgets.Add(check);
        }
        UpdateNeuralAvailability();
        return Group(LauncherText.Title("settings.dlssnr"), box);
    }

    private UpscalerKind SelectedUpscaler() =>
        UpscalerButtons.FirstOrDefault(u => u.Button.Active).Kind;

    private void UpdateNeuralAvailability()
    {
        bool neural = SelectedUpscaler() == UpscalerKind.DlssNeural;
        foreach (Widget widget in NeuralWidgets)
            widget.Sensitive = neural;
    }

    private Frame BuildRendererFrame()
    {
        Box box = new(Orientation.Vertical, 2);
        RadioButton? first = null;

        foreach ((string key, string name) in RendererRegistry.Available)
        {
            RadioButton button = AddRadio(box, ref first, name, key == Configuration.Config.Renderer);
            button.Toggled += (_, _) =>
            {
                UpdateRendererDescription();
                UpdateReflexAvailability();   // Reflex rows follow the renderer: Vulkan only
            };
            RendererButtons.Add((key, button));
        }

        if (RendererButtons.Count > 0 && RendererButtons.All(r => !r.Button.Active))
            RendererButtons[0].Button.Active = true;

        return Group(LauncherText.Title("settings.renderer"), box);
    }

    private Frame BuildDescriptionFrame()
    {
        RendererDescription.WidthRequest = 320;
        RendererDescription.HeightRequest = 130;
        return Group(LauncherText.Title("launcher.renderer.about"), RendererDescription);
    }

    private void UpdateRendererDescription() =>
        RendererDescription.Text = RendererNotes.TryGetValue(SelectedRenderer(), out string? note)
            ? LauncherText.T(note)
            : "";

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
        Configuration.Config.TouchControls = TouchCheck.Active;
        Configuration.Config.Vertical = VerticalCheck.Active;
        Configuration.Config.AlwaysAsk = AskCheck.Active;

        foreach ((UpscalerKind kind, RadioButton button) in UpscalerButtons)
            if (button.Active)
                Configuration.Config.Upscaler = Upscalers.KeyOf(kind);
        foreach ((int quality, RadioButton button) in QualityButtons)
            if (button.Active)
                Configuration.Config.UpscalerQuality = quality;
        foreach ((int factor, RadioButton button) in FrameGenButtons)
            if (button.Active)
                Configuration.Config.FrameGeneration = factor;
        foreach ((int mode, RadioButton button) in ReflexButtons)
            if (button.Active)
                Configuration.Config.Reflex = mode;
        Configuration.Config.Sharpness = (float)(SharpnessScale.Value / 100.0);
        foreach ((int preset, RadioButton button) in NeuralPresetButtons)
            if (button.Active)
                Configuration.Config.DLSSNRPreset = preset;
        Configuration.Config.DLSSNRDenoise = (float)(NeuralDenoiseScale.Value / 100.0);
        Configuration.Config.DLSSNRRayReconstruction = NeuralRayReconstruction.Active;
        Configuration.Config.DLSSNRTextureCompression = NeuralTextureCompression.Active;
        Configuration.Config.DLSSNRAutoExposure = NeuralAutoExposure.Active;
        Configuration.Config.DLSSNRHdr = NeuralHdr.Active;
        Configuration.Config.Save();
    }

    public void Open()
    {
        Window.ShowAll();
        Application.Run();
    }
}

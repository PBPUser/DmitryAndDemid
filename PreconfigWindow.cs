#if !ANDROID
using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Data;
using Gtk;

namespace DmitryAndDemid;

public class PreconfigWindow
{
    public readonly Window Window;
    bool isOpen = false;
    public PreconfigWindow()
    {
        Application.Init();
        var app = new Application("co.sugar.DmitryAndDemid", GLib.ApplicationFlags.None);
        app.Register(GLib.Cancellable.Current);
        Window = new Window($"AAG2 — {Engine.Name}");
        Window.Resizable = false;
        Window.DeleteEvent += (a, b) => Application.Quit();
        var display = Gdk.Display.Default;
        if (display == null)
            Environment.Exit(0);
        // Every 4:3 resolution that fits on at least one monitor. `mp` used to carry over between monitors
        // and entries were never de-duplicated, so a second monitor produced either nothing or duplicates.
        SortedSet<double> multipliers = new();
        for (int i = 0; i < display.NMonitors; i++)
        {
            Gdk.Rectangle geometry = display.GetMonitor(i).Geometry;
            for (double mp = 1; 640 * mp <= geometry.Width && 480 * mp <= geometry.Height; mp += .5)
                multipliers.Add(mp);
        }
        if (multipliers.Count == 0)
            multipliers.Add(1);
        List<string> ress = multipliers.Reverse().Select(mp => $"{(int)(640 * mp)}x{(int)(480 * mp)}").ToList();
        var gridRes = new Grid();
        gridRes.RowSpacing = 4;
        gridRes.ColumnSpacing = 8;
        gridRes.Margin = 0;
        var radioButtonDotByDot = new RadioButton("Borderless Window DOT by DOT (Recomended)") { Halign = Align.Start };
        var radioButtonBorderless = new RadioButton(radioButtonDotByDot, "Borderless Window") { Halign = Align.Start };

        // Select(mode, resolution) on activation only. The old code used StateChanged, which also fires when
        // a button is DEselected — so picking one option ran the handler of the option you just left.
        void Bind(RadioButton button, FullScreenType mode, string resolution)
        {
            button.Toggled += (_, _) =>
            {
                if (!button.Active)
                    return;
                Configuration.Config.FullScreenType = mode;
                Configuration.Config.Resolution = resolution;
                Configuration.Config.Save();
            };
        }

        // The two borderless buttons previously had no handler at all: choosing either (including the
        // "Recommended" default) left FullScreenType at whatever was last saved.
        // Borderless modes cover the monitor, so the resolution here is the INTERNAL render resolution,
        // which the game then letterboxes; keep whatever is configured.
        Bind(radioButtonDotByDot, FullScreenType.BorderlessDotByDot, Configuration.Config.Resolution);
        Bind(radioButtonBorderless, FullScreenType.Borderless, Configuration.Config.Resolution);
        if (Configuration.Config.FullScreenType == FullScreenType.BorderlessDotByDot)
            radioButtonDotByDot.Active = true;
        else if (Configuration.Config.FullScreenType == FullScreenType.Borderless)
            radioButtonBorderless.Active = true;

        int rowS = 0;
        foreach (var x in ress)
        {
            var fullScreen = new RadioButton(radioButtonDotByDot, $"Full Screen {x}");
            var nonFullScreen = new RadioButton(radioButtonDotByDot, $"Window {x}");
            gridRes.Attach(fullScreen, 0, rowS, 1, 1);
            gridRes.Attach(nonFullScreen, 0, ress.Count + rowS, 1, 1);
            if (Configuration.Config.Resolution == x)
            {
                if (Configuration.Config.FullScreenType == FullScreenType.Exclusive)
                    fullScreen.Active = true;
                else if (Configuration.Config.FullScreenType == FullScreenType.Window)
                    nonFullScreen.Active = true;
            }
            Bind(fullScreen, FullScreenType.Exclusive, x);
            Bind(nonFullScreen, FullScreenType.Window, x); // was writing Borderless — "Window" left you borderless
            rowS++;
        }
        // Renderer picker. Built from Engine.Available, so it lists exactly the renderers that exist. Labelled
        // with the engine's name because this row picks what the Nikitos Engine runs ON, not which engine runs.
        var rendererBox = new Box(Orientation.Horizontal, 8) { Halign = Align.Start };
        rendererBox.Add(new Label($"{Engine.Name} renderer:"));

        var rendererCombo = new ComboBoxText();
        foreach ((string key, string name) in Engine.Available)
            rendererCombo.AppendText(name);

        int active = Array.FindIndex(Engine.Available, r => r.Key == Configuration.Config.Renderer);
        rendererCombo.Active = active < 0 ? 0 : active;

        rendererCombo.Changed += (_, _) =>
        {
            int index = rendererCombo.Active;
            if (index < 0 || index >= Engine.Available.Length)
                return;
            Configuration.Config.Renderer = Engine.Available[index].Key;
            Configuration.Config.Save();
        };
        rendererBox.Add(rendererCombo);


        var btn = new Button("Play");
        btn.Clicked += Play_Clicked;
        var grid = new Grid();
        grid.RowSpacing = 4;
        grid.ColumnSpacing = 8;
        grid.Margin = 4;
        var label = new Label("Choose resolution.");
        label.Halign = Align.Start;
        int row = 0;
        var checkBox = new CheckButton("Ask each startup time");
        checkBox.Active = Configuration.Config.AlwaysAsk; // was hard-coded true, ignoring the saved setting
        checkBox.Toggled += (_, _) =>
        {
            Configuration.Config.AlwaysAsk = checkBox.Active;
            Configuration.Config.Save();
        };
        grid.Attach(label, 0, row++, 1, 1);
        grid.Attach(radioButtonDotByDot, 0, row++, 1, 1);
        grid.Attach(radioButtonBorderless, 0, row++, 1, 1);
        grid.Attach(gridRes, 0, row++, 1, 1);
        grid.Attach(rendererBox, 0, row++, 1, 1);
        grid.Attach(checkBox, 0, row++, 1, 1);
        grid.Attach(btn, 0, row++, 1, 1);
        btn.Hexpand = false;
        btn.Halign = Align.Center;
        checkBox.Hexpand = false;
        checkBox.Halign = Align.Center;
        Window.Add(grid);
    }

    void Play_Clicked(object? sender, EventArgs e)
    {
        Start();
    }

    /// <summary>Set by Play: the game starts once the GTK loop has been left, not inside its click handler.</summary>
    private bool StartRequested;

    /// <summary>
    /// Play. This used to build the Runtime and run the game's main loop right here, inside the button's
    /// click callback — on GTK's own thread, before GTK had returned to its loop. The window's close was
    /// queued and never processed, so the configurator sat on screen, frozen, for the whole game. Now the
    /// click only closes the window and ends the GTK loop; <see cref="Open"/> starts the game after
    /// <c>Application.Run</c> has returned and the dialog is gone.
    /// </summary>
    void Start()
    {
        StartRequested = true;
        Window.Close();
        Application.Quit();
    }

    public void Open()
    {
        Window.ShowAll();
        Application.Run();
        // Let GTK paint the window away before the game's own window appears over the same spot.
        while (Application.EventsPending())
            Application.RunIteration(false);
        if (!StartRequested)
            return;   // closed with the title bar's X: the launcher exits, as it always did
        Runtime.CurrentRuntime = new Runtime();
        Runtime.CurrentRuntime.Start();
    }
}

#endif

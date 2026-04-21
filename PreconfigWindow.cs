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
        Window = new Window("AAG2");
        Window.Resizable = false;
        Window.DeleteEvent += (a, b) => Application.Quit();

        var display = Gdk.Display.Default;
        if (display == null)
            Environment.Exit(0);
        Gdk.Rectangle rc = new();
        double mp = 1;
        int w = 0;
        List<string> ress = new();
        for (int i = 0; i < display.NMonitors; i++)
        {
            rc = display.GetMonitor(i).Geometry;
            Console.WriteLine($"Geometry: {rc}");
            while (true)
            {
                if (rc.Width < 640 * mp)
                    break;
                if (rc.Height < 480 * mp)
                    break;
                ress.Add($"{(int)(640 * mp)}x{(int)(480 * mp)}");
                mp += .5;
            }
        }
        ress.Reverse();
        Console.WriteLine($"Resolutions: {String.Join("\n", ress)}");
        var gridRes = new Grid();
        gridRes.RowSpacing = 4;
        gridRes.ColumnSpacing = 8;
        gridRes.Margin = 0;
        var radioButtonDotByDot = new RadioButton("Borderless Window DOT by DOT (Recomended)") { Halign = Align.Start };
        var radioButtonBorderless = new RadioButton(radioButtonDotByDot, "Borderless Window") { Halign = Align.Start };
        int rowS = 0;

        foreach (var x in ress)
        {
            var fullScreen = new RadioButton(radioButtonDotByDot, $"Full Screen {x}");
            var nonFullScreen = new RadioButton(radioButtonDotByDot, $"Window {x}");
            gridRes.Attach(fullScreen, 0, rowS, 1, 1);
            gridRes.Attach(nonFullScreen, 0, ress.Count + rowS, 1, 1);
            if (Configuration.Config.Resolution == x)
            {
                if (Configuration.Config.FullScreenType == Data.FullScreenType.Exclusive)
                    fullScreen.Active = true;
                else if (Configuration.Config.FullScreenType == Data.FullScreenType.Window)
                    nonFullScreen.Active = true;
            }
            fullScreen.StateChanged += (a, b) =>
            {
                Configuration.Config.FullScreenType = FullScreenType.Exclusive;
                Configuration.Config.Resolution = x;
                Configuration.Config.Save();
            };
            nonFullScreen.StateChanged += (a, b) =>
            {
                Configuration.Config.FullScreenType = FullScreenType.Borderless;
                Configuration.Config.Resolution = x;
                Configuration.Config.Save();
            };
            rowS++;
        }
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
        checkBox.Active = true;
        checkBox.StateChanged += (a, b) =>
        {
            Configuration.Config.AlwaysAsk = checkBox.Active;
            Configuration.Config.Save();
        };
        grid.Attach(label, 0, row++, 1, 1);
        grid.Attach(radioButtonDotByDot, 0, row++, 1, 1);
        grid.Attach(radioButtonBorderless, 0, row++, 1, 1);
        grid.Attach(gridRes, 0, row++, 1, 1);
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

    void Start()
    {
        Window.Close();
        Runtime.CurrentRuntime = new Runtime();
        Runtime.CurrentRuntime.Start();
    }

    public void Open()
    {
        Window.ShowAll();
        Application.Run();
    }
}

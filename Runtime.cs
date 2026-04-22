using static Raylib_cs.Raylib;
using static DmitryAndDemid.Configuration;
using Gtk;
using Raylib_cs;
using DmitryAndDemid.Common;
using DmitryAndDemid.Screens;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid;

public class Runtime
{
    public static Runtime CurrentRuntime;

    public Runtime()
    {

    }

    public double Time;
    public int Width;
    public int Height;

    public bool DisableClose = false;
    bool ADPTriggered = false;
    public Rectangle FullScreenRect;


    public double Scale = 1;
    public Dictionary<string, Shader> Shaders = new();
    public Dictionary<string, Texture2D> Textures = new();

    public async Task Start()
    {
        var strs = Config.Resolution.Split("x");
        bool isErrored = false;
        string error = "";
        if (strs.Length != 2)
        {
            isErrored = true;
            error = "Invalid Resolution Configuration";
        }
        int width, height;
        if (!int.TryParse(strs[0], out width))
        {
            isErrored = true;
            error = "Invalid Resolution Configuration";
        }
        if (!int.TryParse(strs[1], out height))
        {
            isErrored = true;
            error = "Invalid Resolution Configuration";
        }
        if (isErrored)
        {
            var dialog = new MessageDialog(null, DialogFlags.Modal, MessageType.Info, ButtonsType.Ok, error);
            dialog.Run();
            dialog.Destroy();
            Environment.Exit(0);
        }
        Width = width;
        Height = height;
        FullScreenRect = new(0, 0, Width, Height);
        Scale = ((double)width) / 640d;
        InitWindow(width, height, "An AKOB Game 2: A story about Dmitry from Drochigin and Demid (Sergeevich)");
        SetTargetFPS(240);
        SetExitKey(KeyboardKey.Null);
        Time = GetTime();
        double c = 0;
        ScreenLoading = new LoadingScreen();
        AddScreen(ScreenLoading);
        await Load();
        while (!WindowShouldClose() || DisableClose)
        {
            c = GetTime();
            PreRender(Time - c);
            Render();
            Time = c;
        }
        Raylib.CloseWindow();
    }

    LoadingScreen ScreenLoading;
    MainScreen ScreenMain;

    bool ScreenRefreshRequired = false;
    List<Screen> Screens = new();
    List<Screen> QueueToAdd = new();
    List<Screen> QueueToRemove = new();

    List<System.Action> Actions = new();

    async Task Load()
    {
        LoadShaders();
        LoadTextures();
        Helper.LoadShaderAttribs();
        var thread = Thread.CurrentThread;
        Task.Delay(3000).ContinueWith(_ =>
        {
            if (!ADPTriggered)
                AddAction(() =>
                {
                    if (IsKeyDown(KeyboardKey.J))
                    {
                        ScreenLoading.SetADPText("User activated crash.", false);
                        ADPTriggered = true;
                    }
                    else
                        SwitchToMain();
                });
        });
    }

    void SwitchToMain()
    {
        ScreenMain = new MainScreen();
        RemoveScreen(ScreenLoading);
        AddScreen(ScreenMain);
    }

    void LoadTextures()
    {
        foreach (var x in Directory.GetFiles("Assets/Textures"))
            Textures[Path.GetFileName(x)] = LoadTexture(x);
    }

    void LoadShaders()
    {
        string[] fragmentShaders = Directory.GetFiles("Assets/Shaders", "*.fs");
        foreach (var x in fragmentShaders)
        {
            string vertexFile = x.Remove(x.Length - 3, 3) + ".vs";
            string shaderKey = x.Remove(x.Length - 3, 3).Replace("\\", "/").Split("/").Last();
            try
            {
                if (File.Exists(vertexFile))
                    Shaders.Add(shaderKey, LoadShader(vertexFile, x));
                else
                    Shaders.Add(shaderKey, LoadShader("Assets/Shaders/base.vs", x));
                if (Shaders[shaderKey].Id == 0)
                {
                    ScreenLoading.SetADPText("Failed to load shader: " + x, false);
                    ADPTriggered = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                ScreenLoading.SetADPText(ex.StackTrace, false);
                ADPTriggered = true;
                break;
            }
        }

    }

    public void AddAction(System.Action action)
    {
        ScreenRefreshRequired = true;
        Actions.Add(action);
    }

    public void AddScreen(Screen screen)
    {
        ScreenRefreshRequired = true;
        QueueToAdd.Add(screen);
    }

    public void RemoveScreen(Screen screen)
    {
        ScreenRefreshRequired = true;
        QueueToRemove.Add(screen);
    }

    void PreRender(double delta)
    {
        if (ScreenRefreshRequired)
            RefreshScreens();
        foreach (var screen in Screens)
        {
            screen.PreRender(delta);
        }
        Screens.Last().TopUpdate();
    }

    void RefreshScreens()
    {
        foreach (var x in Actions)
        {
            x.Invoke();
        }
        var lastScreen = Screens.LastOrDefault();
        Actions.Clear();
        Screens.AddRange(QueueToAdd);
        QueueToAdd.Clear();
        Screens.RemoveAll(x => QueueToRemove.Contains(x));
        QueueToRemove.Clear();
        ScreenRefreshRequired = false;
        if (lastScreen != Screens.LastOrDefault())
        {
            lastScreen?.Deactivated();
            Screens.LastOrDefault()?.Activated();
        }
    }

    void Render()
    {
        BeginDrawing();
        ClearBackground(Color.Black);
        foreach (var screen in Screens)
            screen.Render();
        DrawFPS(0, 0);
        EndDrawing();
    }
}

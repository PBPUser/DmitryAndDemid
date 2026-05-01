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

    public float SFXVolume = 1.0f;
    public float MusicVolume = 1.0f;

    public bool DisableClose = false;
    bool ADPTriggered = false;
    public Rectangle FullScreenRect;
    
    
    private Rectangle CurrentScoreSource;
    Rectangle CurrentScoreTarget;


    public double Scale = 1;
    public float ScaleF = 1;
    public Dictionary<string, Shader> Shaders = new();
    public Dictionary<string, Texture2D> Textures = new();
    public Dictionary<string, Sound> Sounds = new();
    public Dictionary<string, Font> Fonts = new();
    public int GamepadCount = 0;

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
        SFXVolume = Config.SFXVolume;
        MusicVolume = Config.MusicVolume;
        FullScreenRect = new(0, 0, Width, Height);
        Scale = ((double)width) / 640d;
        ScaleF = (float)Scale;
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

    Task Load()
    {
        try
        {
            InitAudioDevice();
        }
        catch (Exception ex)
        {
            ADPTriggered = true;
            ScreenLoading.SetADPText("HeBo3MoJHo uHutsuAJlu3upoBaTb 3ByKoByI0 noDcucTemu.", false);
        } 
        LoadShaders();
        LoadFonts();
        LoadTextures();
        Helper.LoadShaderAttribs();
        var thread = Thread.CurrentThread;
        LoadAudio();
        #if DEBUG
        Task.Delay(500).ContinueWith(_ =>
        #else
        Task.Delay(Config.FastLoading?3000:33000).ContinueWith(_ =>
        #endif
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
        return Task.CompletedTask;
    }

    void SwitchToMain()
    {
        ScreenMain = new MainScreen();
        RemoveScreen(ScreenLoading);
        AddScreen(ScreenMain);
    }

    void LoadAudio()
    {
        foreach (var file in Directory.GetFiles("Assets/Sounds"))
            Sounds[Path.GetFileNameWithoutExtension(file)] = LoadSound(file);
    }
    
    void LoadTextures()
    {
        foreach (var x in Directory.GetFiles("Assets/Textures", "*.png"))
            Textures[Path.GetFileName(x)] = LoadTexture(x);
        Textures["MenuItemSelectionGradient1"] = Helper.RenderSelectionBackground(200, 200, 0);
        Textures["MenuBackground"] = Helper.FillTextureWithColor(Color.Black with { A = 128 }, Width, Height).Texture;
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

    void LoadFonts()
    {
        foreach (var font in Directory.GetFiles("Assets/Fonts"))
        {
            Fonts[Path.GetFileNameWithoutExtension(font)] = LoadFont(font);
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
        screen.TargetCreate();
        QueueToAdd.Add(screen);
    }

    public void RemoveScreen(Screen screen)
    {
        ScreenRefreshRequired = true;
        QueueToRemove.Add(screen);
    }

    private int UpdateRenderFrom = 0;
    
    public void SetScreenRenderingFrom(int index)
    {
        UpdateRenderFrom = Math.Clamp(index, 0, Screens.Count - 1);
    }
    
    public int GetScreenIndex(Screen screen) => Screens.IndexOf(screen); 
    
    void PreRender(double delta)
    {
        if (ScreenRefreshRequired)
            RefreshScreens();
        GamepadCheck();
        for (int i = UpdateRenderFrom; i < Screens.Count; i++)
        {
            Screens[i].PreRender(delta);
        }
        Screens.Last().TopUpdate();
    }

    void GamepadCheck()
    {
        int prevGamepadCount = GamepadCount;
        GamepadCount = 0;
        while (IsGamepadAvailable(GamepadCount))
            GamepadCount++;
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
        if(MenuScreen.MenuItem.RequiresRender)
            MenuScreen.MenuItem.RenderItems();
        ClearBackground(Color.Black);
        for (int i = UpdateRenderFrom; i < Screens.Count; i++)
            Screens[i].Render();
        DrawFPS(0, 0);
        EndDrawing();
     }
}

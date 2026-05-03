using System.Numerics;
using System.Runtime.InteropServices;
using static Raylib_cs.Raylib;
using static DmitryAndDemid.Configuration;
using Gtk;
using Raylib_cs;
using DmitryAndDemid.Common;
using DmitryAndDemid.Screens;
using DmitryAndDemid.Utils;
using ImGuiNET;
using rlImGui_cs;

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
        rlImGui_cs.rlImGui.Setup(true);
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
                }
            }
            catch (Exception ex)
            {
                ScreenLoading.SetADPText(ex.StackTrace, false);
                ADPTriggered = true;
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
        #if DEBUG
        UpdateTextureView();
        #endif
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
        }    }

    void Render()
    {
        BeginDrawing();
        if(MenuScreen.MenuItem.RequiresRender)
            MenuScreen.MenuItem.RenderItems();
        ClearBackground(Color.Black);
        for (int i = UpdateRenderFrom; i < Screens.Count; i++)
            Screens[i].Render();
        DrawFPS(0, 0);
#if DEBUG
        if (TextureViewerOpen)
            DrawTextureView();
        else
        {
            rlImGui.Begin();
            Screens.Last().DrawImgui();
            rlImGui.End();
        }
#endif
        EndDrawing();
     }
    
    void UpdateTextureView()
    {
        if (GetTime() - TextureViewerDelay < TextureViewerLastTimeKeyPressed)
            return;
        if (IsKeyDown(KeyboardKey.LeftControl) && IsKeyDown(KeyboardKey.J))
        {
            TextureViewerOpen = !TextureViewerOpen;
            TextureViewerLastTimeKeyPressed = GetTime();
            return;
        }
        if (SetValueMode)
        {
            if (IsKeyDown(KeyboardKey.Tab))
            {
                if(IsKeyDown(KeyboardKey.LeftShift))
                    SetValueModeCursorField = (SetValueModeCursorField + 4) % 5;
                else
                    SetValueModeCursorField = (SetValueModeCursorField + 1) % 5;
                TextureViewerLastTimeKeyPressed = GetTime();
                return;
            }
            switch (SetValueModeCursorField)
            {
                case 0:
                    return;
                case 3:
                    if (IsKeyDown(KeyboardKey.A))
                        SetValueMode = false;
                    return;
            }

            return;
        }
        if (!TextureViewerOpen)
            return;
        if (IsKeyDown(KeyboardKey.Up))
        {
            TextureId--;
            TextureViewerLastTimeKeyPressed = GetTime();
            return;
        }

        var p = ShaderId;
        if (IsKeyDown(KeyboardKey.Left))
        {
            ShaderId = (ShaderId+(Shaders.Count+1)) % (Shaders.Count+1)-1;
            TextureViewerLastTimeKeyPressed = GetTime();
        }
        if (IsKeyDown(KeyboardKey.Right))
        {
            ShaderId = (ShaderId+2+(Shaders.Count+1)) % (Shaders.Count+1)-1;
            TextureViewerLastTimeKeyPressed = GetTime();
        }

        if (Math.Abs(p-ShaderId) > 0.000001f)
        {
            PreviewerShaderValues = new Dictionary<string, (object, ShaderUniformDataType)>();
            if (ShaderId == -1)
                return;
            var id = Shaders.ElementAt(ShaderId);
            var strs = File.ReadAllLines($"Assets/Shaders/{id.Key}.fs").Where(x => x.StartsWith("uniform"));
            foreach (var str in strs)
            {
                string[] spl =  str.Split(' ');
                string type = spl[1];
                string name = spl[2];
                PreviewerShaderValues[name] = (null, type switch
                {
                    "float" => ShaderUniformDataType.Float,
                    "vec2" => ShaderUniformDataType.Vec2,
                    "vec3" => ShaderUniformDataType.Vec3,
                    "vec4" => ShaderUniformDataType.Vec4,
                    "sampler2D" => ShaderUniformDataType.Sampler2D,
                    _ => ShaderUniformDataType.Float
                });
            }
            return;
        }
        if (IsKeyDown(KeyboardKey.R))
        {
            Zoom = 1;
            ImageOffset = Vector2.Zero;
            TextureViewerLastTimeKeyPressed = GetTime();
            return;
        }
        if (IsKeyDown(KeyboardKey.S))
        {
            SetValueMode = !SetValueMode;
            TextureViewerLastTimeKeyPressed = GetTime();
            return;
        }
        
        if (IsKeyDown(KeyboardKey.Down))
        {
            TextureId = (TextureId + 1) % Textures.Count;
            TextureViewerLastTimeKeyPressed = GetTime();
            return;
        }
        var delta = GetMouseWheelMove();
        if (MathF.Abs(delta) > 0)
        {
            Zoom = MathF.Max(Zoom+delta / 8, 0);
        }

        if (IsMouseButtonDown(MouseButton.Left))
        {
            ImageOffset += GetMouseDelta();
        }
    }

    void DrawTextureView()
    {
        rlImGui.Begin();
        var key = Textures.ElementAt(TextureId).Key;
        Texture2D texture = Textures.ElementAt(TextureId).Value;
        ImGui.Begin("Texture Viewer: ");
        DrawRectangle(0,0,Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height, Color.Black);
        ImGui.TextUnformatted($"Texture ID: {TextureId} ({key})");
        if (ShaderId != -1)
        {
            var shader = Shaders.ElementAt(ShaderId);
            ImGui.TextUnformatted($"Shader ID: {ShaderId} ({shader.Key})");
            foreach (var variable in PreviewerShaderValues.Where(x => x.Value.Item1 != null))
            {
                switch (variable.Value.Item2)
                {
                    case ShaderUniformDataType.Float:
                        SetShaderValue(shader.Value, 
                            GetShaderLocation(shader.Value, variable.Key), 
                            (float)(variable.Value.Item1 as float?),
                            variable.Value.Item2);
                        break;
                    case ShaderUniformDataType.Vec2:
                        SetShaderValue(shader.Value, 
                            GetShaderLocation(shader.Value, variable.Key), 
                            (Vector2)(variable.Value.Item1 as Vector2?),
                            variable.Value.Item2);
                        break;
                    case ShaderUniformDataType.Vec3:
                        SetShaderValue(shader.Value, 
                            GetShaderLocation(shader.Value, variable.Key), 
                            (Vector3)variable.Value.Item1,
                            variable.Value.Item2);
                        break;
                }
            }
            BeginShaderMode(shader.Value);
        }

        if (ImGui.Button("Set Shader Values"))
        {
            SetValueMode = true;
        }
        if (ImGui.Button("Reset"))
        {
            Zoom = 1;
            ImageOffset = Vector2.Zero;
        }
        ImGui.End();
        DrawTextureEx(texture, ImageOffset, 0, Zoom, Color.White);
        EndShaderMode();
        if (SetValueMode)
        {
            ImGui.Begin("Set Value: ");
            foreach (var shaderValue in PreviewerShaderValues)
            {
                string str = ""+shaderValue.Value.Item1;
                switch (shaderValue.Value.Item2)
                {
                    case ShaderUniformDataType.Float:
                        if (ImGui.InputText(shaderValue.Key, ref str, 0xff))
                        {
                            if (float.TryParse(str, out float value))
                                PreviewerShaderValues[shaderValue.Key] =
                                    (value, PreviewerShaderValues[shaderValue.Key].Item2);
                        }
                        break;
                    case ShaderUniformDataType.Vec2:
                        if (ImGui.InputText(shaderValue.Key, ref str, 0xff))
                        {
                            string[] spl = str.Split(',');
                            if (spl.Length != 2)
                                break;
                            if (float.TryParse(spl[0], out float x))
                                if (float.TryParse(spl[1], out float y))
                                    PreviewerShaderValues[shaderValue.Key] =
                                        (new Vector2(x,y), PreviewerShaderValues[shaderValue.Key].Item2);
                        }
                        break;
                    case ShaderUniformDataType.Vec3:
                        if (ImGui.InputText(shaderValue.Key, ref str, 0xff))
                        {
                            string[] spl = str.Split(',');
                            if (spl.Length != 3)
                                break;
                            if (float.TryParse(spl[0], out float x))
                                if (float.TryParse(spl[1], out float y))
                                    if (float.TryParse(spl[2], out float z))
                                        PreviewerShaderValues[shaderValue.Key] =
                                            (new Vector3(x,y,z), PreviewerShaderValues[shaderValue.Key].Item2);
                        }
                        break;
                }
            }
            if (ImGui.Button("Close"))
                SetValueMode = false;
            ImGui.End();
        }
        rlImGui.End();
    }

    private ShaderUniformDataType ShaderUniformDataType = ShaderUniformDataType.Float;
    private string FieldText = "";
    private string ValueText = "";
    private bool SetValueMode = false;
    private int SetValueModeCursorField = 0;
    Vector2 ImageOffset = Vector2.Zero;
    private float Zoom = 1;
    private int TextureId = 0;
    private int ShaderId = -1;
    private double TextureViewerLastTimeKeyPressed = 0;
    private const double TextureViewerDelay = 0.125;
    private bool TextureViewerOpen = false;
    private Dictionary<string, (object?, ShaderUniformDataType)> PreviewerShaderValues = new();
}

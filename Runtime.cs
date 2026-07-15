using System.Net.Mime;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using static DmitryAndDemid.Rendering.Gfx;
using static DmitryAndDemid.Configuration;
using DmitryAndDemid.Backgrounds;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Screens;
using DmitryAndDemid.Utils;
#if DEBUG
using ImGuiNET;
#endif

namespace DmitryAndDemid;

public class Runtime
{
    public static Runtime CurrentRuntime;

    public Runtime()
    {

    }

    // Loaded on first use, NOT in a static field initializer. Reading it at type-init time meant that merely
    // touching any Runtime static — e.g. Runtime.OpenSettingsOnStart in the Activity's OnCreate — tried to read
    // base.vs before the Android asset source was installed (that happens later, on the GL thread), and the
    // whole type initializer threw. It is only used by the desktop editor, so lazy costs nothing.
    private static string? _baseVertexShader;
    public static string BaseVertexShader => _baseVertexShader ??= Assets.ReadAllText("Assets/Shaders/base.vs");

    public static Rgba TransparentWhite = Rgba.White with { A = 0 };
    public static Rgba TransparentBlack = Rgba.Black with { A = 0 };
    public string VersionString = "0.02a";
    public double Time;
    public int Width;
    public int Height;
    public float SFXVolume = 1.0f;
    public float MusicVolume = 1.0f;
    public bool DisableClose = false;
    bool ADPTriggered = false;
    public Rect FullScreenRect;
    private Rect CurrentScoreSource;
    Rect CurrentScoreTarget;
    public double Scale = 1;
    public float ScaleF = 1;
    public Dictionary<string, ShaderHandle> Shaders = new();
    public Dictionary<string, TextureHandle> Textures = new();
    public Dictionary<string, SoundHandle> Sounds = new();
    public Dictionary<string, FontHandle> Fonts = new();
    public Dictionary<string, BulletRenderingInfo> BulletVisualPresets = new();
    public int GamepadCount = 0;

    /// <summary>
    /// The game always renders into this at its internal 4:3 resolution (Width x Height); the frame is then
    /// blitted into the real window by <see cref="Present"/>. Without this indirection the fixed
    /// Scale = Width/640 layout would run off the bottom of any non-4:3 monitor in fullscreen.
    /// </summary>
    TargetHandle Backbuffer;

    /// <summary>Where the backbuffer lands inside the window: the whole window, or a letterboxed sub-rect.</summary>
    public Rect PresentRect { get; private set; }

    public FullScreenType WindowMode = FullScreenType.Window;

    public async Task Start()
    {
        // Pick the renderer: --renderer=<name> on the command line wins, else config.json, else Raylib.
        string rendererName = Config.Renderer;
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg.StartsWith("--renderer=", StringComparison.OrdinalIgnoreCase))
                rendererName = arg["--renderer=".Length..];
        Engine.Use(Engine.Create(rendererName));
        Console.WriteLine($"Renderer: {Engine.BackendName}");

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
            Platform.FatalError(error);
            Environment.Exit(0);
        }
        // Vertical mode: a portrait window/backbuffer. Everything is laid out from Width/Height/Scale, so the
        // swap flips the whole game to portrait.
        if (Config.Vertical && width > height)
            (width, height) = (height, width);
        Width = width;
        Height = height;
        SFXVolume = Config.SFXVolume;
        MusicVolume = Config.MusicVolume;
        FullScreenRect = new(0, 0, Width, Height);
        Scale = Width / 640d;
        ScaleF = (float)Scale;
        var size = 100 * ScaleF;
        Engine.Platform.OpenWindow(width, height,
            $"AAG2 ~ Subhumanian Fartalism [{Engine.BackendName}]");
        Engine.Platform.SetWindowIcon("Assets/Textures/icon.png");
        SetWindowMode(Config.FullScreenType);
        Backbuffer = LoadRenderTexture(Width, Height);
        var sugarTexture = LoadTexture(Assets.Resolve("Assets/Textures/sugar_logo.png"));
        if (Engine.Backend.SupportsDebugUi)
            Engine.Backend.SetupDebugUi();
        BeginDrawing();
        ClearBackground(Rgba.Black);
        BeginTextureMode(Backbuffer);
        ClearBackground(Rgba.Black);
        DrawTexturePro(sugarTexture,
            new Rect(Vector2.Zero, 400, 400),
            new Rect((Width - size) / 2, (Height - size) / 2, size, size),
            Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        Present();
        EndDrawing();
        UnloadTexture(sugarTexture);
        SetTargetFPS(Config.FrameCap);
        if (Config.UseVSYNC)
            Engine.Platform.SetVSync(true);
        Engine.Platform.DisableExitKey();
        Time = GetTime();
        double c = 0;
        ScreenLoading = new LoadingScreen();
        AddScreen(ScreenLoading);
        await Load();
        while (!WindowShouldClose() || DisableClose)
            RunFrame();
        Engine.Platform.CloseWindow();
    }

    /// <summary>
    /// One frame. Desktop spins this in its own loop; Android cannot — there the Activity's GLSurfaceView
    /// owns the loop and calls this from onDrawFrame, so the body has to be reachable on its own.
    /// </summary>
    public void RunFrame()
    {
        double now = GetTime();
        PreRender(Time - now);
        Render();
        Time = now;
    }

#if ANDROID
    /// <summary>
    /// Android startup: the GL context and the surface already exist (the Activity made them), so this is
    /// <see cref="Start"/> minus everything to do with owning a window.
    /// </summary>
    public async Task StartAndroid(Silk.NET.OpenGL.GL gl, int surfaceWidth, int surfaceHeight,
        IAudio? audio = null)
    {
        SilkGLBackend backend = new();
        Engine.Use(backend);
        if (audio != null)
            backend.Audio = audio;   // must be set before Load(), which loads every sound
        backend.AttachExternalContext(gl, surfaceWidth, surfaceHeight);

        // The game renders at a fixed 4:3 internal resolution that Present() letterboxes onto the surface, so
        // the phone's own aspect needs no handling here. The resolution is the configured one (higher = a
        // sharper letterboxed image); 640x480 if the config value is unusable.
        if (Helper.GetResolutionFromString(Config.Resolution, out (int width, int height) res) && res.width > 0 && res.height > 0)
        {
            Width = res.width;
            Height = res.height;
        }
        else
        {
            Width = 640;
            Height = 480;
        }
        // Vertical mode renders into a portrait backbuffer: keep the configured pixel budget but make it taller
        // than wide. Every screen reads Width/Height/Scale, so this flips the whole game to portrait.
        if (Config.Vertical && Width > Height)
            (Width, Height) = (Height, Width);
        SFXVolume = Config.SFXVolume;
        MusicVolume = Config.MusicVolume;
        FullScreenRect = new(0, 0, Width, Height);
        Scale = Width / 640d;
        ScaleF = (float)Scale;

        WindowMode = FullScreenType.Borderless;   // the Activity surface is always fullscreen
        Backbuffer = LoadRenderTexture(Width, Height);
        Time = GetTime();
        ScreenLoading = new LoadingScreen();
        AddScreen(ScreenLoading);
        await Load();
    }
#endif

    /// <summary>
    /// Switches presentation mode and persists it. Live-switchable: the internal resolution never changes,
    /// only how the backbuffer is mapped onto the window, so nothing needs reloading.
    /// </summary>
    public void SetWindowMode(FullScreenType mode)
    {
        WindowMode = mode;
        Engine.Platform.ApplyWindowMode(ToEngineMode(mode), Width, Height);
        if (Config.FullScreenType != mode)
        {
            Config.FullScreenType = mode;
            Config.Save();
        }
    }

    static Rendering.WindowMode ToEngineMode(FullScreenType type) => type switch
    {
        FullScreenType.Borderless => Rendering.WindowMode.Borderless,
        FullScreenType.BorderlessDotByDot => Rendering.WindowMode.BorderlessDotByDot,
        FullScreenType.Exclusive => Rendering.WindowMode.Exclusive,
        _ => Rendering.WindowMode.Windowed,
    };

    /// <summary>
    /// Blits the backbuffer into the window. Recomputed every frame so that a mode switch, a monitor change
    /// or a window resize is picked up without any extra bookkeeping.
    /// </summary>
    void Present()
    {
        int windowWidth = GetScreenWidth(), windowHeight = GetScreenHeight();
        float x, y, w, h;

        // Dot-by-dot means 1:1 pixels — no scaling at all, just centre it. Falls back to fitting if the
        // chosen internal resolution is actually larger than the monitor.
        bool dotByDot = WindowMode == FullScreenType.BorderlessDotByDot
                        && Width <= windowWidth && Height <= windowHeight;
        if (dotByDot)
        {
            w = Width;
            h = Height;
        }
        else
        {
            // Fit while preserving the 4:3 aspect: pillarbox on a wider monitor, letterbox on a taller one.
            float scale = MathF.Min(windowWidth / (float)Width, windowHeight / (float)Height);
            w = Width * scale;
            h = Height * scale;
        }
        x = (windowWidth - w) / 2f;
        y = (windowHeight - h) / 2f;
        PresentRect = new Rect(x, y, w, h);

        // Point filtering when the mapping is 1:1 or an exact integer multiple, bilinear otherwise —
        // otherwise a fractional scale makes the pixel art shimmer.
        bool integerScale = MathF.Abs(w % Width) < 0.01f && MathF.Abs(h % Height) < 0.01f;
        SetTextureFilter(Backbuffer.Texture,
            dotByDot || integerScale ? FilterMode.Point : FilterMode.Bilinear);

        // Blit as a straight RGB copy (src factor ONE, dst factor ZERO), deliberately ignoring the
        // backbuffer's alpha channel.
        //
        // Raylib's default blend applies SRC_ALPHA/ONE_MINUS_SRC_ALPHA to the ALPHA channel as well as to
        // RGB, so drawing a half-transparent pixel into an opaque target leaves alpha at 0.75, not 1
        // (0.5*0.5 + 1*(1-0.5)). That never mattered while the game composited straight to the window,
        // because a window's alpha channel is never sampled. The backbuffer's alpha IS sampled here, so
        // without this the semi-transparent passes — spellcard effects above all — would punch holes in
        // the frame and the gameplay would blend into the black behind it.
        // The RGB is already correctly composited; only the alpha is junk, so we discard it.
        BeginBlendMode(BlendMode.CopyRgb);
        DrawTexturePro(
            Backbuffer.Texture,
            new Rect(0, Height, Width, -Height), // render targets are stored bottom-up
            PresentRect,
            Vector2.Zero, 0, Rgba.White);
        EndBlendMode();
    }


    /// <summary>Maps a window-space point (e.g. the mouse) into the game's internal coordinate space.</summary>
    public Vector2 WindowToGame(Vector2 windowPoint)
    {
        if (PresentRect.Width <= 0 || PresentRect.Height <= 0)
            return windowPoint;
        return new Vector2(
            (windowPoint.X - PresentRect.X) * Width / PresentRect.Width,
            (windowPoint.Y - PresentRect.Y) * Height / PresentRect.Height);
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
            if (!Engine.Audio.Initialize())
                throw new Exception("audio device unavailable");
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
        LoadBullets();
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
                    if (IsKeyDown(KeyCode.J))
                    {
                        ScreenLoading.SetADPText("Kpajjj AKTUBUPOBAHblJ noJl3OBaTeJlEM..", false);
                        ADPTriggered = true;
                    }
                    else
                        SwitchToMain();
                });
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Set by the Android "Configure" app shortcut: the game opens straight into its settings screen. The
    /// desktop configurator is a separate GTK app, so this path is Android's stand-in for it.
    /// </summary>
    public static bool OpenSettingsOnStart;

    void SwitchToMain()
    {
        ScreenMain = new MainScreen();
        RemoveScreen(ScreenLoading);
        AddScreen(ScreenMain);
        if (OpenSettingsOnStart)
        {
            OpenSettingsOnStart = false;
            AddScreen(new SettingsScreen());
        }
    }

    void LoadAudio()
    {
        foreach (var file in Assets.Files("Assets/Sounds"))
            Sounds[Path.GetFileNameWithoutExtension(file)] = LoadSound(file);
    }
    
    void LoadTextures()
    {
        foreach (var x in Assets.Files("Assets/Textures", "*.png"))
            Textures[Path.GetFileName(x)] = LoadTexture(x);
        Textures["MenuItemSelectionGradient1"] = Helper.RenderSelectionBackground(200, 200, 0);
        Textures["MenuBackground"] = Helper.FillTextureWithColor(Rgba.Black with { A = 128 }, Width, Height).Texture;
        Textures["Copyright"] = Helper.DrawTextScaled(")(U,2026 Konu9lnpaBa Caxap Ko.", 12, 2, 2, 1, Fonts["kodemono"], "gradient").Texture;
        Textures["Version"] = Helper.DrawTextScaled($"Beer {VersionString} (npo6Ha9l Bepcu9I)", 12, 2, 2, 1, Fonts["kodemono"], "gradient").Texture;
        Textures["384x448"] = Helper.FillTextureWithColor(Rgba.White, 384, 448).Texture;
        PrepareScoreTexture();
        Textures = Textures.OrderBy(x => x.Key).ToDictionary();
    }

    public float ScoreSpacing = 0;
    public float ScoreLetterWidth = 0;
    public float ScoreLetterHeight = 0;
    
    void PrepareScoreTexture()
    {
        const string text = "0123456789./";
        float spacing = ScaleF * 4, fontSize = ScaleF * 64; 
        Vector2 measure = MeasureTextEx(Fonts["kodemono"], text, fontSize, spacing);
        float letterWidth = measure.X / text.Length;
        TargetHandle
            temp1 = LoadRenderTexture((int)measure.X, (int)measure.Y),
            final = LoadRenderTexture((int)(measure.X + spacing * 2), (int)(measure.Y + spacing * 2));
        BeginTextureMode(temp1);
        DrawTextEx(Fonts["kodemono"], text, Vector2.Zero, fontSize, spacing, Rgba.White);
        EndTextureMode();
        SetShaderValue(Shaders["outline2"], GetShaderLocation(Shaders["outline2"], "border_width"), ScaleF * 6, UniformType.Float);
        SetShaderValue(Shaders["outline2"], GetShaderLocation(Shaders["outline2"], "fres"), measure + new Vector2(spacing * 2), UniformType.Vec2);
        SetShaderValue(Shaders["outline2"], GetShaderLocation(Shaders["outline2"], "res"), measure, UniformType.Vec2);
        SetShaderValue(Shaders["outline2"], GetShaderLocation(Shaders["outline2"], "pos"), [0, 0], UniformType.Vec2);
        BeginTextureMode(final);
        BeginShaderMode(Shaders["outline2"]);
        DrawTexture(temp1.Texture, (int)spacing , (int)spacing, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        UnloadRenderTexture(temp1);
        Textures["ScoreDigitsPrerender"] = final.Texture;
        ScoreSpacing = spacing;
        ScoreLetterHeight = measure.Y;
        ScoreLetterWidth = letterWidth;
    }

    void LoadBullets()
    {
        var jso = new JsonSerializerOptions()
        {
            IncludeFields = true
        };
        foreach (var x in Assets.Files("Assets/Data/BulletVisuals"))
            BulletVisualPresets[Path.GetFileNameWithoutExtension(x)] =
                JsonSerializer.Deserialize<BulletRenderingInfo>(File.ReadAllText(x), jso);
    }

    void LoadShaders()
    {
        string[] fragmentShaders = Assets.Files("Assets/Shaders", "*.fs").OrderBy(x => x).ToArray();
        foreach (var x in fragmentShaders)
        {
            string vertexFile = x.Remove(x.Length - 3, 3) + ".vs";
            string shaderKey = x.Remove(x.Length - 3, 3).Replace("\\", "/").Split("/").Last();
            try
            {
                // Anything the driver says about *this* shader, and not the one before it.
                ShaderDiagnostics.Clear();
                if (File.Exists(vertexFile))
                    Shaders.Add(shaderKey, LoadShader(vertexFile, x));
                else
                    Shaders.Add(shaderKey, LoadShader(Assets.Resolve("Assets/Shaders/base.vs"), x));
                if (Shaders[shaderKey].Id == 0)
                {
                    string reason = ShaderDiagnostics.HasError
                        ? ShaderDiagnostics.LastError
                        : "no compiler log — the driver gave no reason";
                    string message = $"Failed to load shader: {x}\n{reason}";
                    Console.Error.WriteLine(message);
                    ScreenLoading.SetADPText(message, false);
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
        int fSize = (int)(64 * ScaleF);
        foreach (var font in Assets.Files("Assets/Fonts"))
        {
            Fonts[Path.GetFileNameWithoutExtension(font)] = LoadFontEx(font, fSize, [], 0);
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
        UpdateBackgroundTester();
        #endif
    }

    void GamepadCheck()
    {
        Engine.Input.RefreshGamepads();
        GamepadCount = Engine.Input.GamepadCount;
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
        // Reset the render-from floor whenever the stack changes. An opaque screen (gameplay) re-raises it in
        // its own PreRender every frame it is present; without this reset it stayed raised after that screen was
        // removed, so Render() kept skipping the menu underneath — a blank menu after leaving gameplay once.
        UpdateRenderFrom = 0;
        ScreenRefreshRequired = false;
        if (lastScreen != Screens.LastOrDefault())
        {
            lastScreen?.Deactivated();
            Screens.LastOrDefault()?.Activated();
        }    }

    public bool IsFrameCap240 = Config.FrameCap == 240;

    void Render()
    {
        BeginDrawing();
        // MenuItem pre-renders into its own targets; keep it outside the backbuffer so it can't be
        // mistaken for frame content.
        if(MenuScreen.MenuItem.RequiresRender)
            MenuScreen.MenuItem.RenderItems();
        ClearBackground(Rgba.Black); // the letterbox bars

        BeginTextureMode(Backbuffer);
        Engine.Renderer.TargetFloor = 1;
        ClearBackground(Rgba.Black);
        for (int i = UpdateRenderFrom; i < Screens.Count; i++)
            Screens[i].Render();
        Engine.Renderer.TargetFloor = 0;
        // Reset rather than End: it unwinds the whole stack, so a screen that leaked a Begin can't
        // corrupt the next frame. In the balanced case it is exactly an End.
        Engine.Renderer.ResetTargets();

        Present();
        // FPS counter: in windowed mode the window corner is the game corner, but when fullscreen/letterboxed
        // (0,0) lands in a black bar, so put it at the top-left of the actual game area instead.
        if (WindowMode == FullScreenType.Window)
            DrawFPS(0, 0);
        else
            DrawFPS((int)PresentRect.X + 4, (int)PresentRect.Y + 4);
#if DEBUG
        if (BackgroundTesterOpen)
            DrawBackgroundTester();
        else if (TextureViewerOpen)
            DrawTextureView();
        else
        {
            if (Engine.Backend.SupportsDebugUi)
            {
                Engine.Backend.BeginDebugUi();
                Screens.Last().DrawImgui();
                Engine.Backend.EndDebugUi();
            }
        }
#endif
        if (IsFrameCap240)
            DrawTexture(Textures["241fps.png"], 0,0,Rgba.White);
        EndDrawing();
     }

#if DEBUG
    private bool UseWhiteBackground = false;
    
    void UpdateTextureView()
    {
        if (GetTime() - TextureViewerDelay < TextureViewerLastTimeKeyPressed)
            return;
        if (IsKeyDown(KeyCode.LeftControl))
        {
            if (IsKeyDown(KeyCode.J))
            {
                TextureViewerOpen = !TextureViewerOpen;
                TextureViewerLastTimeKeyPressed = GetTime();
            }
            else if (IsKeyDown(KeyCode.A))
            {
                UseWhiteBackground = !UseWhiteBackground;
                TextureViewerLastTimeKeyPressed = GetTime();
            }
            else if (IsKeyDown(KeyCode.C))
            {
                TextureViewerLastTimeKeyPressed = GetTime();
            }
            return;
        }
        if (SetValueMode)
        {
            if (IsKeyDown(KeyCode.Tab))
            {
                if(IsKeyDown(KeyCode.LeftShift))
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
                    if (IsKeyDown(KeyCode.A))
                        SetValueMode = false;
                    return;
            }

            return;
        }
        if (!TextureViewerOpen)
            return;
        if (IsKeyDown(KeyCode.Up))
        {
            TextureId = (TextureId + Textures.Count - 1) % Textures.Count;
            TextureViewerLastTimeKeyPressed = GetTime();
            return;
        }

        var p = ShaderId;
        if (IsKeyDown(KeyCode.Left))
        {
            ShaderId = (ShaderId+(Shaders.Count+1)) % (Shaders.Count+1)-1;
            TextureViewerLastTimeKeyPressed = GetTime();
        }
        if (IsKeyDown(KeyCode.Right))
        {
            ShaderId = (ShaderId+2+(Shaders.Count+1)) % (Shaders.Count+1)-1;
            TextureViewerLastTimeKeyPressed = GetTime();
        }

        if (Math.Abs(p-ShaderId) > 0.000001f)
        {
            PreviewerShaderValues = new Dictionary<string, (object, UniformType)>();
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
                    "float" => UniformType.Float,
                    "vec2" => UniformType.Vec2,
                    "vec3" => UniformType.Vec3,
                    "vec4" => UniformType.Vec4,
                    "sampler2D" => UniformType.Sampler2D,
                    _ => UniformType.Float
                });
            }
            return;
        }
        if (IsKeyDown(KeyCode.R))
        {
            Zoom = 1;
            ImageOffset = Vector2.Zero;
            TextureViewerLastTimeKeyPressed = GetTime();
            return;
        }
        if (IsKeyDown(KeyCode.S))
        {
            SetValueMode = !SetValueMode;
            TextureViewerLastTimeKeyPressed = GetTime();
            return;
        }
        
        if (IsKeyDown(KeyCode.Down))
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

        if (IsMouseButtonDown(MouseBtn.Left))
        {
            ImageOffset += GetMouseDelta();
        }
    }
    
    void DrawTextureView()
    {
        if (!Engine.Backend.SupportsDebugUi)
            return;
        Engine.Backend.BeginDebugUi();
        var key = Textures.ElementAt(TextureId).Key;
        TextureHandle texture = Textures.ElementAt(TextureId).Value;
        ImGui.Begin("Texture Viewer: ");
        ImGui.Text("press Ctrl+A to switch background");
        ImGui.Text($"size: {texture.Width}x{texture.Height}");
        DrawRectangle(0,0,Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height, UseWhiteBackground ? Rgba.White : Rgba.Black);
        ImGui.TextUnformatted($"Texture ID: {TextureId} ({key})");
        if (ShaderId != -1)
        {
            var shader = Shaders.ElementAt(ShaderId);
            ImGui.TextUnformatted($"ShaderHandle ID: {ShaderId} ({shader.Key})");
            foreach (var variable in PreviewerShaderValues.Where(x => x.Value.Item1 != null))
            {
                switch (variable.Value.Item2)
                {
                    case UniformType.Float:
                        SetShaderValue(shader.Value, 
                            GetShaderLocation(shader.Value, variable.Key), 
                            (float)(variable.Value.Item1 as float?),
                            variable.Value.Item2);
                        break;
                    case UniformType.Vec2:
                        SetShaderValue(shader.Value, 
                            GetShaderLocation(shader.Value, variable.Key), 
                            (Vector2)(variable.Value.Item1 as Vector2?),
                            variable.Value.Item2);
                        break;
                    case UniformType.Vec3:
                        SetShaderValue(shader.Value, 
                            GetShaderLocation(shader.Value, variable.Key), 
                            (Vector3)variable.Value.Item1,
                            variable.Value.Item2);
                        break;
                }
            }
            BeginShaderMode(shader.Value);
        }

        if (ImGui.Button("Set ShaderHandle Values"))
        {
            SetValueMode = true;
        }
        if (ImGui.Button("Reset"))
        {
            Zoom = 1;
            ImageOffset = Vector2.Zero;
        }
        ImGui.End();
        DrawTextureEx(texture, ImageOffset, 0, Zoom, Rgba.White);
        EndShaderMode();
        if (SetValueMode)
        {
            ImGui.Begin("Set Value: ");
            foreach (var shaderValue in PreviewerShaderValues)
            {
                string str = ""+shaderValue.Value.Item1;
                switch (shaderValue.Value.Item2)
                {
                    case UniformType.Float:
                        if (ImGui.InputText(shaderValue.Key, ref str, 0xff))
                        {
                            if (float.TryParse(str, out float value))
                                PreviewerShaderValues[shaderValue.Key] =
                                    (value, PreviewerShaderValues[shaderValue.Key].Item2);
                        }
                        break;
                    case UniformType.Vec2:
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
                    case UniformType.Vec3:
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
        Engine.Backend.EndDebugUi();
    }

    // ---- Background tester (Ctrl+B) ------------------------------------------------------------
    // Cycle every StageBackground implementation live with Left/Right, without reaching a stage or
    // editing GameBox.cs:634. Mirrors the texture viewer above: inline state + an Update/Draw pair,
    // toggled by a Ctrl combo and debounced on wall time (there is no IsKeyPressed).
    private static readonly (string Name, Func<StageBackground> Make)[] BackgroundTesterFactories =
    {
        ("HousesBackground", () => new HousesBackground()),
        ("DrogichinBackground", () => new DrogichinBackground()),
        ("SillyBackground (empty)", () => new SillyBackground()),
        ("Swap: Houses <-> Drogichin", () => new SwapStageBackground(new HousesBackground(), new DrogichinBackground())),
    };
    private bool BackgroundTesterOpen = false;
    private int BackgroundTesterId = 0;
    private int BackgroundTesterBuiltId = -1;
    private StageBackground? BackgroundTesterInstance;
    private TargetHandle BackgroundTesterTarget;
    private bool BackgroundTesterTargetReady = false;
    private double BackgroundTesterLastKey = 0;

    void UpdateBackgroundTester()
    {
        if (GetTime() - TextureViewerDelay < BackgroundTesterLastKey)
            return;
        if (IsKeyDown(KeyCode.LeftControl) && IsKeyDown(KeyCode.B))
        {
            BackgroundTesterOpen = !BackgroundTesterOpen;
            if (BackgroundTesterOpen)
                TextureViewerOpen = false;   // the two testers are mutually exclusive
            BackgroundTesterLastKey = GetTime();
            return;
        }
        if (!BackgroundTesterOpen)
            return;
        if (IsKeyDown(KeyCode.Right))
        {
            BackgroundTesterId = (BackgroundTesterId + 1) % BackgroundTesterFactories.Length;
            BackgroundTesterLastKey = GetTime();
            return;
        }
        if (IsKeyDown(KeyCode.Left))
        {
            BackgroundTesterId = (BackgroundTesterId + BackgroundTesterFactories.Length - 1) % BackgroundTesterFactories.Length;
            BackgroundTesterLastKey = GetTime();
            return;
        }
        if (IsKeyDown(KeyCode.Escape))
        {
            BackgroundTesterOpen = false;
            BackgroundTesterLastKey = GetTime();
        }
    }

    void DrawBackgroundTester()
    {
        if (!BackgroundTesterTargetReady)
        {
            BackgroundTesterTarget = LoadRenderTexture(384, 448);
            BackgroundTesterTargetReady = true;
        }
        if (BackgroundTesterInstance == null || BackgroundTesterBuiltId != BackgroundTesterId)
        {
            BackgroundTesterInstance?.Dispose();
            BackgroundTesterInstance = BackgroundTesterFactories[BackgroundTesterId].Make();
            BackgroundTesterBuiltId = BackgroundTesterId;
        }

        // animate from wall time at the sim rate, exactly as GameBox feeds StageBackground.Draw
        double t = GetTime();
        int tick = (int)(t * 60);
        float delta = (float)(t * 60 - tick);
        BackgroundTesterInstance.Draw(BackgroundTesterTarget, tick, delta);

        DrawRectangle(0, 0, Width, Height, Rgba.Black);
        // aspect-fit the 384x448 playfield into the window, centered and undistorted
        float scale = Height / 448f;
        float w = 384f * scale;
        float x = (Width - w) / 2f;
        DrawTexturePro(BackgroundTesterTarget.Texture,
            Helper.GetFullSourceRenderTexture(BackgroundTesterTarget),
            new Rect(x, 0, w, Height), Vector2.Zero, 0, Rgba.White);

        if (!Engine.Backend.SupportsDebugUi)
            return;
        Engine.Backend.BeginDebugUi();
        ImGui.Begin("Background Tester");
        ImGui.Text($"[{BackgroundTesterId + 1}/{BackgroundTesterFactories.Length}] {BackgroundTesterFactories[BackgroundTesterId].Name}");
        ImGui.Text("Left / Right: cycle backgrounds");
        ImGui.Text("Ctrl+B or Esc: close");
        ImGui.End();
        Engine.Backend.EndDebugUi();
    }
#endif

    private UniformType UniformType = UniformType.Float;
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
    private Dictionary<string, (object?, UniformType)> PreviewerShaderValues = new();
}

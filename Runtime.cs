using System.Diagnostics;
using System.Net.Mime;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using static DmitryAndDemid.Rendering.Gfx;
using static DmitryAndDemid.Configuration;
using DmitryAndDemid.Backgrounds;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Screens;
using DmitryAndDemid.Utils;
using DmitryAndDemid.Utils.DualSense;
using System.Text.Json.Serialization;


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
    public string VersionString = "0.05a";
    public double Time;
    public int Width;
    public int Height;
    public float SFXVolume = 1.0f;
    public float MusicVolume = 1.0f;
    public float VoicesVolume = 1.0f;
    public bool DisableClose = false;
    bool ADPTriggered = false;
    /// Wall-clock (<see cref="Gfx.GetTime"/>) deadline at which the loading screen hands off to the main menu.
    /// Set at the end of <see cref="Load"/> and polled every frame in <see cref="PreRender"/>. This deliberately
    /// replaces a <c>Task.Delay(...).ContinueWith(...)</c>: on mono-nx (Switch, Mono interpreter) that
    /// continuation never fires, so the game sat on the loading screen forever while the 60 fps loop ran.
    double LoadingSwitchAt = double.PositiveInfinity;
    public Rect FullScreenRect;
    private Rect CurrentScoreSource;
    Rect CurrentScoreTarget;
    public double Scale = 1;
    public float ScaleF = 1;
    public Dictionary<string, ShaderHandle> Shaders = new();
    public Dictionary<string, BasicTexture> Textures = new();
    public Dictionary<string, SoundHandle> Sounds = new();
    public Dictionary<string, FontHandle> Fonts = new();
    public string[] CurrentlyLoadedTags = [];
    public Dictionary<string, BulletRenderingInfo> BulletVisualPresets = new();
    public int GamepadCount = 0;

    /// <summary>
    /// The game always renders into this at its internal 4:3 resolution (Width x Height); the frame is then
    /// blitted into the real window by <see cref="Present"/>. Without this indirection the fixed
    /// Scale = Width/640 layout would run off the bottom of any non-4:3 monitor in fullscreen.
    /// </summary>
    RenderedTexture Backbuffer;

    /// <summary>Where the backbuffer lands inside the window: the whole window, or a letterboxed sub-rect.</summary>
    public Rect PresentRect { get; private set; }

    public FullScreenType WindowMode = FullScreenType.Window;

    public async Task Start()
    {
        // Pick the renderer: --renderer=<name> on the command line wins, else config.json, else Raylib.
        string rendererName = Config.Renderer;
        var hasNvidiaDriverFile = Helper.HasNvidiaDriverFile();
        foreach (string arg in Environment.GetCommandLineArgs())
            if (arg.StartsWith("--renderer=", StringComparison.OrdinalIgnoreCase))
                rendererName = arg["--renderer=".Length..];
        if (rendererName == "vulkan" && hasNvidiaDriverFile)
        {
            rendererName = "silk";
        }
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
        // Engine name AND backend name: the Nikitos Engine is the seam, the backend is what it is running on
        // this launch, and a bug report that says only "Vulkan" has told us half of what we need.
        Engine.Platform.OpenWindow(width, height,
            $"AAG2 ~ Subhumanian Fartalism [{Engine.Name} / {Engine.BackendName}]");
        Engine.Platform.SetWindowIcon("Assets/Textures/icon.png");
        SetWindowMode(Config.FullScreenType);
        Backbuffer = LoadRenderTexture(Width, Height);
        var sugarTexture = LoadTexture(Assets.Resolve("Assets/Textures/sugar_logo.png"));
        if (Engine.Backend.SupportsDebugUi)
            Engine.Backend.SetupDebugUi();
        bool showBuild = false;
        #if DEBUG
        showBuild = true;
        #endif
        #if SWITCH
        showBuild = true;
        #endif
        // Compose the splash into the backbuffer once — the logo is centred in the backbuffer's own space.
        BeginTextureMode(Backbuffer);
        ClearBackground(Rgba.Black);
        if (showBuild)
            DrawText($"{Engine.Name}; Version: {VersionString}; Build: {BuildInfo.Number}; Renderer: {rendererName}", 0, 0, (int)(14 * ScaleF),Rgba.White);
        DrawTexturePro(sugarTexture,
            new Rect(Vector2.Zero, 400, 400),
            new Rect((Width - size) / 2, (Height - size) / 2, size, size),
            Vector2.Zero, 0, Rgba.DarkGray);
        EndTextureMode();

        // Borderless/exclusive fullscreen resizes the window asynchronously, so right after SetWindowMode the OS
        // can still report the old (windowed) size. This splash frame stays frozen on screen for the whole of the
        // synchronous Load() below — the per-frame Present() loop only starts afterwards — so a PresentRect built
        // from the stale size leaves the letterboxed backbuffer (sugar logo and all) pinned in a corner instead of
        // centred. Re-present for a brief real-time window: each BeginDrawing pumps window events and refreshes the
        // reported size, so the final frozen frame lands with the settled fullscreen dimensions. Windowed mode is
        // already correctly sized, so it presents just once.
        void PresentSplash()
        {
            BeginDrawing();
            ClearBackground(Rgba.Black);
            Present();
            EndDrawing();
        }
        if (WindowMode != FullScreenType.Window)
        {
            double settleStart = GetTime();
            do { PresentSplash(); } while (GetTime() - settleStart < 0.25);
        }
        else
        {
            PresentSplash();
        }
        UnloadTexture(sugarTexture);
        SetTargetFPS(Config.FrameCap);
        if (Config.UseVSYNC)
            Engine.Platform.SetVSync(true);
        Engine.Platform.DisableExitKey();
        // Look for a DualSense before the first frame, so the rebinding screen can label buttons the way they
        // are printed on the pad and the one-time DualSense layout can be offered on this launch.
        DualSensePad.Initialize();
        if (DualSensePad.IsConnected && Config.GamepadProfile.Length == 0 && Config.IsUsingDefaultBindings())
            Config.ApplyDualSenseDefaults();
        Time = GetTime();
        double c = 0;
        ScreenLoading = new LoadingScreen();
        AddScreen(ScreenLoading);
        await Load();
        if (BenchMode)
        {
            RunBench();
            Engine.Platform.CloseWindow();
            return;
        }
        while (!WindowShouldClose() || DisableClose)
            RunFrame();
        // Hand the pad back before the window goes: otherwise the lightbar stays on the last frame's colour and
        // the adaptive triggers stay stiff for whatever the player opens next.
        DualSensePad.Shutdown();
        Engine.Platform.CloseWindow();
    }

    /// <summary>
    /// One frame. Desktop spins this in its own loop; Android cannot — there the Activity's GLSurfaceView
    /// owns the loop and calls this from onDrawFrame, so the body has to be reachable on its own.
    /// </summary>
    public void RunFrame()
    {
        while (this.ContinueWorkIn > GetTime())
            Thread.Sleep(1);
        double now = GetTime();
        PreRender(Time - now);
        Render();
        Time = now;
    }

    /// <summary>Set by <c>--bench</c> / config <c>Bench</c>: run <see cref="RunBench"/> instead of the menu loop.</summary>
    public static bool BenchMode;

    /// <summary>
    /// Headless sim-throughput benchmark. Builds a real <see cref="GameBox"/> on the first character + stage and
    /// ticks it as fast as the CPU allows — <see cref="GameBox.BenchMode"/> bypasses the 60 TPS gate — so bullets
    /// spawn and accumulate and the O(n²) collision cost grows the way it does in real play. Prints ticks/sec:
    /// the number that decides whether an interpreter (mono-nx / Switch) can hold 60 TPS under load. Renders
    /// nothing; runs after <see cref="Load"/> so every asset the sim reads is present. See docs/switch-port.md.
    /// </summary>
    void RunBench()
    {
        Console.WriteLine("[bench] starting headless sim-throughput benchmark");
        BenchmarkResult r = Benchmark.Run(muteSfx: true);
        if (r.Error != null)
        {
            Console.WriteLine("[bench] FAILED: " + r.Error);
            return;
        }
        Console.WriteLine($"[bench] backend={r.Backend} targetLoad={r.TargetLoad} peakObjs={r.PeakObjects}");
        Console.WriteLine($"[bench] {r.Ticks} ticks in {r.Seconds:F2}s = {r.TicksPerSec:F0} ticks/s " +
                          $"({r.RealtimeMultiple:F2}x the 60 TPS budget)");
        Console.WriteLine($"[bench] GC heap  max={Benchmark.FormatBytes(r.MemMaxBytes)}  " +
                          $"avg={Benchmark.FormatBytes(r.MemAvgBytes)}  median={Benchmark.FormatBytes(r.MemMedianBytes)}");
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
        bool grade = ColorGradeShader.Id != 0 && ColorGradeActive;
        if (grade)
        {
            BeginShaderMode(ColorGradeShader);
            SetShaderValue(ColorGradeShader, "brightness", Config.Brightness, UniformType.Float);
            SetShaderValue(ColorGradeShader, "contrast", Config.Contrast, UniformType.Float);
            SetShaderValue(ColorGradeShader, "saturation", Config.Saturation, UniformType.Float);
            SetShaderValue(ColorGradeShader, "hue", Config.Hue * MathF.PI / 180f, UniformType.Float);
            // The shader raises to 1/gamma; dividing the setting into the standard 2.2 keeps the
            // default (gamma = 2.2, a typical monitor) an exact no-op and makes the setting an offset
            // FROM the player's expected display gamma rather than an absolute exponent.
            SetShaderValue(ColorGradeShader, "gamma", 2.2f / MathF.Max(Config.Gamma, 0.05f), UniformType.Float);
            SetShaderValue(ColorGradeShader, "colorblind", ColorBlindModeIndex(Config.ColorBlind), UniformType.Int);
        }
        BeginBlendMode(BlendMode.CopyRgb);
        DrawTexturePro(
            Backbuffer.Texture,
            new Rect(0, Height, Width, -Height), // render targets are stored bottom-up
            PresentRect,
            Vector2.Zero, 0, Rgba.White);
        EndBlendMode();
        if (grade)
            EndShaderMode();
    }

    /// <summary>The colourblind uniform's mode number for a config value; 0 (no simulation) for Normal.</summary>
    private static int ColorBlindModeIndex(ColorBlindMode mode) => mode switch
    {
        ColorBlindMode.Protanopia => 1,
        ColorBlindMode.Deuteranopia => 2,
        ColorBlindMode.Tritanopia => 3,
        ColorBlindMode.Tritanomaly => 4,
        ColorBlindMode.Deuteranomaly => 5,
        ColorBlindMode.Achromatopsia => 6,
        _ => 0,
    };


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
        void Mark(string s)
        {
#if SWITCH
            Console.WriteLine($"[load] {s}");
#endif
        }
        try
        {
            Mark("shaders…");    LoadShaders();
            Mark("colorgrade…"); LoadColorGradeShader();
            Mark("fonts…");      LoadFonts();
            Mark("textures…"); LoadTextures(["main", "load"]);
            Mark("shaderAttribs…"); Helper.LoadShaderAttribs();
            Mark("bullets…");    LoadBullets();
            Mark("audio…");      LoadAudio();
            Mark("load done");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[load] EXCEPTION: " + ex);
            throw;
        }
        // Arm the loading-screen → main-menu hand-off. Driven from the render loop (PreRender), NOT a
        // Task.Delay continuation — on mono-nx the threadpool timer never fires it and the game strands on
        // the loading screen (the 60 fps loop keeps running, nothing switches). GetTime() is wall-clock, so
        // a per-frame deadline is equivalent on desktop/Android and the only thing that works on Switch.
        #if DEBUG
        LoadingSwitchAt = GetTime() + 0.5;
        #else
        LoadingSwitchAt = GetTime() + (Config.FastLoading ? 3.0 : 33.0);
        #endif
        return Task.CompletedTask;
    }

    /// <summary>
    /// Set by the Android "Configure" app shortcut: the game opens straight into its settings screen. The
    /// desktop configurator is a separate GTK app, so this path is Android's stand-in for it.
    /// </summary>
    public static bool OpenSettingsOnStart;

    /// <summary>
    /// Crash-proof trace for the Switch bring-up: appends one line to <c>/mono/crashtrace.txt</c> and closes the
    /// handle each call, so the mark survives a hard interpreter crash + <c>svcExitProcess</c> — unlike
    /// <see cref="Console.WriteLine"/>, whose managed buffer is lost on crash. Read the file back over FTP.
    /// </summary>
    public static void SwTrace(string s)
    {
#if SWITCH
        try { System.IO.File.AppendAllText(Utils.Platform.DataPath("crashtrace.txt"), s + "\n"); } catch { }
#endif
    }

    void SwitchToMain()
    {
        SwTrace("[switch] new MainScreen()…");
        ScreenMain = new MainScreen();
#if SWITCH
        SwTrace("[switch] MainScreen built; swapping screens");
#endif
        RemoveScreen(ScreenLoading);
        AddScreen(ScreenMain);
#if SWITCH
        SwTrace("[switch] SwitchToMain done");
#endif
        if (OpenSettingsOnStart)
        {
            OpenSettingsOnStart = false;
            AddScreen(new SettingsScreen());
        }

        // TEMP (Mali crash bring-up): if a sentinel file exists in the writable data dir, jump straight into a
        // Default game with the first character/stage — the same path PersonSelectScreen.OpenNext takes — so the
        // gameplay-entry crash reproduces deterministically without fighting flaky injected menu input. Toggle
        // with `adb shell run-as co.sugar.aag2 touch files/autostart.txt`. Remove once the crash is fixed.
        try
        {
            if (System.IO.File.Exists(Utils.Platform.DataPath("autostart.txt")))
            {
                Utils.Platform.Trace("[autostart] launching Default game");
                string personFile = Utils.Assets.Files("Assets/Data/PlayablePersons/", "*.json")[0];
                var person = System.Text.Json.JsonSerializer.Deserialize<Data.ProtogonistData>(
                    System.IO.File.ReadAllText(personFile));
                string stagePath = Data.Archive.FileStageInfo.CampaignStagePaths()[0];
                var pkg = Utils.BitPackage.OpenStreamReadPackage(stagePath);
                var stage = Data.Archive.FileStageInfo.Load(ref pkg);
                pkg.Dispose();
                var gp = new Screens.GameplayScreen(person!, 0, [stage], 0, false);
                AddScreen(gp);
                Screens.TiledLoadingScreen? tls = null;
                tls = new Screens.TiledLoadingScreen(3, 0.5, () => RemoveScreen(tls!), true, 0);
                AddScreen(tls);
                Utils.Platform.Trace("[autostart] screens queued");
            }
        }
        catch (Exception e)
        {
            Utils.Platform.Trace("[autostart] failed: " + e);
        }
    }

    void LoadAudio()
    {
        foreach (var file in Assets.Files("Assets/Sounds"))
        {
            SoundHandle sound = LoadSound(file);
            // A sound that fails to load is still registered, so Sounds["name"] keeps resolving and callers
            // stay unchanged — it just plays nothing. Say so once at boot: an unplayable file is otherwise
            // indistinguishable from one nobody triggers, which is how three silent FLACs went unnoticed.
            if (!sound.IsValid && Engine.Audio.IsAvailable)
                Console.Error.WriteLine($"[audio] {Path.GetFileName(file)} did not load — it will be silent");
            Sounds[Path.GetFileNameWithoutExtension(file)] = sound;
        }
    }
    
    public void UnloadTextures()
    {
        foreach (var x in RenderedTextureLoadedIntoMainList)
            Gfx.UnloadRenderTexture(x);
        RenderedTextureLoadedIntoMainList.Clear();
        foreach (var x in Textures)
            Gfx.UnloadTexture(x.Value);
        Textures.Clear();
    }


    List<RenderedTexture> RenderedTextureLoadedIntoMainList = new();
    

    public void LoadRenderTextureIntoList(string key, RenderedTexture rTexture)
    {
        RenderedTextureLoadedIntoMainList.Add(rTexture);
        Textures[key] = rTexture.Texture;
    }

    public void LoadTextureWithConfig(string key, TextureLoadingProperties conf, BasicTexture texture)
    {
        int w = texture.Width, h = texture.Height;
        if (conf.MatchGameResolutionScaling)
        {
            if(conf.FullResolutionScaling > ScaleF)
            {
                float s = conf.FullResolutionScaling / ScaleF;
                w = (int)(w / s);
                h = (int)(h / s);
            }
        }
        if(Configuration.Config.TextureQuality == 1)
        {
            w /= conf.MidQualityDivider;
            h /= conf.MidQualityDivider;
        }
        else if(Configuration.Config.TextureQuality == 0)
        {
            w /= conf.LowQualityDivider;
            h /= conf.LowQualityDivider;
        }
        if (w != texture.Width)
        {
            var scaledTexture = LoadRenderTexture(w, h);
            BeginTextureMode(scaledTexture);
            DrawTexturePro(texture,
                new Rect(0, 0, texture.Width, -texture.Height),
                new Rect(0, 0, w, h),
                Vector2.Zero, 0, Rgba.White);
            EndTextureMode();
            LoadRenderTextureIntoList(key, scaledTexture);
            Gfx.UnloadTexture(texture);
        }
        else
        {
            Textures[key] = texture;
        }
    }

    public void LoadTextures(string[] tags)
    {
        TextureLoadingProperties? props;
        string textureConfName = "", key = "";
        foreach (var x in Assets.Files("Assets/Textures", "*.png"))
        {
            key = Path.GetFileName(x);
            textureConfName = Path.ChangeExtension(x, ".json");
            if (File.Exists(textureConfName))
            {
                props = JsonSerializer.Deserialize<TextureLoadingProperties>(File.ReadAllText(textureConfName));
                if (props != null)
                {
                    if (props.TextureLoadGroup == "" || tags.Contains(props.TextureLoadGroup))
                        LoadTextureWithConfig(key, props, LoadTexture(x));
                }
                else
                    Textures[key] = LoadTexture(x);
            }
            else
            {
                Textures[key] = LoadTexture(x);
            }
        }
        Textures["MenuItemSelectionGradient1"] = Helper.RenderSelectionBackground(200, 200, 0);
        Textures["MenuBackground"] = Helper.FillTextureWithColor(Rgba.Black with { A = 128 }, Width, Height).Texture;
        Textures["Copyright"] = Helper.DrawTextScaled(")(U,2026 Konu9lnpaBa Caxap Ko.", 12, 2, 2, 1, Fonts["kodemono"], "gradient").Texture;
        Textures["Version"] = Helper.DrawTextScaled($"Beer {VersionString} (npo6Ha9l Bepcu9I)", 12, 2, 2, 1, Fonts["kodemono"], "gradient").Texture;
        Textures["384x448"] = Helper.FillTextureWithColor(Rgba.White, 384, 448).Texture;
        PrepareScoreTexture();
        
        Textures = Textures.OrderBy(x => x.Key).ToDictionary();
        CurrentlyLoadedTags = tags;
    }

    public void LoadTextureGroup(string tag)
    {
        if(CurrentlyLoadedTags.Contains(tag)) return;
        TextureLoadingProperties? props;
        string textureConfName = "", key = "";
        foreach (var x in Assets.Files("Assets/Textures", "*.png"))
        {
            key = Path.GetFileName(x);
            textureConfName = Path.ChangeExtension(x, ".json");
            if (File.Exists(textureConfName))
            {
                props = JsonSerializer.Deserialize<TextureLoadingProperties>(File.ReadAllText(textureConfName));
                if (props != null)
                    if (tag.Equals(props.TextureLoadGroup))
                        LoadTextureWithConfig(key, props, LoadTexture(x));
            }
        }
        CurrentlyLoadedTags = CurrentlyLoadedTags.Union([tag]).ToArray();
    }

    /// <summary>
    /// Frees every texture whose .json group is NOT in <paramref name="keep"/> — the selective counterpart to
    /// <see cref="UnloadTextures"/>, which empties the whole dictionary (shared and untagged art included) and
    /// so can only run where no living screen still holds a cached handle. Untagged textures (no .json, or an
    /// empty group) and the procedural entries are always kept: they belong to every set. Called when the
    /// title screen regains focus, which is the one point every flow that loaded extra groups (a run, a demo,
    /// an ending, the staff roll) is guaranteed to have left behind.
    /// </summary>
    public void UnloadTextureGroupsExcept(params string[] keep)
    {
        var keepSet = new HashSet<string>(keep);
        var groupOf = new Dictionary<string, string>();
        foreach (string file in Assets.Files("Assets/Textures", "*.png"))
        {
            string conf = Path.ChangeExtension(file, ".json");
            if (!File.Exists(conf))
                continue;
            TextureLoadingProperties? props =
                JsonSerializer.Deserialize<TextureLoadingProperties>(File.ReadAllText(conf));
            if (!string.IsNullOrEmpty(props?.TextureLoadGroup))
                groupOf[Path.GetFileName(file)] = props!.TextureLoadGroup;
        }
        foreach (string key in Textures.Keys.ToList())
        {
            if (!groupOf.TryGetValue(key, out string? group) || keepSet.Contains(group))
                continue;
            // A quality-scaled texture is the colour half of a render texture (see LoadTextureWithConfig);
            // free the whole target, not just the attachment.
            RenderedTexture scaled =
                RenderedTextureLoadedIntoMainList.FirstOrDefault(r => r.Texture.Id == Textures[key].Id);
            if (scaled.Id != 0)
            {
                Gfx.UnloadRenderTexture(scaled);
                RenderedTextureLoadedIntoMainList.Remove(scaled);
            }
            else
                Gfx.UnloadTexture(Textures[key]);
            Textures.Remove(key);
        }
        CurrentlyLoadedTags = CurrentlyLoadedTags.Where(keepSet.Contains).ToArray();
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
        RenderedTexture
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
        {
#if SWITCH
            Console.WriteLine($"[load] bullet {Path.GetFileName(x)}");
#endif
            BulletVisualPresets[Path.GetFileNameWithoutExtension(x)] =
                JsonSerializer.Deserialize<BulletRenderingInfo>(File.ReadAllText(x), jso);
        }
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

    // ---- Ease-of-access colour grading (InvalidSettingsScreen) -----------------------------------
    // Compiled from an embedded source string rather than shipped in Assets/Shaders: the Vulkan
    // backend has no runtime GLSL compiler (its shaders are precompiled SPIR-V sidecars), so a file in
    // the scanned folder would come back ShaderHandle.None there and trip the load-error popup. From
    // source, Vulkan's LoadShaderFromSource just declines, GLES declines by failing the compile, and
    // either way grading simply stays off instead of breaking the launch.
    private ShaderHandle ColorGradeShader;

    private const string ColorGradeSource = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform float brightness;
        uniform float contrast;
        uniform float saturation;
        uniform float hue;
        uniform float gamma;
        uniform int colorblind;

        void main()
        {
            vec3 c = texture2D(texture0, fragTexCoord).rgb * colDiffuse.rgb;
            c = (c * brightness - 0.5) * contrast + 0.5;
            float luma = dot(c, vec3(0.299, 0.587, 0.114));
            c = mix(vec3(luma), c, saturation);

            if (hue != 0.0)
            {
                vec3 yiq = mat3(0.299, 0.595716, 0.211456,
                                0.587, -0.274453, -0.522591,
                                0.114, -0.321263, 0.311135) * c;
                float angle = atan(yiq.z, yiq.y) + hue;
                float chroma = length(yiq.yz);
                c = mat3(1.0, 1.0, 1.0,
                         0.9563, -0.2721, -1.1070,
                         0.6210, -0.6474, 1.7046) * vec3(yiq.x, chroma * cos(angle), chroma * sin(angle));
            }

            // Machado et al. (2009) simulation matrices at full severity; the -omaly modes blend them
            // partway back towards the untouched picture, achromatopsia is a plain luma collapse.
            if (colorblind == 1 || colorblind == 4 || colorblind == 5 || colorblind == 2 || colorblind == 3)
            {
                mat3 m = colorblind == 1 || colorblind == 4
                    ? mat3(0.152286, 0.114503, -0.003882,
                           1.052583, 0.786281, -0.048116,
                           -0.204868, 0.099216, 1.051998)          // protan
                    : colorblind == 2 || colorblind == 5
                    ? mat3(0.367322, 0.280085, -0.011820,
                           0.860646, 0.672501, 0.042940,
                           -0.227968, 0.047413, 0.968881)          // deutan
                    : mat3(1.255528, -0.078411, 0.004733,
                           -0.076749, 0.930809, 0.691367,
                           -0.178779, 0.147602, 0.303900);         // tritan
                float severity = colorblind >= 4 ? 0.6 : 1.0;      // 4/5 are the -omaly modes
                c = mix(c, m * c, severity);
            }
            else if (colorblind == 6)
                c = vec3(dot(c, vec3(0.299, 0.587, 0.114)));

            c = pow(max(c, vec3(0.0)), vec3(1.0 / gamma));
            gl_FragColor = vec4(c, 1.0);
        }
        """;

    /// <summary>True when any ease-of-access colour value differs from its neutral default.</summary>
    private static bool ColorGradeActive =>
        Config.Contrast != 1f || Config.Brightness != 1f || Config.Saturation != 1f ||
        Config.Hue != 0f || Config.Gamma != 2.2f || Config.ColorBlind != ColorBlindMode.Normal;

    void LoadColorGradeShader()
    {
        // Vulkan cannot compile GLSL at runtime (precompiled SPIR-V only) — grading is off there.
        if (Engine.BackendName == "Vulkan")
            return;
        ColorGradeShader = LoadShaderFromMemory(null, ColorGradeSource);
        if (ColorGradeShader.Id == 0)
            Console.WriteLine("Color-grade shader failed to compile — ease-of-access grading is off.");
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

    /// <summary>
    /// Tears down every screen above the persistent <see cref="ScreenMain"/> — e.g. once Extra mode's
    /// results/replay-save cascade finishes, so the player lands directly back on the main menu instead of
    /// revealing the DifficultyScreen/PersonSelectScreen chain a normal run's screens are pushed on top of and
    /// never pop (see <see cref="AddScreen"/> callers throughout <c>Screens/</c>).
    /// </summary>
    public void ReturnToMainMenu()
    {
        foreach (Screen screen in Screens)
            if (screen != ScreenMain)
                RemoveScreen(screen);
    }

    /// <summary>The screen immediately below <paramref name="screen"/> in the stack, or null if it's the
    /// bottom (or not present). Used by screens that render a real-time capture of what's behind them, e.g. the
    /// windowed <see cref="Screens.ListSelectScreen"/> refracting Settings through its glass panel.</summary>
    public Screen? GetScreenBelow(Screen screen)
    {
        int index = Screens.IndexOf(screen);
        return index > 0 ? Screens[index - 1] : null;
    }

    private bool WasHelpKeyDown = false;
    #if DEBUG
    bool WasDebugKeyDown = false;
    #endif
    
    void PreRender(double delta)
    {
        bool isHelpKeyDown = IsKeyDown(KeyCode.F1);
        if(!WasHelpKeyDown && isHelpKeyDown)
            Helper.OpenWebPage("https://support.google.com/chrome");
        WasHelpKeyDown = isHelpKeyDown;
        #if DEBUG
        bool isKeyDebugDown = IsKeyDown(KeyCode.F3);
        if (!WasDebugKeyDown && isKeyDebugDown)
            ShowDebugInformation = !ShowDebugInformation;
        WasDebugKeyDown = isKeyDebugDown;
        #endif
        if (ScreenRefreshRequired)
            RefreshScreens();
        // Loading-screen -> main-menu hand-off, armed in Load(). Polled here (render loop) instead of via a
        // Task.Delay continuation, which never fires on mono-nx. Fires once, then disarms.
        if (GetTime() >= LoadingSwitchAt)
        {
            LoadingSwitchAt = double.PositiveInfinity;
            if (!ADPTriggered)
            {
                if (IsKeyDown(KeyCode.J))
                {
                    ScreenLoading.SetADPText("Kpajjj AKTUBUPOBAHblJ noJl3OBaTeJlEM..", false);
                    ADPTriggered = true;
                }
                else
                    SwitchToMain();
            }
        }

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
        // Buttons and sticks come from the backend above; this drives the DualSense's lightbar, LEDs, triggers
        // and motors, which no backend can see. It is a no-op with any other pad, or none.
        DualSenseFeedback.Update();
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
    #if DEBUG
    bool ShowDebugInformation = false;
    SystemInfo SystemInfo = Utils.SystemInfo.Collect();
    #endif

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
        if (ShowDebugInformation)
        {
            if (GetTime() - DebugMemLastUpdate > 0.5)
            {
                DebugMemLastUpdate = GetTime();
                try { DebugRamBytes = Environment.WorkingSet; } catch { DebugRamBytes = 0; }
                DebugVramBytes = EstimateTextureVramBytes();
            }
            // Debug builds show renderer / version / build at the top-right of the game area.
            var vf = Fonts["kodemono"];
            float vfs = 14 * ScaleF;
            // The Lihanov name here, the Nikitos one on the splash and the window title — both are the engine's,
            // and this overlay is where a developer is reading rather than a player, so it gets the one the
            // codebase's own comments tend to use.
            string leftDebugInformation = $"Subhumanian Fartalism {VersionString} ({VersionString}/{BuildInfo.Number})\n{Engine.AlternateName} on {Engine.BackendName}\n{GetFPS()} fps T: {(Config.FrameCap == -1 ? "inf" : Config.FrameCap)}\n \nLoaded Texture Groups: {string.Join(", ", CurrentlyLoadedTags)}";
            string rightDebugInformation = $"Runtime: {RuntimeInformation.FrameworkDescription} {RuntimeInformation.ProcessArchitecture}\nMem: {Benchmark.FormatBytes(DebugRamBytes)}\nVMem: {Benchmark.FormatBytes(DebugVramBytes)}\n \nCPU: {Environment.ProcessorCount}x ({SystemInfo.CoreTopology}) {SystemInfo.CpuName} @ {SystemInfo.MaxClockMHz} MHz\n \nDisplay: {GetScreenWidth()}x{GetScreenHeight()} ({SystemInfo.PhysicalCores})\n \n{Config.Renderer}";
            float leftX = WindowMode == FullScreenType.Window ? 0 : PresentRect.X;
            float rightX = WindowMode == FullScreenType.Window ? Width : PresentRect.X + PresentRect.Width;
            float topY = WindowMode == FullScreenType.Window ? 0 : PresentRect.Y;
            DrawMultilineText(vf, leftDebugInformation, new Vector2(leftX + 6 * ScaleF, topY + 4 * ScaleF), vfs, 1, Rgba.White, TextAlign.Start, Rgba.DebugSemiTransparentGray);
            DrawMultilineText(vf, rightDebugInformation, new Vector2(rightX - 6 * ScaleF, topY + 4 * ScaleF), vfs, 1, Rgba.White, TextAlign.Stop, Rgba.DebugSemiTransparentGray);
        }
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

    // RAM / VRAM readout shown at the bottom of the game area (see Render). Refreshed on a slow timer rather
    // than every frame — WorkingSet is cheap, but the VRAM estimate walks the whole Textures dictionary, and
    // neither number moves fast enough to need a per-frame update.
    private long DebugRamBytes, DebugVramBytes;
    private double DebugMemLastUpdate = -1;

    /// <summary>Sum of every loaded texture's Width*Height*4 (assumed RGBA8) — an approximation of the game's
    /// own texture VRAM footprint. It does not count render targets (Backbuffer, per-screen/per-GameBox render
    /// textures), which aren't tracked centrally, so the true figure runs somewhat higher.</summary>
    private long EstimateTextureVramBytes()
    {
        long total = 0;
        foreach (BasicTexture t in Textures.Values)
            total += (long)t.Width * t.Height * 4;
        return total;
    }

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
        BasicTexture texture = Textures.ElementAt(TextureId).Value;
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
        ("DrogichinFlyoverBackground (road -> clouds -> town)", () => new DrogichinFlyoverBackground()),
        ("SillyBackground (empty)", () => new SillyBackground()),
        ("Swap: Houses <-> Drogichin", () => new SwapStageBackground(new HousesBackground(), new DrogichinBackground())),
    };
    /// <summary>Display names of every stage background the tester can build — shared with the gameplay
    /// events editor, which offers the same list in an ImGui combo.</summary>
    public static string[] BackgroundTesterNames => BackgroundTesterFactories.Select(f => f.Name).ToArray();

    public double ContinueWorkIn = 0;

    /// <summary>Builds a fresh instance of the tester background at <paramref name="index"/> (clamped).</summary>
    public static StageBackground CreateTesterBackground(int index) =>
        BackgroundTesterFactories[Math.Clamp(index, 0, BackgroundTesterFactories.Length - 1)].Make();

    private bool BackgroundTesterOpen = false;
    private int BackgroundTesterId = 0;
    private int BackgroundTesterBuiltId = -1;
    private StageBackground? BackgroundTesterInstance;
    private RenderedTexture BackgroundTesterTarget;
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

    private UniformType UniformTypeRuntime = UniformType.Float;
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

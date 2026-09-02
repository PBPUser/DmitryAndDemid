using System.Numerics;
using Raylib_cs;
using rlImGui_cs;
using RlBlendMode = Raylib_cs.BlendMode;
using RlColor = Raylib_cs.Color;
using RlRectangle = Raylib_cs.Rectangle;

using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// Raylib implementation of <see cref="IBackend"/> — the Nikitos Engine's default backend on desktop.
/// Together with SilkGLBackend these are the ONLY files
/// that may reference a graphics API; everything else goes through <see cref="Engine"/>/<see cref="Gfx"/>.
/// </summary>
public sealed class RaylibBackend : IBackend
{
    public const string BaseVertexShaderPath = "Assets/Shaders/base.vs";

    public string Name => "Raylib";

    // Handle tables. Ids start at 1 so default(THandle) is always "none".
    private readonly Dictionary<int, Texture2D> Textures = new();
    private readonly Dictionary<int, RenderTexture2D> Targets = new();
    private readonly Dictionary<int, ShaderRecord> Shaders = new();
    private readonly Dictionary<int, Font> Fonts = new();
    private readonly Dictionary<int, BasicTexture> TargetTextures = new();
    private readonly Dictionary<(int Shader, string Name), int> UniformLocations = new();

    private readonly Stack<RenderedTexture> TargetStack = new();
    private int NextId = 1;
    private int GamepadCountCache;
    private FontHandle DefaultFont;

    private sealed record ShaderRecord(Shader Shader, string[] UniformNames);

    // ---- textures -------------------------------------------------------------------------

    public BasicTexture LoadTexture(string path)
    {
        Texture2D texture = Raylib.LoadTexture(path);
        if (!Raylib.IsTextureValid(texture))
            return BasicTexture.None;
        int id = NextId++;
        Textures[id] = texture;
        return new BasicTexture(id);
    }

    /// <summary>
    /// Raylib has no upload-from-array entry point, so wrap the pixels in an Image that points straight at
    /// them. LoadTextureFromImage copies to the GPU during the call and keeps nothing, so the pin only has to
    /// outlive that call — and there is no Image to UnloadImage afterwards, since raylib never owned the data.
    /// </summary>
    public unsafe BasicTexture LoadTextureFromPixels(byte[] rgba, int width, int height)
    {
        if (!IRenderer.AreLoadablePixels(rgba, width, height))
            return BasicTexture.None;

        Texture2D texture;
        fixed (byte* pixels = rgba)
        {
            Image image = new()
            {
                Data = pixels,
                Width = width,
                Height = height,
                Mipmaps = 1,
                Format = PixelFormat.UncompressedR8G8B8A8,
            };
            texture = Raylib.LoadTextureFromImage(image);
        }

        if (!Raylib.IsTextureValid(texture))
            return BasicTexture.None;
        int id = NextId++;
        Textures[id] = texture;
        return new BasicTexture(id);
    }

    public void UnloadTexture(BasicTexture texture)
    {
        if (Textures.Remove(texture.Id, out Texture2D native))
            Raylib.UnloadTexture(native);
    }

    public bool IsValid(BasicTexture texture) =>
        Textures.TryGetValue(texture.Id, out Texture2D native) && Raylib.IsTextureValid(native);

    public Vector2 GetTextureSize(BasicTexture texture) =>
        Textures.TryGetValue(texture.Id, out Texture2D native)
            ? new Vector2(native.Width, native.Height)
            : Vector2.Zero;

    public void SetTextureFilter(BasicTexture texture, FilterMode filter)
    {
        if (Textures.TryGetValue(texture.Id, out Texture2D native))
            Raylib.SetTextureFilter(native, (TextureFilter)filter);
    }

    /// <summary>Migration escape hatch: adopt a texture created outside the backend (e.g. by rlImGui).</summary>
    public BasicTexture Adopt(Texture2D texture)
    {
        int id = NextId++;
        Textures[id] = texture;
        return new BasicTexture(id);
    }

    /// <summary>Migration escape hatch: the native texture behind a handle (rlImGui needs it).</summary>
    public Texture2D Native(BasicTexture texture) => Textures[texture.Id];

    // ---- render targets -------------------------------------------------------------------

    public RenderedTexture CreateTarget(int width, int height)
    {
        // The game measures text and creates a target that size; empty text measures 0x0. Raylib tolerated
        // a degenerate 0x0 render texture (binding it was a harmless no-op), so clamp rather than fail.
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        RenderTexture2D target = Raylib.LoadRenderTexture(width, height);
        if (!Raylib.IsRenderTextureValid(target))
            return RenderedTexture.None;
        int id = NextId++;
        Targets[id] = target;

        int textureId = NextId++;
        Textures[textureId] = target.Texture;
        TargetTextures[id] = new BasicTexture(textureId);
        return new RenderedTexture(id);
    }

    public void DestroyTarget(RenderedTexture target)
    {
        if (!Targets.Remove(target.Id, out RenderTexture2D native))
            return;
        // The colour attachment dies with the target; drop the alias without a second UnloadTexture.
        if (TargetTextures.Remove(target.Id, out BasicTexture texture))
            Textures.Remove(texture.Id);
        Raylib.UnloadRenderTexture(native);
    }

    public bool IsValid(RenderedTexture target) =>
        Targets.TryGetValue(target.Id, out RenderTexture2D native) && Raylib.IsRenderTextureValid(native);

    public BasicTexture GetTargetTexture(RenderedTexture target) =>
        TargetTextures.GetValueOrDefault(target.Id, BasicTexture.None);

    public int TargetFloor { get; set; }

    public void BeginTarget(RenderedTexture target)
    {
        // Push even for an unknown handle: the caller will still call EndTarget, and the stack has to stay
        // balanced or the pop would unbind the PARENT target instead.
        TargetStack.Push(target);
        if (Targets.TryGetValue(target.Id, out RenderTexture2D native))
            Raylib.BeginTextureMode(native); // flushes the batch before switching; binding over an active FBO is safe
    }

    public void EndTarget()
    {
        if (TargetStack.Count <= TargetFloor)
        {
            // Unbalanced End — ignore rather than popping the frame's own target out from under the
            // remaining draw calls.
#if DEBUG
            Console.WriteLine("Renderer: ignoring unbalanced EndTarget() (no matching BeginTarget above the frame floor).");
#endif
            return;
        }

        TargetStack.Pop();
        if (TargetStack.TryPeek(out RenderedTexture parent) && Targets.TryGetValue(parent.Id, out RenderTexture2D native))
            Raylib.BeginTextureMode(native); // re-bind the parent, do not fall to the window
        else
            Raylib.EndTextureMode();
    }

    public void ResetTargets()
    {
        if (TargetStack.Count == 0)
            return;
        TargetStack.Clear();
        Raylib.EndTextureMode();
    }

    // ---- shaders --------------------------------------------------------------------------

    public ShaderHandle LoadShader(string? vertexPath, string fragmentPath)
    {
        Shader shader = Raylib.LoadShader(Assets.Resolve(vertexPath ?? BaseVertexShaderPath), Assets.Resolve(fragmentPath));
        return RegisterShader(shader, Assets.Exists(fragmentPath) ? Assets.ReadAllText(fragmentPath) : "");
    }

    public ShaderHandle LoadShaderFromSource(string? vertexSource, string fragmentSource)
    {
        Shader shader = Raylib.LoadShaderFromMemory(vertexSource, fragmentSource);
        return RegisterShader(shader, fragmentSource);
    }

    private ShaderHandle RegisterShader(Shader shader, string fragmentSource)
    {
        if (!Raylib.IsShaderValid(shader))
        {
            // Raylib compiles inside the native library; whatever the driver said arrived through the trace
            // callback ShaderDiagnostics installed, so there is nothing to add here beyond the fallback.
            if (!ShaderDiagnostics.HasError)
                ShaderDiagnostics.Report("driver rejected the shader (no compiler log)");
            return ShaderHandle.None;
        }
        int id = NextId++;
        Shaders[id] = new ShaderRecord(shader, ShaderSource.ParseUniformNames(fragmentSource));
        return new ShaderHandle(id);
    }

    public void UnloadShader(ShaderHandle shader)
    {
        if (!Shaders.Remove(shader.Id, out ShaderRecord? record))
            return;
        foreach ((int Shader, string Name) key in UniformLocations.Keys.Where(k => k.Shader == shader.Id).ToArray())
            UniformLocations.Remove(key);
        Raylib.UnloadShader(record.Shader);
    }

    public bool IsValid(ShaderHandle shader) =>
        Shaders.TryGetValue(shader.Id, out ShaderRecord? record) && Raylib.IsShaderValid(record.Shader);

    public void BeginShader(ShaderHandle shader)
    {
        if (Shaders.TryGetValue(shader.Id, out ShaderRecord? record))
            Raylib.BeginShaderMode(record.Shader);
    }

    public void EndShader() => Raylib.EndShaderMode();

    public IReadOnlyList<string> GetUniformNames(ShaderHandle shader) =>
        Shaders.TryGetValue(shader.Id, out ShaderRecord? record) ? record.UniformNames : Array.Empty<string>();

    public int GetUniformLocation(ShaderHandle shader, string name)
    {
        if (!Shaders.TryGetValue(shader.Id, out ShaderRecord? record))
            return -1;
        (int, string) key = (shader.Id, name);
        if (UniformLocations.TryGetValue(key, out int location))
            return location;
        location = Raylib.GetShaderLocation(record.Shader, name);
        UniformLocations[key] = location;
        return location;
    }

    public void SetUniform<T>(ShaderHandle shader, int location, T value, UniformType type) where T : unmanaged
    {
        if (location < 0 || !Shaders.TryGetValue(shader.Id, out ShaderRecord? record))
            return;
        Raylib.SetShaderValue(record.Shader, location, value, (ShaderUniformDataType)type);
    }

    public void SetUniform<T>(ShaderHandle shader, string name, T value, UniformType type) where T : unmanaged =>
        SetUniform(shader, GetUniformLocation(shader, name), value, type);

    public void SetUniformArray(ShaderHandle shader, int location, float[] values, UniformType type)
    {
        if (location < 0 || !Shaders.TryGetValue(shader.Id, out ShaderRecord? record))
            return;
        Raylib.SetShaderValueV(record.Shader, location, values, (ShaderUniformDataType)type, 1);
    }

    public void SetUniformTexture(ShaderHandle shader, int location, BasicTexture texture)
    {
        if (location < 0 || !Shaders.TryGetValue(shader.Id, out ShaderRecord? record))
            return;
        if (!Textures.TryGetValue(texture.Id, out Texture2D native))
            return;
        Raylib.SetShaderValueTexture(record.Shader, location, native);
    }

    // ---- fonts and text -------------------------------------------------------------------

    /// <summary>Reads the framebuffer back and writes it where asked. Not Raylib's own TakeScreenshot, which
    /// prefixes its storage base path and so cannot take an absolute destination.</summary>
    public bool TakeScreenshot(string path)
    {
        Image image = Raylib.LoadImageFromScreen();
        bool ok = Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
        return ok;
    }

    public FontHandle LoadFont(string path, int size)
    {
        Font font = Raylib.LoadFontEx(path, size, [], 0);
        int id = NextId++;
        Fonts[id] = font;
        return new FontHandle(id);
    }

    public void UnloadFont(FontHandle font)
    {
        if (Fonts.Remove(font.Id, out Font native))
            Raylib.UnloadFont(native);
    }

    public FontHandle GetDefaultFont()
    {
        if (DefaultFont.IsValid)
            return DefaultFont;
        int id = NextId++;
        Fonts[id] = Raylib.GetFontDefault();
        DefaultFont = new FontHandle(id);
        return DefaultFont;
    }

    public Vector2 MeasureText(FontHandle font, string text, float fontSize, float spacing) =>
        Fonts.TryGetValue(font.Id, out Font native)
            ? Raylib.MeasureTextEx(native, text, fontSize, spacing)
            : Vector2.Zero;

    public void DrawText(FontHandle font, string text, Vector2 position, float fontSize, float spacing, Rgba tint)
    {
        if (Fonts.TryGetValue(font.Id, out Font native))
            Raylib.DrawTextEx(native, text, position, fontSize, spacing, ToColor(tint));
    }

    public void DrawTextPro(FontHandle font, string text, Vector2 position, Vector2 origin, float rotation,
        float fontSize, float spacing, Rgba tint)
    {
        if (Fonts.TryGetValue(font.Id, out Font native))
            Raylib.DrawTextPro(native, text, position, origin, rotation, fontSize, spacing, ToColor(tint));
    }

    // ---- drawing --------------------------------------------------------------------------

    public void Clear(Rgba color) => Raylib.ClearBackground(ToColor(color));

    public void DrawTexture(BasicTexture texture, Vector2 position, Rgba tint)
    {
        if (Textures.TryGetValue(texture.Id, out Texture2D native))
            Raylib.DrawTextureEx(native, position, 0, 1, ToColor(tint));
    }

    public void DrawTexture(BasicTexture texture, Vector2 position, float rotation, float scale, Rgba tint)
    {
        if (Textures.TryGetValue(texture.Id, out Texture2D native))
            Raylib.DrawTextureEx(native, position, rotation, scale, ToColor(tint));
    }

    public void DrawTexture(BasicTexture texture, Rect source, Rect destination, Vector2 origin, float rotation,
        Rgba tint)
    {
        if (Textures.TryGetValue(texture.Id, out Texture2D native))
            Raylib.DrawTexturePro(native, ToRect(source), ToRect(destination), origin, rotation, ToColor(tint));
    }

    public void DrawNinePatch(BasicTexture texture, NinePatch patch, Rect destination, Vector2 origin,
        float rotation, Rgba tint)
    {
        if (Textures.TryGetValue(texture.Id, out Texture2D native))
            Raylib.DrawTextureNPatch(native, ToNPatch(patch), ToRect(destination), origin, rotation, ToColor(tint));
    }

    public void DrawRect(Rect rect, Rgba color) => Raylib.DrawRectangleRec(ToRect(rect), ToColor(color));

    public void DrawRect(Rect rect, Vector2 origin, float rotation, Rgba color) =>
        Raylib.DrawRectanglePro(ToRect(rect), origin, rotation, ToColor(color));

    public void DrawLine(Vector2 from, Vector2 to, Rgba color) =>
        Raylib.DrawLineV(from, to, ToColor(color));

    public void BeginBlend(BlendMode mode)
    {
        if (mode == BlendMode.CopyRgb)
        {
            // Straight RGB copy: src * 1 + dst * 0, alpha channel discarded.
            Rlgl.SetBlendFactors(GlOne, GlZero, GlFuncAdd);
            Raylib.BeginBlendMode(RlBlendMode.Custom);
            return;
        }
        Raylib.BeginBlendMode((RlBlendMode)mode);
    }

    public void EndBlend() => Raylib.EndBlendMode();

    private const int GlZero = 0x0000, GlOne = 0x0001, GlFuncAdd = 0x8006;

    // ---- frame / window -------------------------------------------------------------------

    public void BeginFrame() => Raylib.BeginDrawing();

    public void EndFrame() => Raylib.EndDrawing();

    public void OpenWindow(int width, int height, string title)
    {
        // Before the window, so the GL context creation and every later shader compile is logged through us.
        ShaderDiagnostics.CaptureRaylibLog();
        Raylib.InitWindow(width, height, title);
    }

    public void SetWindowIcon(string path)
    {
        if (!Assets.Exists(path))
            return;
        Image icon = Raylib.LoadImage(Assets.Resolve(path));
        Raylib.SetWindowIcon(icon);
        Raylib.UnloadImage(icon);
    }

    public void CloseWindow() => Raylib.CloseWindow();

    public bool ShouldClose => Raylib.WindowShouldClose();

    public void SetWindowSize(int width, int height) => Raylib.SetWindowSize(width, height);

    public WindowMode CurrentWindowMode { get; private set; } = WindowMode.Windowed;

    public int WindowWidth => Raylib.GetScreenWidth();

    public int WindowHeight => Raylib.GetScreenHeight();

    public int MonitorWidth => Raylib.GetMonitorWidth(Raylib.GetCurrentMonitor());

    public int MonitorHeight => Raylib.GetMonitorHeight(Raylib.GetCurrentMonitor());

    /// <summary>
    /// Raylib's fullscreen/borderless states are TOGGLES, not idempotent flags — calling ToggleFullscreen
    /// while already fullscreen turns it off. So always return to a plain window first.
    /// </summary>
    public void ApplyWindowMode(WindowMode mode, int windowedWidth, int windowedHeight)
    {
        if (Raylib.IsWindowFullscreen())
            Raylib.ToggleFullscreen();
        if (Raylib.IsWindowState(ConfigFlags.BorderlessWindowMode))
            Raylib.ToggleBorderlessWindowed();

        int monitor = Raylib.GetCurrentMonitor();
        switch (mode)
        {
            case WindowMode.Borderless:
            case WindowMode.BorderlessDotByDot:
                Raylib.ToggleBorderlessWindowed();
                break;

            case WindowMode.Exclusive:
                Raylib.SetWindowSize(Raylib.GetMonitorWidth(monitor), Raylib.GetMonitorHeight(monitor));
                Raylib.ToggleFullscreen();
                break;

            case WindowMode.Windowed:
            default:
                Raylib.SetWindowSize(windowedWidth, windowedHeight);
                Vector2 monitorPosition = Raylib.GetMonitorPosition(monitor);
                Raylib.SetWindowPosition(
                    (int)monitorPosition.X + (Raylib.GetMonitorWidth(monitor) - windowedWidth) / 2,
                    (int)monitorPosition.Y + (Raylib.GetMonitorHeight(monitor) - windowedHeight) / 2);
                break;
        }

        CurrentWindowMode = mode;
    }

    public void SetVSync(bool enabled)
    {
        if (enabled)
            Raylib.SetWindowState(ConfigFlags.VSyncHint);
        else
            Raylib.ClearWindowState(ConfigFlags.VSyncHint);
    }

    public void SetTargetFps(int fps) => Raylib.SetTargetFPS(fps);

    public void DisableExitKey() => Raylib.SetExitKey(KeyboardKey.Null);

    public double Time => Raylib.GetTime();

    public int Fps => Raylib.GetFPS();

    public void DrawFpsCounter(int x, int y) => Raylib.DrawFPS(x, y);

    // ---- input ----------------------------------------------------------------------------

    public bool IsKeyDown(KeyCode key) => Raylib.IsKeyDown((KeyboardKey)key);

    public bool IsMouseDown(MouseBtn button) => Raylib.IsMouseButtonDown((MouseButton)button);

    public Vector2 MousePosition => Raylib.GetMousePosition();

    public Vector2 MouseDelta => Raylib.GetMouseDelta();

    public float MouseWheel => Raylib.GetMouseWheelMove();

    public int GamepadCount => GamepadCountCache;

    public void RefreshGamepads()
    {
        int count = 0;
        while (Raylib.IsGamepadAvailable(count))
            count++;
        GamepadCountCache = count;
    }

    public bool IsPadDown(PadButton button)
    {
        for (int i = 0; i < GamepadCountCache; i++)
            if (Raylib.IsGamepadButtonDown(i, (GamepadButton)button))
                return true;
        return false;
    }

    public float GetPadAxis(PadAxis axis)
    {
        float value = 0;
        for (int i = 0; i < GamepadCountCache; i++)
        {
            float current = Raylib.GetGamepadAxisMovement(i, (GamepadAxis)axis);
            if (MathF.Abs(current) > MathF.Abs(value))
                value = current;
        }
        return value;
    }

    public PadButton? GetPressedPadButton()
    {
        int button = Raylib.GetGamepadButtonPressed();
        return button <= 0 ? null : (PadButton)button;
    }

    // ---- touch ----------------------------------------------------------------------------

    public int TouchCount => Raylib.GetTouchPointCount();

    public Vector2 GetTouchPosition(int index) => Raylib.GetTouchPosition(index);

    // ---- audio (shared component; also used by the Silk backend) --------------------------

    private readonly RaylibAudio AudioDevice = new();

    public bool IsAvailable => AudioDevice.IsAvailable;

    public float SfxVolume
    {
        get => AudioDevice.SfxVolume;
        set => AudioDevice.SfxVolume = value;
    }

    public bool Initialize() => AudioDevice.Initialize();

    public SoundHandle LoadSound(string path) => AudioDevice.LoadSound(path);

    public SoundHandle LoadSoundFromPcm(short[] samples, int sampleRate, int channels) =>
        AudioDevice.LoadSoundFromPcm(samples, sampleRate, channels);

    public void UnloadSound(SoundHandle sound) => AudioDevice.UnloadSound(sound);

    public void Play(SoundHandle sound) => AudioDevice.Play(sound);

    // ---- debug UI (ImGui via rlImGui — Raylib-specific) ------------------------------------

    public bool SupportsDebugUi => true;

    public void SetupDebugUi() => rlImGui.Setup(true);

    public void BeginDebugUi() => rlImGui.Begin();

    public void EndDebugUi() => rlImGui.End();

    public void DebugUiImage(BasicTexture texture)
    {
        if (Textures.TryGetValue(texture.Id, out Texture2D native))
            rlImGui.Image(native);
    }

    public void DebugUiImage(RenderedTexture target)
    {
        if (Targets.TryGetValue(target.Id, out RenderTexture2D native))
            rlImGui.ImageRenderTexture(native);
    }

    // ---- teardown -------------------------------------------------------------------------

    public void Dispose()
    {
        AudioDevice.Dispose();
        foreach (ShaderRecord record in Shaders.Values)
            Raylib.UnloadShader(record.Shader);
        foreach (RenderTexture2D target in Targets.Values)
            Raylib.UnloadRenderTexture(target);

        // Target colour attachments are freed by UnloadRenderTexture; don't double-free them.
        HashSet<int> targetTextureIds = TargetTextures.Values.Select(t => t.Id).ToHashSet();
        foreach ((int id, Texture2D texture) in Textures)
            if (!targetTextureIds.Contains(id))
                Raylib.UnloadTexture(texture);

        Textures.Clear();
        Targets.Clear();
        TargetTextures.Clear();
        Shaders.Clear();
        Fonts.Clear();
        UniformLocations.Clear();

    }

    // ---- conversions (the Raylib boundary) ------------------------------------------------

    private static RlColor ToColor(Rgba c) => new(c.R, c.G, c.B, c.A);

    private static RlRectangle ToRect(Rect r) => new(r.X, r.Y, r.Width, r.Height);

    private static NPatchInfo ToNPatch(NinePatch p) => new()
    {
        Source = ToRect(p.Source),
        Left = p.Left,
        Top = p.Top,
        Right = p.Right,
        Bottom = p.Bottom,
        Layout = (NPatchLayout)p.Layout,
    };
}

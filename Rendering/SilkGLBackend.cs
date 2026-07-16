using System.Numerics;
using System.Runtime.InteropServices;
#if !ANDROID
using Silk.NET.Input;
#endif
using Silk.NET.Maths;
using Silk.NET.OpenGL;
#if !ANDROID
using Silk.NET.Windowing;
#endif
using StbImageSharp;
using StbTrueTypeSharp;

using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// Second renderer: Silk.NET windowing + OpenGL 3.3 core.
///
/// It deliberately reproduces Raylib's conventions rather than inventing its own, which is what lets the
/// game's ~60 existing GLSL shaders load UNCHANGED:
///   - vertex attributes are bound to the names Assets/Shaders/base.vs declares:
///     vertexPosition / vertexTexCoord / vertexNormal / vertexColor;
///   - the shaders' standard uniforms are mvp, texture0 and colDiffuse;
///   - the projection is a y-down ortho (0,0 = top-left), for both the window and render targets, so a
///     target's contents land bottom-up in texture memory exactly as they do under Raylib — which is why
///     the game's negative-height source rectangles keep flipping correctly.
///
/// Audio is still delegated to Raylib's mixer (it is window-independent). That is the one remaining seam;
/// swapping it for Silk.NET.OpenAL touches nothing outside this file.
/// </summary>
public sealed unsafe class SilkGLBackend : IBackend
{
    public const string BaseVertexShaderPath = "Assets/Shaders/base.vs";

    public string Name => "Silk.NET/OpenGL";

#if !ANDROID
    private IWindow Window = null!;
#endif
    private GL Gl = null!;
#if !ANDROID
    private IInputContext Input = null!;
    private IKeyboard? Keyboard;
    private IMouse? Mouse;
#endif

    private uint QuadVao, QuadVbo, QuadEbo;
    private uint DefaultProgram;
    private TextureHandle WhitePixel;

    private readonly Dictionary<int, GlTexture> Textures = new();
    private readonly Dictionary<int, GlTarget> Targets = new();
    private readonly Dictionary<int, GlShader> Shaders = new();
    private readonly Dictionary<int, GlFont> Fonts = new();
    private readonly Dictionary<int, TextureHandle> TargetTextures = new();

    private readonly Stack<TargetHandle> TargetStack = new();
    private ShaderHandle ActiveShader;
    private int NextId = 1;

    private int FrameWidth, FrameHeight;   // dimensions of whatever we are currently drawing into
    private int SurfaceWidth, SurfaceHeight;   // Android: the fixed GLSurfaceView size, for window-space draws
    private double StartTime;
    private int FrameCounter;
    private double FpsAccumulator;
    private int FpsValue;

    private sealed class GlTexture
    {
        public uint Id;
        public int Width, Height;
        public bool OwnedByTarget;
    }

    private sealed class GlTarget
    {
        public uint Fbo;
        public uint ColorTexture;
        public int Width, Height;
    }

    private sealed class GlShader
    {
        public uint Program;
        public string[] UniformNames = [];
        public readonly Dictionary<string, int> Locations = new();
    }

    private sealed class GlFont
    {
        public TextureHandle Atlas;
        public float BaseSize;
        public readonly Dictionary<char, Glyph> Glyphs = new();
    }

    private struct Glyph
    {
        public float U0, V0, U1, V1;
        public float OffsetX, OffsetY;
        public float AdvanceX;
        public float Width, Height;
    }

    // ---- window ---------------------------------------------------------------------------
    // Android has no window of its own: the Activity's GLSurfaceView owns the surface and the context, and
    // AttachExternalContext (below) takes it from there. Everything in this section is GLFW-shaped and only
    // exists on desktop; the Android stubs at the end of the section answer the same interface.
#if !ANDROID

    public void OpenWindow(int width, int height, string title)
    {
        WindowOptions options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(width, height),
            Title = title,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default,
                new APIVersion(3, 3)),
            VSync = false,
        };

        Window = Silk.NET.Windowing.Window.Create(options);
        Window.Initialize();

        Gl = GL.GetApi(Window);
        IsGles = (Gl.GetStringS(StringName.Version) ?? "").Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);
        Input = Window.CreateInput();
        Keyboard = Input.Keyboards.FirstOrDefault();
        Mouse = Input.Mice.FirstOrDefault();

        Gl.Enable(EnableCap.Blend);
        Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        BuildQuad();
        DefaultProgram = BuildProgram(Assets.ReadAllText(ShaderPath(BaseVertexShaderPath)), DefaultFragmentSource);
        WhitePixel = CreateSolidTexture(255, 255, 255, 255);

        StartTime = (double)Environment.TickCount64 / 1000.0;
        SurfaceWidth = FrameWidth = width;
        SurfaceHeight = FrameHeight = height;
    }

    public void CloseWindow() => Window?.Close();

    public bool ShouldClose
    {
        get
        {
            Window.DoEvents();
            return Window.IsClosing;
        }
    }

    public void SetWindowSize(int width, int height) => Window.Size = new Vector2D<int>(width, height);

    public WindowMode CurrentWindowMode { get; private set; } = WindowMode.Windowed;

    public int WindowWidth => Window.Size.X;

    public int WindowHeight => Window.Size.Y;

    public int MonitorWidth => Window.Monitor?.VideoMode.Resolution?.X ?? Window.Size.X;

    public int MonitorHeight => Window.Monitor?.VideoMode.Resolution?.Y ?? Window.Size.Y;

    public void ApplyWindowMode(WindowMode mode, int windowedWidth, int windowedHeight)
    {
        switch (mode)
        {
            case WindowMode.Borderless:
            case WindowMode.BorderlessDotByDot:
                Window.WindowBorder = WindowBorder.Hidden;
                Window.Position = Window.Monitor?.Bounds.Origin ?? new Vector2D<int>(0, 0);
                Window.Size = new Vector2D<int>(MonitorWidth, MonitorHeight);
                break;

            case WindowMode.Exclusive:
                Window.WindowState = WindowState.Fullscreen;
                break;

            case WindowMode.Windowed:
            default:
                Window.WindowState = WindowState.Normal;
                Window.WindowBorder = WindowBorder.Resizable;
                Window.Size = new Vector2D<int>(windowedWidth, windowedHeight);
                Window.Center();
                break;
        }
        CurrentWindowMode = mode;
    }

    public void SetVSync(bool enabled) => Window.VSync = enabled;

    public void SetWindowIcon(string path)
    {
        if (!Assets.Exists(path))
            return;
        ImageResult image = ImageResult.FromMemory(Assets.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
        // Silk wants the pixels pinned for the duration of the call; a RawImage over the managed array does it.
        var raw = new Silk.NET.Core.RawImage(image.Width, image.Height, image.Data);
        Window.SetWindowIcon([raw]);
    }

#else   // ANDROID

    public void OpenWindow(int width, int height, string title) =>
        throw new NotSupportedException("On Android the Activity owns the surface; use AttachExternalContext.");

    public void CloseWindow() { }

    /// <summary>Android decides when the app ends; the game never closes its own window.</summary>
    public bool ShouldClose => false;

    public void SetWindowSize(int width, int height) { }

    public WindowMode CurrentWindowMode => WindowMode.Borderless;

    // The surface size is fixed for the Activity's lifetime; FrameWidth/Height, by contrast, tracks whatever
    // is currently bound (a render target during target draws), so the window size must come from here, not
    // from FrameWidth — otherwise Present() reads a target's size and mis-scales the whole frame.
    public int WindowWidth => SurfaceWidth;
    public int WindowHeight => SurfaceHeight;
    public int MonitorWidth => SurfaceWidth;
    public int MonitorHeight => SurfaceHeight;

    /// <summary>Always fullscreen — there is no other mode on a phone.</summary>
    public void ApplyWindowMode(WindowMode mode, int windowedWidth, int windowedHeight) { }

    /// <summary>The compositor paces the frames; GLSurfaceView is already vsynced.</summary>
    public void SetVSync(bool enabled) { }

    /// <summary>The launcher icon is an Android resource (@drawable/icon); there is no window icon to set.</summary>
    public void SetWindowIcon(string path) { }

#endif

    public void SetTargetFps(int fps)
    {
        // Silk drives its own loop timing; the game paces itself off Time, so nothing to do here.
    }

    public void DisableExitKey()
    {
        // Silk has no built-in exit key to disable.
    }

    public double Time => (double)Environment.TickCount64 / 1000.0 - StartTime;

    public int Fps => FpsValue;

    public void DrawFpsCounter(int x, int y) => DrawText(GetDefaultFont(), $"{FpsValue} FPS",
        new Vector2(x, y), 44, 3, Rgba.Lime);

    // ---- frame ----------------------------------------------------------------------------

    public void BeginFrame()
    {
        // SWITCH, like Android, drives GL through an SDL-owned external context (SdlGlBackend) — there is no GLFW
        // Window to poll or swap, and the surface size is the one AttachExternalContext was given.
#if !ANDROID && !SWITCH
        Window.DoEvents();
        FrameWidth = Window.Size.X;
        FrameHeight = Window.Size.Y;
#else
        FrameWidth = SurfaceWidth;
        FrameHeight = SurfaceHeight;
#endif
        Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        Gl.Viewport(0, 0, (uint)FrameWidth, (uint)FrameHeight);
    }

    public void EndFrame()
    {
#if !ANDROID && !SWITCH
        Window.SwapBuffers();   // on Android GLSurfaceView / on Switch SdlGlBackend swaps for us instead
#endif

        FrameCounter++;
        double now = Time;
        if (now - FpsAccumulator >= 1.0)
        {
            FpsValue = FrameCounter;
            FrameCounter = 0;
            FpsAccumulator = now;
        }
    }

    // ---- textures -------------------------------------------------------------------------

    public TextureHandle LoadTexture(string path)
    {
        if (!Assets.Exists(path))
            return TextureHandle.None;

        using Stream stream = Assets.OpenRead(path);
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return CreateTexture(image.Data, image.Width, image.Height);
    }

    private TextureHandle CreateTexture(byte[] rgba, int width, int height)
    {
        uint id = Gl.GenTexture();
        Gl.BindTexture(TextureTarget.Texture2D, id);
        fixed (byte* p = rgba)
        {
            Gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, p);
        }
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        Gl.BindTexture(TextureTarget.Texture2D, 0);

        int handle = NextId++;
        Textures[handle] = new GlTexture { Id = id, Width = width, Height = height };
        return new TextureHandle(handle);
    }

    private TextureHandle CreateSolidTexture(byte r, byte g, byte b, byte a) =>
        CreateTexture([r, g, b, a], 1, 1);

    /// <summary>
    /// Upload a texture from RGBA pixels already in NATIVE memory (e.g. an SDL_Surface) instead of a managed
    /// byte[]. Skips the Large Object Heap allocation that <see cref="ImageResult"/> makes — essential on the
    /// Switch/mono-nx interpreter, whose ~21 MB LOS can't hold a single 3840×2880 (44 MB) decode. Rows must be
    /// tightly packed (pitch == width*4, which SDL's RGBA32 surfaces are). Used by SdlGlBackend's LoadTexture.
    /// </summary>
    public TextureHandle CreateTextureFromNativePixels(IntPtr rgba, int width, int height)
    {
        uint id = Gl.GenTexture();
        Gl.BindTexture(TextureTarget.Texture2D, id);
        Gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, (void*)rgba);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        Gl.BindTexture(TextureTarget.Texture2D, 0);

        int handle = NextId++;
        Textures[handle] = new GlTexture { Id = id, Width = width, Height = height };
        return new TextureHandle(handle);
    }

    public void UnloadTexture(TextureHandle texture)
    {
        if (!Textures.Remove(texture.Id, out GlTexture? t))
            return;
        if (!t.OwnedByTarget)
            Gl.DeleteTexture(t.Id);
    }

    public bool IsValid(TextureHandle texture) => Textures.ContainsKey(texture.Id);

    public Vector2 GetTextureSize(TextureHandle texture) =>
        Textures.TryGetValue(texture.Id, out GlTexture? t) ? new Vector2(t.Width, t.Height) : Vector2.Zero;

    public void SetTextureFilter(TextureHandle texture, FilterMode filter)
    {
        if (!Textures.TryGetValue(texture.Id, out GlTexture? t))
            return;
        int mode = filter == FilterMode.Point ? (int)GLEnum.Nearest : (int)GLEnum.Linear;
        Gl.BindTexture(TextureTarget.Texture2D, t.Id);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, mode);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, mode);
        Gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    // ---- render targets -------------------------------------------------------------------

    public TargetHandle CreateTarget(int width, int height)
    {
        // Empty text measures 0x0; Raylib tolerated a degenerate target, so clamp rather than fail.
        width = Math.Max(1, width);
        height = Math.Max(1, height);

        uint fbo = Gl.GenFramebuffer();
        Gl.BindFramebuffer(FramebufferTarget.Framebuffer, fbo);

        uint color = Gl.GenTexture();
        Gl.BindTexture(TextureTarget.Texture2D, color);
        Gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)width, (uint)height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.Repeat);
        Gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.Repeat);
        Gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, color, 0);

        Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        Gl.BindTexture(TextureTarget.Texture2D, 0);

        int textureId = NextId++;
        Textures[textureId] = new GlTexture { Id = color, Width = width, Height = height, OwnedByTarget = true };

        int id = NextId++;
        Targets[id] = new GlTarget { Fbo = fbo, ColorTexture = color, Width = width, Height = height };
        TargetTextures[id] = new TextureHandle(textureId);
        return new TargetHandle(id);
    }

    public void DestroyTarget(TargetHandle target)
    {
        if (!Targets.Remove(target.Id, out GlTarget? t))
            return;
        if (TargetTextures.Remove(target.Id, out TextureHandle texture))
            Textures.Remove(texture.Id);
        Gl.DeleteFramebuffer(t.Fbo);
        Gl.DeleteTexture(t.ColorTexture);
    }

    public bool IsValid(TargetHandle target) => Targets.ContainsKey(target.Id);

    public TextureHandle GetTargetTexture(TargetHandle target) =>
        TargetTextures.GetValueOrDefault(target.Id, TextureHandle.None);

    public int TargetFloor { get; set; }

    public void BeginTarget(TargetHandle target)
    {
        // Push even for an unknown handle so the matching EndTarget cannot pop the PARENT target.
        TargetStack.Push(target);
        if (Targets.TryGetValue(target.Id, out GlTarget? t))
            Bind(t);
    }

    public void EndTarget()
    {
        if (TargetStack.Count <= TargetFloor)
        {
            // Unbalanced End — ignore, exactly as the Raylib backend does. GL's "unbind" would drop us to
            // the window and silently redirect the rest of the frame.
#if DEBUG
            Console.WriteLine("Renderer: ignoring unbalanced EndTarget().");
#endif
            return;
        }

        TargetStack.Pop();
        if (TargetStack.TryPeek(out TargetHandle parent) && Targets.TryGetValue(parent.Id, out GlTarget? p))
            Bind(p);
        else
            BindWindow();
    }

    public void ResetTargets()
    {
        if (TargetStack.Count == 0)
            return;
        TargetStack.Clear();
        BindWindow();
    }

    private void Bind(GlTarget t)
    {
        Gl.BindFramebuffer(FramebufferTarget.Framebuffer, t.Fbo);
        Gl.Viewport(0, 0, (uint)t.Width, (uint)t.Height);
        FrameWidth = t.Width;
        FrameHeight = t.Height;
    }

    private void BindWindow()
    {
#if !ANDROID && !SWITCH
        FrameWidth = Window.Size.X;
        FrameHeight = Window.Size.Y;
#else
        FrameWidth = SurfaceWidth;
        FrameHeight = SurfaceHeight;
#endif
        Gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        Gl.Viewport(0, 0, (uint)FrameWidth, (uint)FrameHeight);
    }

    // ---- shaders --------------------------------------------------------------------------

    /// <summary>
    /// True when the context is OpenGL ES rather than desktop GL — i.e. Android. ES rejects the game's
    /// #version 330/400 shaders outright, so on ES the backend loads the generated ES variants instead
    /// (Assets/Shaders/gles, produced by Tools/compile_gles_shaders.py).
    /// </summary>
    public bool IsGles { get; private set; }

    private const string GlesShaderDirectory = "Assets/Shaders/gles";

    private string ShaderPath(string path)
    {
        if (!IsGles)
            return path;
        string es = $"{GlesShaderDirectory}/{Path.GetFileName(path)}";
        return Assets.Exists(es) ? es : path;
    }

    public ShaderHandle LoadShader(string? vertexPath, string fragmentPath)
    {
        string vertex = Assets.ReadAllText(ShaderPath(vertexPath ?? BaseVertexShaderPath));
        string fragment = Assets.ReadAllText(ShaderPath(fragmentPath));
        return LoadShaderFromSource(vertex, fragment);
    }

    /// <summary>
    /// Adopts a GL context created by the host instead of opening a window — this is how Android runs, where
    /// the Activity's GLSurfaceView owns the context and the surface, and there is no GLFW at all.
    /// </summary>
    public void AttachExternalContext(GL gl, int width, int height)
    {
        Gl = gl;
        IsGles = (Gl.GetStringS(StringName.Version) ?? "").Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase);

        Gl.Enable(EnableCap.Blend);
        Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        BuildQuad();
        DefaultProgram = BuildProgram(Assets.ReadAllText(ShaderPath(BaseVertexShaderPath)), DefaultFragmentSource);
        WhitePixel = CreateSolidTexture(255, 255, 255, 255);

        StartTime = (double)Environment.TickCount64 / 1000.0;
        SurfaceWidth = FrameWidth = width;
        SurfaceHeight = FrameHeight = height;
    }

    public ShaderHandle LoadShaderFromSource(string? vertexSource, string fragmentSource)
    {
        uint program = BuildProgram(vertexSource ?? Assets.ReadAllText(BaseVertexShaderPath), fragmentSource);
        if (program == 0)
            return ShaderHandle.None;
        int id = NextId++;
        Shaders[id] = new GlShader
        {
            Program = program,
            UniformNames = ShaderSource.ParseUniformNames(fragmentSource),
        };
        return new ShaderHandle(id);
    }

    private uint BuildProgram(string vertexSource, string fragmentSource)
    {
        uint vs = CompileStage(ShaderType.VertexShader, vertexSource);
        uint fs = CompileStage(ShaderType.FragmentShader, fragmentSource);
        if (vs == 0 || fs == 0)
            return 0;

        uint program = Gl.CreateProgram();
        Gl.AttachShader(program, vs);
        Gl.AttachShader(program, fs);

        // Bind the attribute names Raylib uses, so the game's shaders link unchanged.
        Gl.BindAttribLocation(program, 0, "vertexPosition");
        Gl.BindAttribLocation(program, 1, "vertexTexCoord");
        Gl.BindAttribLocation(program, 2, "vertexNormal");
        Gl.BindAttribLocation(program, 3, "vertexColor");

        Gl.LinkProgram(program);
        Gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        if (linked == 0)
        {
            string log = Gl.GetProgramInfoLog(program);
            Console.WriteLine($"SilkGL: shader link failed: {log}");
            ShaderDiagnostics.Report($"link failed: {log}");
        }

        Gl.DeleteShader(vs);
        Gl.DeleteShader(fs);
        return linked == 0 ? 0 : program;
    }

    private uint CompileStage(ShaderType type, string source)
    {
        uint shader = Gl.CreateShader(type);
        Gl.ShaderSource(shader, source);
        Gl.CompileShader(shader);
        Gl.GetShader(shader, ShaderParameterName.CompileStatus, out int ok);
        if (ok != 0)
            return shader;
        string infoLog = Gl.GetShaderInfoLog(shader);
        Console.WriteLine($"SilkGL: {type} compile failed: {infoLog}");
        ShaderDiagnostics.Report($"{(type == ShaderType.VertexShader ? "vertex" : "fragment")} shader: {infoLog}");
        Gl.DeleteShader(shader);
        return 0;
    }

    public void UnloadShader(ShaderHandle shader)
    {
        if (Shaders.Remove(shader.Id, out GlShader? s))
            Gl.DeleteProgram(s.Program);
    }

    public bool IsValid(ShaderHandle shader) => Shaders.ContainsKey(shader.Id);

    public void BeginShader(ShaderHandle shader) => ActiveShader = shader;

    public void EndShader() => ActiveShader = ShaderHandle.None;

    public IReadOnlyList<string> GetUniformNames(ShaderHandle shader) =>
        Shaders.TryGetValue(shader.Id, out GlShader? s) ? s.UniformNames : Array.Empty<string>();

    public int GetUniformLocation(ShaderHandle shader, string name)
    {
        if (!Shaders.TryGetValue(shader.Id, out GlShader? s))
            return -1;
        if (s.Locations.TryGetValue(name, out int cached))
            return cached;
        int location = Gl.GetUniformLocation(s.Program, name);
        s.Locations[name] = location;
        return location;
    }

    public void SetUniform<T>(ShaderHandle shader, int location, T value, UniformType type) where T : unmanaged
    {
        if (location < 0 || !Shaders.TryGetValue(shader.Id, out GlShader? s))
            return;
        Gl.UseProgram(s.Program);
        switch (type)
        {
            case UniformType.Float: Gl.Uniform1(location, (float)(object)value!); break;
            case UniformType.Int: Gl.Uniform1(location, Convert.ToInt32(value)); break;
            case UniformType.Vec2:
            {
                Vector2 v = (Vector2)(object)value!;
                Gl.Uniform2(location, v.X, v.Y);
                break;
            }
            case UniformType.Vec3:
            {
                Vector3 v = (Vector3)(object)value!;
                Gl.Uniform3(location, v.X, v.Y, v.Z);
                break;
            }
            case UniformType.Vec4:
            {
                Vector4 v = (Vector4)(object)value!;
                Gl.Uniform4(location, v.X, v.Y, v.Z, v.W);
                break;
            }
        }
    }

    public void SetUniform<T>(ShaderHandle shader, string name, T value, UniformType type) where T : unmanaged =>
        SetUniform(shader, GetUniformLocation(shader, name), value, type);

    public void SetUniformArray(ShaderHandle shader, int location, float[] values, UniformType type)
    {
        if (location < 0 || !Shaders.TryGetValue(shader.Id, out GlShader? s))
            return;
        Gl.UseProgram(s.Program);
        switch (type)
        {
            case UniformType.Vec2 when values.Length >= 2: Gl.Uniform2(location, values[0], values[1]); break;
            case UniformType.Vec3 when values.Length >= 3: Gl.Uniform3(location, values[0], values[1], values[2]); break;
            case UniformType.Vec4 when values.Length >= 4: Gl.Uniform4(location, values[0], values[1], values[2], values[3]); break;
            case UniformType.Float when values.Length >= 1: Gl.Uniform1(location, values[0]); break;
        }
    }

    public void SetUniformTexture(ShaderHandle shader, int location, TextureHandle texture)
    {
        if (location < 0 || !Shaders.TryGetValue(shader.Id, out GlShader? s))
            return;
        if (!Textures.TryGetValue(texture.Id, out GlTexture? t))
            return;
        // Unit 0 is the primary texture (texture0); extra sampler uniforms get unit 1+.
        Gl.UseProgram(s.Program);
        Gl.ActiveTexture(TextureUnit.Texture1);
        Gl.BindTexture(TextureTarget.Texture2D, t.Id);
        Gl.Uniform1(location, 1);
        Gl.ActiveTexture(TextureUnit.Texture0);
    }

    // ---- drawing --------------------------------------------------------------------------

    public void Clear(Rgba color)
    {
        Gl.ClearColor(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        Gl.Clear((uint)ClearBufferMask.ColorBufferBit);
    }

    public void DrawTexture(TextureHandle texture, Vector2 position, Rgba tint) =>
        DrawTexture(texture, position, 0, 1, tint);

    public void DrawTexture(TextureHandle texture, Vector2 position, float rotation, float scale, Rgba tint)
    {
        Vector2 size = GetTextureSize(texture);
        DrawTexture(texture,
            new Rect(0, 0, size.X, size.Y),
            new Rect(position.X, position.Y, size.X * scale, size.Y * scale),
            Vector2.Zero, rotation, tint);
    }

    public void DrawTexture(TextureHandle texture, Rect source, Rect destination, Vector2 origin, float rotation,
        Rgba tint)
    {
        if (!Textures.TryGetValue(texture.Id, out GlTexture? t))
            return;
        DrawQuad(t, source, destination, origin, rotation, tint);
    }

    public void DrawNinePatch(TextureHandle texture, NinePatch patch, Rect destination, Vector2 origin,
        float rotation, Rgba tint)
    {
        // Straight stretch of the source rect. The game uses nine-patch for one UI frame; the corners are
        // not preserved here, which is a known cosmetic gap of this backend.
        DrawTexture(texture, patch.Source, destination, origin, rotation, tint);
    }

    public void DrawRect(Rect rect, Rgba color) => DrawRect(rect, Vector2.Zero, 0, color);

    public void DrawRect(Rect rect, Vector2 origin, float rotation, Rgba color)
    {
        if (!Textures.TryGetValue(WhitePixel.Id, out GlTexture? white))
            return;
        DrawQuad(white, new Rect(0, 0, 1, 1), rect, origin, rotation, color);
    }

    public void DrawLine(Vector2 from, Vector2 to, Rgba color)
    {
        Vector2 delta = to - from;
        float length = delta.Length();
        if (length < 0.0001f)
            return;
        float angle = MathF.Atan2(delta.Y, delta.X) * 180f / MathF.PI;
        DrawRect(new Rect(from.X, from.Y, length, 1), Vector2.Zero, angle, color);
    }

    public void BeginBlend(BlendMode mode)
    {
        switch (mode)
        {
            case BlendMode.CopyRgb:
                // Straight copy: ignore the source alpha entirely. This is what lets the frame's own
                // backbuffer be blitted without its (degraded) alpha punching holes in the image.
                Gl.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
                break;
            case BlendMode.Additive:
                Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                break;
            case BlendMode.Multiplied:
                Gl.BlendFunc(BlendingFactor.DstColor, BlendingFactor.OneMinusSrcAlpha);
                break;
            default:
                Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                break;
        }
    }

    public void EndBlend() => Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

    private void BuildQuad()
    {
        QuadVao = Gl.GenVertexArray();
        Gl.BindVertexArray(QuadVao);

        QuadVbo = Gl.GenBuffer();
        Gl.BindBuffer(BufferTargetARB.ArrayBuffer, QuadVbo);
        Gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(4 * VertexFloats * sizeof(float)), null,
            BufferUsageARB.DynamicDraw);

        QuadEbo = Gl.GenBuffer();
        Gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, QuadEbo);
        uint[] indices = [0, 1, 2, 0, 2, 3];
        fixed (uint* p = indices)
        {
            Gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), p,
                BufferUsageARB.StaticDraw);
        }

        uint stride = VertexFloats * sizeof(float);
        Gl.EnableVertexAttribArray(0); // vertexPosition (vec3)
        Gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        Gl.EnableVertexAttribArray(1); // vertexTexCoord (vec2)
        Gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));
        Gl.EnableVertexAttribArray(2); // vertexNormal (vec3)
        Gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, (void*)(5 * sizeof(float)));
        Gl.EnableVertexAttribArray(3); // vertexColor (vec4)
        Gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, stride, (void*)(8 * sizeof(float)));

        Gl.BindVertexArray(0);
    }

    private const uint VertexFloats = 12; // pos3 + uv2 + normal3 + color4

    /// <summary>
    /// One textured, rotated, tinted quad — the equivalent of Raylib's DrawTexturePro, including its
    /// semantics: rotation is in DEGREES about <paramref name="origin"/>, and a negative source width or
    /// height flips the sampled region (which is how render targets get drawn right way up).
    /// </summary>
    private void DrawQuad(GlTexture texture, Rect source, Rect dest, Vector2 origin, float rotation, Rgba tint)
    {
        // Negative source width/height mean FLIP, exactly as in Raylib's DrawTexturePro. Getting this wrong
        // is not subtle: the game flips every render target by passing a negative height (e.g. 0,0,384,-448),
        // and naively dividing that into a negative V makes CLAMP_TO_EDGE pin every row to texel row 0 —
        // i.e. the first pixel smeared across the whole quad.
        // A NEGATIVE DESTINATION extent means "same rectangle", not "flip": the game builds LeftDest from
        // UILeftSource.Size, and that source is the flipped (0, H, W, -H) form, so the HUD's dest height
        // arrives as -1200. Raylib draws that as if it were positive (the flip comes from the negative
        // SOURCE); building the quad literally puts it at y in [-1200, 0], off the top of the screen — which
        // is exactly why the whole right-hand HUD strip was missing on this backend.
        if (dest.Width < 0)
            dest.Width = -dest.Width;
        if (dest.Height < 0)
            dest.Height = -dest.Height;

        // Raylib's DrawTexturePro flip arithmetic, verbatim. Note it deliberately produces texture
        // coordinates OUTSIDE [0,1] for the (0, H, W, -H) source form the game uses for render targets, and
        // relies on GL_REPEAT wrapping to bring them back. Reproduce both or the frame smears: with
        // CLAMP_TO_EDGE those out-of-range coords pin to a single texel row.
        bool flipX = false;
        if (source.Width < 0)
        {
            flipX = true;
            source.Width *= -1;
        }
        if (source.Height < 0)
            source.Y -= source.Height;

        float left = (flipX ? source.X + source.Width : source.X) / texture.Width;
        float right = (flipX ? source.X : source.X + source.Width) / texture.Width;
        float top = source.Y / texture.Height;
        float bottom = (source.Y + source.Height) / texture.Height;

        float r = rotation * MathF.PI / 180f;
        float cos = MathF.Cos(r), sin = MathF.Sin(r);

        // Corners relative to the rotation origin, then rotated and translated — same as Raylib.
        Span<Vector2> corners =
        [
            new(-origin.X, -origin.Y),
            new(-origin.X, -origin.Y + dest.Height),
            new(-origin.X + dest.Width, -origin.Y + dest.Height),
            new(-origin.X + dest.Width, -origin.Y),
        ];

        // top-left, bottom-left, bottom-right, top-right
        Span<Vector2> uv = [new(left, top), new(left, bottom), new(right, bottom), new(right, top)];

        float* vertices = stackalloc float[(int)(4 * VertexFloats)];
        for (int i = 0; i < 4; i++)
        {
            Vector2 c = corners[i];
            float x = dest.X + (c.X * cos - c.Y * sin);
            float y = dest.Y + (c.X * sin + c.Y * cos);

            int o = i * (int)VertexFloats;
            vertices[o + 0] = x;
            vertices[o + 1] = y;
            vertices[o + 2] = 0;
            vertices[o + 3] = uv[i].X;
            vertices[o + 4] = uv[i].Y;
            vertices[o + 5] = 0;
            vertices[o + 6] = 0;
            vertices[o + 7] = 1;
            vertices[o + 8] = tint.R / 255f;
            vertices[o + 9] = tint.G / 255f;
            vertices[o + 10] = tint.B / 255f;
            vertices[o + 11] = tint.A / 255f;
        }

        uint program = DefaultProgram;
        if (ActiveShader.IsValid && Shaders.TryGetValue(ActiveShader.Id, out GlShader? custom))
            program = custom.Program;

        Gl.UseProgram(program);

        // y-down ortho: (0,0) is top-left, matching Raylib's 2D convention.
        Matrix4x4 mvp = Matrix4x4.CreateOrthographicOffCenter(0, FrameWidth, FrameHeight, 0, -1, 1);
        int mvpLocation = Gl.GetUniformLocation(program, "mvp");
        if (mvpLocation >= 0)
            Gl.UniformMatrix4(mvpLocation, 1, false, (float*)&mvp);

        int screenSizeLocation = Gl.GetUniformLocation(program, "screenSize");
        if (screenSizeLocation >= 0)
            Gl.Uniform2(screenSizeLocation, (float)FrameWidth, FrameHeight);

        int diffuseLocation = Gl.GetUniformLocation(program, "colDiffuse");
        if (diffuseLocation >= 0)
            Gl.Uniform4(diffuseLocation, 1f, 1f, 1f, 1f);

        Gl.ActiveTexture(TextureUnit.Texture0);
        Gl.BindTexture(TextureTarget.Texture2D, texture.Id);
        int textureLocation = Gl.GetUniformLocation(program, "texture0");
        if (textureLocation >= 0)
            Gl.Uniform1(textureLocation, 0);

        Gl.BindVertexArray(QuadVao);
        Gl.BindBuffer(BufferTargetARB.ArrayBuffer, QuadVbo);
        Gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(4 * VertexFloats * sizeof(float)), vertices);
        Gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*)0);
        Gl.BindVertexArray(0);
    }

    // The built-in shader that draws a plain textured quad. Two spellings of the same program: desktop GLSL
    // and GL ES (which rejects "#version 330" and needs a precision qualifier). Picked by IsGles, same as the
    // file-backed shaders.
    private string DefaultFragmentSource => IsGles ? EsDefaultFragmentSource : GlDefaultFragmentSource;

    private const string GlDefaultFragmentSource = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        out vec4 finalColor;
        void main()
        {
            finalColor = texture(texture0, fragTexCoord) * colDiffuse * fragColor;
        }
        """;

    private const string EsDefaultFragmentSource = """
        #version 300 es
        precision highp float;
        in vec2 fragTexCoord;
        in vec4 fragColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        out vec4 finalColor;
        void main()
        {
            finalColor = texture(texture0, fragTexCoord) * colDiffuse * fragColor;
        }
        """;

    // ---- fonts and text -------------------------------------------------------------------

    public FontHandle LoadFont(string path, int size)
    {
        byte[] ttf = Assets.ReadAllBytes(path);

        // Size the atlas to the font, don't assume. The game loads fonts at 64 * uiScale, which is 160px at
        // 1600x1200 — 95 glyphs at that size need ~2048², not the 1024² a fixed atlas would give, and
        // overflowing it walks straight off the end of the pixel buffer.
        int cellSize = size + 2;
        int columns = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(95f)));
        int needed = columns * cellSize;
        int atlasWidth = 256;
        while (atlasWidth < needed)
            atlasWidth *= 2;
        int atlasHeight = atlasWidth;
        byte[] pixels = new byte[atlasWidth * atlasHeight];

        GlFont font = new() { BaseSize = size };
        StbTrueType.stbtt_fontinfo info = new();

        fixed (byte* ttfPtr = ttf)
        {
            StbTrueType.stbtt_InitFont(info, ttfPtr, 0);
            float scale = StbTrueType.stbtt_ScaleForPixelHeight(info, size);

            int ascent, descent, lineGap;
            StbTrueType.stbtt_GetFontVMetrics(info, &ascent, &descent, &lineGap);

            int penX = 1, penY = 1, rowHeight = 0;
            for (char c = ' '; c < (char)127; c++)
            {
                int advance, leftBearing;
                StbTrueType.stbtt_GetCodepointHMetrics(info, c, &advance, &leftBearing);

                int x0, y0, x1, y1;
                StbTrueType.stbtt_GetCodepointBitmapBox(info, c, scale, scale, &x0, &y0, &x1, &y1);
                int w = x1 - x0, h = y1 - y0;

                if (penX + w + 1 >= atlasWidth)
                {
                    penX = 1;
                    penY += rowHeight + 1;
                    rowHeight = 0;
                }

                // Hard bound: never let the rasteriser write outside the atlas.
                bool fits = w > 0 && h > 0 && penX + w <= atlasWidth && penY + h <= atlasHeight;
                if (fits)
                {
                    fixed (byte* dst = pixels)
                    {
                        StbTrueType.stbtt_MakeCodepointBitmap(info, dst + penY * atlasWidth + penX,
                            w, h, atlasWidth, scale, scale, c);
                    }
                }
                else if (w > 0 && h > 0)
                {
                    Console.WriteLine($"SilkGL: font atlas full, dropping glyph '{c}' ({path} @ {size}px)");
                    w = h = 0;
                }

                font.Glyphs[c] = new Glyph
                {
                    U0 = penX / (float)atlasWidth,
                    V0 = penY / (float)atlasHeight,
                    U1 = (penX + w) / (float)atlasWidth,
                    V1 = (penY + h) / (float)atlasHeight,
                    OffsetX = x0,
                    OffsetY = y0 + ascent * scale,
                    AdvanceX = advance * scale,
                    Width = w,
                    Height = h,
                };

                penX += w + 1;
                rowHeight = Math.Max(rowHeight, h);
            }
        }

        // Expand the 8-bit coverage atlas to RGBA (white, alpha = coverage).
        byte[] rgba = new byte[atlasWidth * atlasHeight * 4];
        for (int i = 0; i < pixels.Length; i++)
        {
            rgba[i * 4 + 0] = 255;
            rgba[i * 4 + 1] = 255;
            rgba[i * 4 + 2] = 255;
            rgba[i * 4 + 3] = pixels[i];
        }
        font.Atlas = CreateTexture(rgba, atlasWidth, atlasHeight);
        SetTextureFilter(font.Atlas, FilterMode.Bilinear);

        int id = NextId++;
        Fonts[id] = font;
        return new FontHandle(id);
    }

    public void UnloadFont(FontHandle font)
    {
        if (Fonts.Remove(font.Id, out GlFont? f))
            UnloadTexture(f.Atlas);
    }

    private FontHandle DefaultFontHandle;

    public FontHandle GetDefaultFont()
    {
        if (DefaultFontHandle.IsValid)
            return DefaultFontHandle;
        // Fall back to any font shipped with the game.
        string? any = Assets.DirectoryExists("Assets/Fonts")
            ? Assets.Files("Assets/Fonts").FirstOrDefault()
            : null;
        DefaultFontHandle = any != null ? LoadFont(any, 32) : FontHandle.None;
        return DefaultFontHandle;
    }

    public Vector2 MeasureText(FontHandle font, string text, float fontSize, float spacing)
    {
        if (!Fonts.TryGetValue(font.Id, out GlFont? f) || string.IsNullOrEmpty(text))
            return Vector2.Zero;

        float scale = fontSize / f.BaseSize;
        float width = 0;
        foreach (char c in text)
            if (f.Glyphs.TryGetValue(c, out Glyph g))
                width += g.AdvanceX * scale + spacing;
        return new Vector2(width - spacing, fontSize);
    }

    public void DrawText(FontHandle font, string text, Vector2 position, float fontSize, float spacing, Rgba tint)
    {
        if (!Fonts.TryGetValue(font.Id, out GlFont? f) || string.IsNullOrEmpty(text))
            return;
        if (!Textures.TryGetValue(f.Atlas.Id, out GlTexture? atlas))
            return;

        float scale = fontSize / f.BaseSize;
        float x = position.X;

        foreach (char c in text)
        {
            if (!f.Glyphs.TryGetValue(c, out Glyph g))
                continue;

            if (g.Width > 0 && g.Height > 0)
            {
                Rect source = new(
                    g.U0 * atlas.Width, g.V0 * atlas.Height,
                    (g.U1 - g.U0) * atlas.Width, (g.V1 - g.V0) * atlas.Height);
                Rect dest = new(
                    x + g.OffsetX * scale, position.Y + g.OffsetY * scale,
                    g.Width * scale, g.Height * scale);
                DrawQuad(atlas, source, dest, Vector2.Zero, 0, tint);
            }

            x += g.AdvanceX * scale + spacing;
        }
    }

    public void DrawTextPro(FontHandle font, string text, Vector2 position, Vector2 origin, float rotation,
        float fontSize, float spacing, Rgba tint)
    {
        // Rotated text is used only by decorative UI; draw unrotated at the offset position.
        DrawText(font, text, position - origin, fontSize, spacing, tint);
    }

    // ---- input ----------------------------------------------------------------------------
    // Keyboard, mouse and gamepads come from Silk's GLFW input, which does not exist on Android; there the
    // only input is touch, which the Activity pushes in through SetTouches.
#if !ANDROID

    public bool IsKeyDown(KeyCode key) =>
        Keyboard != null && SilkKey(key) is { } k && Keyboard.IsKeyPressed(k);

    private static Silk.NET.Input.Key? SilkKey(KeyCode key) => key switch
    {
        KeyCode.Left => Silk.NET.Input.Key.Left,
        KeyCode.Right => Silk.NET.Input.Key.Right,
        KeyCode.Up => Silk.NET.Input.Key.Up,
        KeyCode.Down => Silk.NET.Input.Key.Down,
        KeyCode.Escape => Silk.NET.Input.Key.Escape,
        KeyCode.Enter => Silk.NET.Input.Key.Enter,
        KeyCode.Space => Silk.NET.Input.Key.Space,
        KeyCode.Tab => Silk.NET.Input.Key.Tab,
        KeyCode.LeftShift => Silk.NET.Input.Key.ShiftLeft,
        KeyCode.RightShift => Silk.NET.Input.Key.ShiftRight,
        KeyCode.LeftControl => Silk.NET.Input.Key.ControlLeft,
        >= KeyCode.A and <= KeyCode.Z => Silk.NET.Input.Key.A + (key - KeyCode.A),
        >= KeyCode.Zero and <= KeyCode.Nine => Silk.NET.Input.Key.Number0 + (key - KeyCode.Zero),
        _ => null,
    };

    public bool IsMouseDown(MouseBtn button) =>
        Mouse != null && Mouse.IsButtonPressed((Silk.NET.Input.MouseButton)button);

    public Vector2 MousePosition => Mouse?.Position ?? Vector2.Zero;

    private Vector2 LastMousePosition;

    public Vector2 MouseDelta
    {
        get
        {
            Vector2 current = MousePosition;
            Vector2 delta = current - LastMousePosition;
            LastMousePosition = current;
            return delta;
        }
    }

    public float MouseWheel => Mouse?.ScrollWheels.FirstOrDefault().Y ?? 0;

    public int GamepadCount => Input?.Gamepads.Count(g => g.IsConnected) ?? 0;

    public void RefreshGamepads()
    {
        // Silk keeps its gamepad list live; nothing to poll.
    }

    public bool IsPadDown(PadButton button)
    {
        if (Input == null)
            return false;
        foreach (IGamepad pad in Input.Gamepads.Where(g => g.IsConnected))
            foreach (Silk.NET.Input.Button b in pad.Buttons)
                if (b.Pressed && (int)b.Name == (int)button)
                    return true;
        return false;
    }

    public float GetPadAxis(PadAxis axis)
    {
        if (Input == null)
            return 0;

        float value = 0;
        foreach (IGamepad pad in Input.Gamepads.Where(g => g.IsConnected))
        {
            // Silk exposes sticks as thumbsticks rather than a flat axis list.
            float current = axis switch
            {
                PadAxis.LeftX => pad.Thumbsticks.Count > 0 ? pad.Thumbsticks[0].X : 0,
                PadAxis.LeftY => pad.Thumbsticks.Count > 0 ? pad.Thumbsticks[0].Y : 0,
                PadAxis.RightX => pad.Thumbsticks.Count > 1 ? pad.Thumbsticks[1].X : 0,
                PadAxis.RightY => pad.Thumbsticks.Count > 1 ? pad.Thumbsticks[1].Y : 0,
                PadAxis.LeftTrigger => pad.Triggers.Count > 0 ? pad.Triggers[0].Position : 0,
                PadAxis.RightTrigger => pad.Triggers.Count > 1 ? pad.Triggers[1].Position : 0,
                _ => 0,
            };
            if (MathF.Abs(current) > MathF.Abs(value))
                value = current;
        }
        return value;
    }

#else   // ANDROID

    // Hardware keyboards do exist on Android (docks, Bluetooth); the Activity forwards key events here so the
    // game's keyboard controls work identically to desktop. Empty when only touch is used.
    // Written from the UI thread (key events, the back gesture), read from the GL thread each frame, so every
    // access is locked.
    private readonly HashSet<KeyCode> PressedKeys = new();

    public void SetKeyState(KeyCode key, bool pressed)
    {
        lock (PressedKeys)
        {
            if (pressed)
                PressedKeys.Add(key);
            else
                PressedKeys.Remove(key);
        }
    }

    public bool IsKeyDown(KeyCode key)
    {
        lock (PressedKeys)
            return PressedKeys.Contains(key);
    }
    public bool IsMouseDown(MouseBtn button) => false;
    public Vector2 MousePosition => Vector2.Zero;
    public Vector2 MouseDelta => Vector2.Zero;
    public float MouseWheel => 0;
    public int GamepadCount => 0;
    public void RefreshGamepads() { }
    public bool IsPadDown(PadButton button) => false;
    public float GetPadAxis(PadAxis axis) => 0;
    public PadButton? GetPressedPadButton() => null;

#endif

    /// <summary>
    /// No touch API in Silk.NET's windowing layer, so on desktop the mouse stands in for one finger while the
    /// left button is held. On Android the Activity pushes the real touch points in.
    /// </summary>
#if ANDROID
    /// <summary>
    /// Live touch points, in surface pixels, pushed in by the Activity — Android delivers touches through the
    /// view's event queue, not through Silk's input (which is GLFW and does not exist here).
    /// </summary>
    private Vector2[] Touches = [];

    public void SetTouches(Vector2[] touches) => Touches = touches;

    public int TouchCount => Touches.Length;

    public Vector2 GetTouchPosition(int index) =>
        index >= 0 && index < Touches.Length ? Touches[index] : Vector2.Zero;
#else
    public int TouchCount => Mouse != null && Mouse.IsButtonPressed(Silk.NET.Input.MouseButton.Left) ? 1 : 0;

    public Vector2 GetTouchPosition(int index) => index == 0 ? MousePosition : Vector2.Zero;
#endif

#if !ANDROID
    public PadButton? GetPressedPadButton()
    {
        if (Input == null)
            return null;
        foreach (IGamepad pad in Input.Gamepads.Where(g => g.IsConnected))
            foreach (Silk.NET.Input.Button b in pad.Buttons)
                if (b.Pressed)
                    return (PadButton)(int)b.Name;
        return null;
    }
#endif

    // ---- audio (still Raylib's mixer — window-independent; the seam to replace with OpenAL) ----

#if ANDROID
    // Injected by the Android host before startup (SoundPool-backed); silent until then. Raylib's mixer has
    // no Android build, so audio there is Android-native rather than shared.
    public IAudio Audio { get; set; } = new NullAudio();
#else
    private readonly RaylibAudio Audio = new();
#endif

    public bool IsAvailable => Audio.IsAvailable;

    public float SfxVolume
    {
        get => Audio.SfxVolume;
        set => Audio.SfxVolume = value;
    }

    public bool Initialize() => Audio.Initialize();

    public SoundHandle LoadSound(string path) => Audio.LoadSound(path);

    public void UnloadSound(SoundHandle sound) => Audio.UnloadSound(sound);

    public void Play(SoundHandle sound) => Audio.Play(sound);

    // ---- debug UI -------------------------------------------------------------------------

    /// <summary>rlImGui is bound to Raylib, so the ImGui editors are simply absent under this backend.</summary>
    public bool SupportsDebugUi => false;

    public void SetupDebugUi()
    {
    }

    public void BeginDebugUi()
    {
    }

    public void EndDebugUi()
    {
    }

    public void DebugUiImage(TextureHandle texture)
    {
    }

    public void DebugUiImage(TargetHandle target)
    {
    }

    // ---- teardown -------------------------------------------------------------------------

    public void Dispose()
    {
        Audio.Dispose();
        foreach (GlTarget t in Targets.Values)
        {
            Gl.DeleteFramebuffer(t.Fbo);
            Gl.DeleteTexture(t.ColorTexture);
        }
        foreach (GlTexture t in Textures.Values.Where(t => !t.OwnedByTarget))
            Gl.DeleteTexture(t.Id);
        foreach (GlShader s in Shaders.Values)
            Gl.DeleteProgram(s.Program);

        Targets.Clear();
        Textures.Clear();
        Shaders.Clear();
        Fonts.Clear();

#if !ANDROID
        Window?.Dispose();
#endif
    }
}

#if SWITCH
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.OpenGL;

namespace DmitryAndDemid.Rendering.Switch;

/// <summary>
/// SDL2 + OpenGL ES backend for the Switch — the SHADER-CAPABLE path. SDL owns the window, the GLES context and
/// input; the game's proven GL renderer (<see cref="SilkGLBackend"/>) does the drawing, attached to the
/// SDL-created context through <c>GL.GetApi</c> + an <c>SDL_GL_GetProcAddress</c> loader, exactly as the Android
/// host attaches to its GLSurfaceView. Unlike the 2D <see cref="SdlBackend"/>, the GLES fragment shaders in
/// <c>Assets/Shaders/gles</c> run for real, so bullet glow / distortion / screen effects come back.
///
/// Requires a mono-nx interpreter built with <c>MONO_NX_USE_OPENGL=1</c> (glad + EGL). On a GL-less interpreter
/// <c>SDL_GL_CreateContext</c> fails and this backend can't start — use the 2D SdlBackend there. Select with the
/// <c>gl</c> renderer key. See docs/switch-port.md.
/// </summary>
public sealed unsafe class SdlGlBackend : IBackend
{
    public string Name => "SDL2+GLES (Switch)";

    private readonly SilkGLBackend Renderer = new();
    private IntPtr Window, GlContext, Controller;
    private readonly Stopwatch Clock = Stopwatch.StartNew();
    private bool quit;
    private int Width_ = 1280, Height_ = 720;

    // =========================================================================================
    // IPlatform — SDL window + GLES context
    // =========================================================================================

    public void OpenWindow(int width, int height, string title)
    {
        Sdl.SDL_Init(Sdl.INIT_VIDEO | Sdl.INIT_AUDIO | Sdl.INIT_GAMECONTROLLER | Sdl.INIT_JOYSTICK);
        Sdl.IMG_Init(2 /* IMG_INIT_PNG */);   // native PNG decode for LoadTexture

        // GLES 3.0 — what Assets/Shaders/gles targets (same as the Android host).
        Sdl.SDL_GL_SetAttribute(Sdl.GL_CONTEXT_PROFILE_MASK, Sdl.GL_CONTEXT_PROFILE_ES);
        Sdl.SDL_GL_SetAttribute(Sdl.GL_CONTEXT_MAJOR_VERSION, 3);
        Sdl.SDL_GL_SetAttribute(Sdl.GL_CONTEXT_MINOR_VERSION, 0);
        Sdl.SDL_GL_SetAttribute(Sdl.GL_DOUBLEBUFFER, 1);

        Console.WriteLine("[gl] SDL_CreateWindow(OPENGL)…");
        Window = Sdl.SDL_CreateWindow(title, Sdl.WINDOWPOS_CENTERED, Sdl.WINDOWPOS_CENTERED, width, height,
            Sdl.WINDOW_OPENGL | Sdl.WINDOW_SHOWN);
        Console.WriteLine($"[gl] window={Window != IntPtr.Zero}; SDL_GL_CreateContext…");
        GlContext = Sdl.SDL_GL_CreateContext(Window);
        Console.WriteLine($"[gl] context={GlContext != IntPtr.Zero} ({Marshal.PtrToStringUTF8(Sdl.SDL_GetError())})");
        Sdl.SDL_GL_MakeCurrent(Window, GlContext);
        Sdl.SDL_GL_SetSwapInterval(0);   // no blocking vsync (it hung the 2D path); the console paces itself
        Sdl.SDL_GL_GetDrawableSize(Window, out Width_, out Height_);
        if (Width_ <= 0 || Height_ <= 0) { Width_ = width; Height_ = height; }

        // Hand Silk a GL API bound to the SDL context's proc loader, then let the shared renderer take over.
        Console.WriteLine("[gl] GL.GetApi…");
        GL gl = GL.GetApi(new LamdaNativeContext(name => Sdl.SDL_GL_GetProcAddress(name)));
        Renderer.AttachExternalContext(gl, Width_, Height_);
        Console.WriteLine($"[gl] renderer attached ({Width_}x{Height_})");

        if (Sdl.SDL_NumJoysticks() > 0 && Sdl.SDL_IsGameController(0) != 0)
            Controller = Sdl.SDL_GameControllerOpen(0);
    }

    public void CloseWindow()
    {
        if (Controller != IntPtr.Zero) { Sdl.SDL_GameControllerClose(Controller); Controller = IntPtr.Zero; }
        if (GlContext != IntPtr.Zero) { Sdl.SDL_GL_DeleteContext(GlContext); GlContext = IntPtr.Zero; }
        if (Window != IntPtr.Zero) { Sdl.SDL_DestroyWindow(Window); Window = IntPtr.Zero; }
        Sdl.SDL_Quit();
    }

    public bool ShouldClose { get { PollEvents(); return quit; } }
    private void PollEvents() { while (Sdl.SDL_PollEvent(out SDL_Event ev) != 0) if (ev.type == Sdl.EVENT_QUIT) quit = true; }

    public void SetWindowIcon(string path) { }
    public void SetWindowSize(int width, int height) { }
    public void ApplyWindowMode(WindowMode mode, int windowedWidth, int windowedHeight) { }
    public WindowMode CurrentWindowMode => WindowMode.Exclusive;
    public int WindowWidth => Width_;
    public int WindowHeight => Height_;
    public int MonitorWidth => Width_;
    public int MonitorHeight => Height_;
    public void SetVSync(bool enabled) => Sdl.SDL_GL_SetSwapInterval(enabled ? 1 : 0);
    public void SetTargetFps(int fps) { }
    public void DisableExitKey() { }
    public double Time => Clock.Elapsed.TotalSeconds;

    private int fps, frameCount;
    private double lastReport;
    public int Fps => fps;
    public void DrawFpsCounter(int x, int y) { }

    // =========================================================================================
    // IInput — SDL_GameController (positional, mirrors SdlBackend)
    // =========================================================================================

    public bool IsKeyDown(KeyCode key) => false;
    public bool IsMouseDown(MouseBtn button) => false;
    public Vector2 MousePosition => Vector2.Zero;
    public Vector2 MouseDelta => Vector2.Zero;
    public float MouseWheel => 0f;

    public int GamepadCount => Controller != IntPtr.Zero ? 1 : 0;
    public void RefreshGamepads()
    {
        PollEvents();
        if (Controller == IntPtr.Zero && Sdl.SDL_NumJoysticks() > 0 && Sdl.SDL_IsGameController(0) != 0)
            Controller = Sdl.SDL_GameControllerOpen(0);
        Sdl.SDL_GameControllerUpdate();
    }

    public bool IsPadDown(PadButton button)
    {
        if (Controller == IntPtr.Zero) return false;
        if (button == PadButton.LeftTrigger2)
            return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_TRIGGERLEFT) > 8000;
        if (button == PadButton.RightTrigger2)
            return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_TRIGGERRIGHT) > 8000;
        int b = SdlButton(button);
        return b >= 0 && Sdl.SDL_GameControllerGetButton(Controller, b) != 0;
    }

    public float GetPadAxis(PadAxis axis)
    {
        if (Controller == IntPtr.Zero) return 0f;
        switch (axis)
        {
            case PadAxis.LeftX:  return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_LEFTX) / 32767f;
            case PadAxis.LeftY:  return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_LEFTY) / 32767f;
            case PadAxis.RightX: return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_RIGHTX) / 32767f;
            case PadAxis.RightY: return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_RIGHTY) / 32767f;
            case PadAxis.LeftTrigger:  return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_TRIGGERLEFT) / 16383.5f - 1f;
            case PadAxis.RightTrigger: return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_TRIGGERRIGHT) / 16383.5f - 1f;
            default: return 0f;
        }
    }

    public PadButton? GetPressedPadButton()
    {
        if (Controller == IntPtr.Zero) return null;
        for (PadButton b = PadButton.LeftFaceUp; b <= PadButton.RightThumb; b++)
            if (IsPadDown(b)) return b;
        return null;
    }

    private static int SdlButton(PadButton b) => b switch
    {
        PadButton.LeftFaceUp     => Sdl.CONTROLLER_BUTTON_DPAD_UP,
        PadButton.LeftFaceRight  => Sdl.CONTROLLER_BUTTON_DPAD_RIGHT,
        PadButton.LeftFaceDown   => Sdl.CONTROLLER_BUTTON_DPAD_DOWN,
        PadButton.LeftFaceLeft   => Sdl.CONTROLLER_BUTTON_DPAD_LEFT,
        // Nintendo face-button LABELS, not Xbox positions. SDL reports the Switch pad positionally
        // (SDL_A=bottom=Nintendo "B", SDL_B=right=Nintendo "A", SDL_X=left=Nintendo "Y", SDL_Y=top=Nintendo "X"),
        // so A<->B and X<->Y are swapped vs the desktop layout. The menu confirms on RightFaceDown and cancels on
        // RightFaceRight (Common/MenuScreen.cs); this makes the physical button printed "A" confirm and "B" cancel,
        // matching Switch conventions instead of the reversed Xbox-positional behaviour.
        PadButton.RightFaceUp    => Sdl.CONTROLLER_BUTTON_X,
        PadButton.RightFaceRight => Sdl.CONTROLLER_BUTTON_A,
        PadButton.RightFaceDown  => Sdl.CONTROLLER_BUTTON_B,
        PadButton.RightFaceLeft  => Sdl.CONTROLLER_BUTTON_Y,
        PadButton.LeftTrigger1   => Sdl.CONTROLLER_BUTTON_LEFTSHOULDER,
        PadButton.RightTrigger1  => Sdl.CONTROLLER_BUTTON_RIGHTSHOULDER,
        PadButton.MiddleLeft     => Sdl.CONTROLLER_BUTTON_BACK,
        PadButton.Middle         => Sdl.CONTROLLER_BUTTON_GUIDE,
        PadButton.MiddleRight    => Sdl.CONTROLLER_BUTTON_START,
        PadButton.LeftThumb      => Sdl.CONTROLLER_BUTTON_LEFTSTICK,
        PadButton.RightThumb     => Sdl.CONTROLLER_BUTTON_RIGHTSTICK,
        _ => -1,
    };

    // Switch touchscreen via SDL. Fingers are polled from device 0 (the panel); SDL keeps this state current as
    // long as events are pumped, which PollEvents() does each frame. Coordinates are normalised 0..1, scaled to
    // the drawable size so they match the window-pixel space WindowToGame expects (same contract as RaylibBackend).
    public int TouchCount
    {
        get
        {
            if (Sdl.SDL_GetNumTouchDevices() <= 0) return 0;
            long dev = Sdl.SDL_GetTouchDevice(0);
            return dev == 0 ? 0 : Sdl.SDL_GetNumTouchFingers(dev);
        }
    }

    public Vector2 GetTouchPosition(int index)
    {
        if (Sdl.SDL_GetNumTouchDevices() <= 0) return Vector2.Zero;
        long dev = Sdl.SDL_GetTouchDevice(0);
        if (dev == 0) return Vector2.Zero;
        IntPtr fingerPtr = Sdl.SDL_GetTouchFinger(dev, index);
        if (fingerPtr == IntPtr.Zero) return Vector2.Zero;
        // SDL_Finger { Sint64 id; float x; float y; float pressure; } — read x/y by fixed offset instead of
        // Marshal.PtrToStructure, which can build a dynamic-code marshaling stub the interpreter can't JIT.
        float x = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(fingerPtr, 8));
        float y = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(fingerPtr, 12));
        return new Vector2(x * Width_, y * Height_);
    }

    // =========================================================================================
    // IAudio — SDL2_mixer (mp3/ogg SFX). Guarded: silent fallback if the mixer isn't shimmed. See SwitchAudio.
    // =========================================================================================

    private readonly SwitchAudio audio = new();
    public bool Initialize() => audio.Initialize();
    public bool IsAvailable => audio.IsAvailable;
    public SoundHandle LoadSound(string path) => audio.LoadSound(path);
    public void UnloadSound(SoundHandle sound) => audio.UnloadSound(sound);
    public void Play(SoundHandle sound) => audio.Play(sound);
    public float SfxVolume { get => audio.SfxVolume; set => audio.SfxVolume = value; }

    // =========================================================================================
    // IRenderer — delegate every draw to the shared GL renderer
    // =========================================================================================

    public TextureHandle LoadTexture(string path)
    {
        // Native decode (SDL2_image → SDL_Surface) uploaded straight to GL — avoids the large managed byte[] that
        // StbImage would put on the interpreter's ~21 MB LOS (a 3840×2880 background is a 44 MB decode that OOMs).
        IntPtr surf = Sdl.IMG_Load(path);
        if (surf == IntPtr.Zero)
        {
            Console.WriteLine($"[gl] IMG_Load failed {path}: {Marshal.PtrToStringUTF8(Sdl.SDL_GetError())}");
            return TextureHandle.None;
        }
        IntPtr conv = Sdl.SDL_ConvertSurfaceFormat(surf, Sdl.PIXELFORMAT_ABGR8888, 0);   // R,G,B,A byte order
        Sdl.SDL_FreeSurface(surf);
        if (conv == IntPtr.Zero) return TextureHandle.None;
        SDL_Surface s = Marshal.PtrToStructure<SDL_Surface>(conv);

        // Cap texture size: the game ships 4K (3840×2880 = 44 MB) backgrounds that hang the GLES upload, and are
        // only ever scaled down to the 384×448 playfield / the screen anyway. Downscale anything over the cap.
        // Safe for the oversized assets, which are full-image backgrounds/illustrations (not coord-sampled atlases).
        const int max = 2048;
        if (s.w > max || s.h > max)
        {
            float sc = max / (float)Math.Max(s.w, s.h);
            int nw = Math.Max(1, (int)(s.w * sc)), nh = Math.Max(1, (int)(s.h * sc));
            IntPtr small = Sdl.SDL_CreateRGBSurfaceWithFormat(0, nw, nh, 32, Sdl.PIXELFORMAT_ABGR8888);
            Sdl.SDL_SetSurfaceBlendMode(conv, Sdl.BLENDMODE_NONE);   // straight copy so alpha is preserved
            Sdl.SDL_BlitScaled(conv, IntPtr.Zero, small, IntPtr.Zero);
            Sdl.SDL_FreeSurface(conv);
            conv = small;
            s = Marshal.PtrToStructure<SDL_Surface>(conv);
            Console.WriteLine($"[gl] downscaled {Path.GetFileName(path)} to {nw}x{nh}");
        }

        TextureHandle h = Renderer.CreateTextureFromNativePixels(s.pixels, s.w, s.h);
        Sdl.SDL_FreeSurface(conv);
        return h;
    }
    // Straight to GL: pixels the game already holds managed need no native decode, so the LOS worry that makes
    // LoadTexture go through SDL2_image doesn't apply — the caller's byte[] is the size it is either way.
    public TextureHandle LoadTextureFromPixels(byte[] rgba, int width, int height) =>
        Renderer.LoadTextureFromPixels(rgba, width, height);

    public void UnloadTexture(TextureHandle texture) => Renderer.UnloadTexture(texture);
    public bool IsValid(TextureHandle texture) => Renderer.IsValid(texture);
    public Vector2 GetTextureSize(TextureHandle texture) => Renderer.GetTextureSize(texture);
    public void SetTextureFilter(TextureHandle texture, FilterMode filter) => Renderer.SetTextureFilter(texture, filter);

    public TargetHandle CreateTarget(int width, int height) => Renderer.CreateTarget(width, height);
    public void DestroyTarget(TargetHandle target) => Renderer.DestroyTarget(target);
    public bool IsValid(TargetHandle target) => Renderer.IsValid(target);
    public TextureHandle GetTargetTexture(TargetHandle target) => Renderer.GetTargetTexture(target);
    public void BeginTarget(TargetHandle target) => Renderer.BeginTarget(target);
    public void EndTarget() => Renderer.EndTarget();
    public int TargetFloor { get => Renderer.TargetFloor; set => Renderer.TargetFloor = value; }
    public void ResetTargets() => Renderer.ResetTargets();

    public ShaderHandle LoadShader(string? vertexPath, string fragmentPath) => Renderer.LoadShader(vertexPath, fragmentPath);
    public ShaderHandle LoadShaderFromSource(string? vertexSource, string fragmentSource) => Renderer.LoadShaderFromSource(vertexSource, fragmentSource);
    public void UnloadShader(ShaderHandle shader) => Renderer.UnloadShader(shader);
    public bool IsValid(ShaderHandle shader) => Renderer.IsValid(shader);
    public void BeginShader(ShaderHandle shader) => Renderer.BeginShader(shader);
    public void EndShader() => Renderer.EndShader();
    public int GetUniformLocation(ShaderHandle shader, string name) => Renderer.GetUniformLocation(shader, name);
    public void SetUniform<T>(ShaderHandle shader, int location, T value, UniformType type) where T : unmanaged => Renderer.SetUniform(shader, location, value, type);
    public void SetUniformTexture(ShaderHandle shader, int location, TextureHandle texture) => Renderer.SetUniformTexture(shader, location, texture);
    public void SetUniformArray(ShaderHandle shader, int location, float[] values, UniformType type) => Renderer.SetUniformArray(shader, location, values, type);
    public void SetUniform<T>(ShaderHandle shader, string name, T value, UniformType type) where T : unmanaged => Renderer.SetUniform(shader, name, value, type);
    public IReadOnlyList<string> GetUniformNames(ShaderHandle shader) => Renderer.GetUniformNames(shader);

    public FontHandle LoadFont(string path, int size) => Renderer.LoadFont(path, size);
    public void UnloadFont(FontHandle font) => Renderer.UnloadFont(font);
    public FontHandle GetDefaultFont() => Renderer.GetDefaultFont();
    public Vector2 MeasureText(FontHandle font, string text, float fontSize, float spacing) => Renderer.MeasureText(font, text, fontSize, spacing);
    public void DrawText(FontHandle font, string text, Vector2 position, float fontSize, float spacing, Rgba tint) => Renderer.DrawText(font, text, position, fontSize, spacing, tint);
    public void DrawTextPro(FontHandle font, string text, Vector2 position, Vector2 origin, float rotation, float fontSize, float spacing, Rgba tint) => Renderer.DrawTextPro(font, text, position, origin, rotation, fontSize, spacing, tint);

    public void Clear(Rgba color) => Renderer.Clear(color);
    public void DrawTexture(TextureHandle texture, Vector2 position, Rgba tint) => Renderer.DrawTexture(texture, position, tint);
    public void DrawTexture(TextureHandle texture, Vector2 position, float rotation, float scale, Rgba tint) => Renderer.DrawTexture(texture, position, rotation, scale, tint);
    public void DrawTexture(TextureHandle texture, Rect source, Rect destination, Vector2 origin, float rotation, Rgba tint) => Renderer.DrawTexture(texture, source, destination, origin, rotation, tint);
    public void DrawNinePatch(TextureHandle texture, NinePatch patch, Rect destination, Vector2 origin, float rotation, Rgba tint) => Renderer.DrawNinePatch(texture, patch, destination, origin, rotation, tint);
    public void DrawRect(Rect rect, Rgba color) => Renderer.DrawRect(rect, color);
    public void DrawRect(Rect rect, Vector2 origin, float rotation, Rgba color) => Renderer.DrawRect(rect, origin, rotation, color);
    public void DrawLine(Vector2 from, Vector2 to, Rgba color) => Renderer.DrawLine(from, to, color);
    public void BeginBlend(BlendMode mode) => Renderer.BeginBlend(mode);
    public void EndBlend() => Renderer.EndBlend();

    public void BeginFrame() => Renderer.BeginFrame();

    public void EndFrame()
    {
        Renderer.EndFrame();            // renders; under SWITCH it does NOT swap (external context)
        audio.Pump();                   // mix + queue this frame's audio (once per frame, main thread)
        Sdl.SDL_GL_SwapWindow(Window);  // we present
        frameCount++;
        double now = Clock.Elapsed.TotalSeconds;
        if (now - lastReport >= 1.0) { fps = (int)(frameCount / (now - lastReport)); Console.WriteLine($"[gl] loop alive: {fps} fps"); frameCount = 0; lastReport = now; }
    }

    // =========================================================================================
    // IBackend — debug UI (never on Switch)
    // =========================================================================================

    public bool SupportsDebugUi => false;
    public void SetupDebugUi() { }
    public void BeginDebugUi() { }
    public void EndDebugUi() { }
    public void DebugUiImage(TextureHandle texture) { }
    public void DebugUiImage(TargetHandle target) { }

    public void Dispose() { Renderer.Dispose(); CloseWindow(); }
}
#endif

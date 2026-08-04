#if SWITCH
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace DmitryAndDemid.Rendering.Switch;

/// <summary>
/// Nintendo Switch (Horizon OS) backend: mono-nx runtime + deko3d GPU. See <c>docs/switch-port.md</c>.
///
/// This is a Phase-1 <b>scaffold</b>. It establishes the seam and the platform/input/clock plumbing that can be
/// reasoned about now; the GPU draw path is deliberately left as TODOs because it depends on (a) the mono-nx
/// native fork exporting the deko3d symbols in <see cref="Dk"/>, and (b) the offline UAM shader pipeline. Draw
/// calls are no-ops rather than throws so that, once a device build exists, the game loop can run and be
/// brought up incrementally (clear → quads → textures → shaders) without every frame aborting.
///
/// Nothing here compiles into the desktop/Android builds — the whole file is under <c>#if SWITCH</c>.
/// </summary>
public sealed unsafe class Deko3dBackend : IBackend
{
    public string Name => "deko3d (Switch)";

    // ---- deko3d / libnx objects (owned for the lifetime of the window) --------------------------------
    private IntPtr Device;
    private IntPtr Queue;
    private IntPtr Swapchain;
    private IntPtr Pad;            // native HidPad state buffer (Marshal-allocated)

    // ---- clock (IPlatform.Time drives the whole 60 TPS tick) ------------------------------------------
    // A managed monotonic clock, used regardless of platform — it needs no libnx symbol and is accurate enough
    // to drive the fixed tick. (The libnx armGetSystemTick clock was removed: it faults on stock mono-nx.)
    private readonly Stopwatch Clock = Stopwatch.StartNew();

    // Stock mono-nx exports only SDL2/console through its __Internal dl_shim, NOT the libnx platform symbols
    // (romfs/applet/pad/hid) this backend calls. NativePlatform records whether they resolved at OpenWindow;
    // when false the backend runs HEADLESS (managed clock, no romfs, no input) so the managed game + 60 TPS sim
    // still run — the Phase-0 smoke/perf test. A mono-nx fork that registers those symbols flips this true.
    private bool NativePlatform;

    // Handheld default; docked is 1920x1080. Real value comes from the swapchain's images in Phase 3.
    private int Width_ = 1280, Height_ = 720;

    // =========================================================================================
    // IPlatform
    // =========================================================================================

    public void OpenWindow(int width, int height, string title)
    {
        // The libnx platform layer: romfs (asset backing), the applet-exit gate, and pad/touch input. On a
        // mono-nx fork that exports these via __Internal they succeed; on STOCK mono-nx the first call throws an
        // unresolved-symbol exception, and we drop to headless mode instead of terminating. Either way the
        // managed game continues — assets still load through the Assets seam (plain file IO, which works), the
        // clock is the managed Stopwatch, and input is simply absent.
        try
        {
            Nx.romfsInit();
            Nx.appletLockExit();
            Pad = Marshal.AllocHGlobal(256);          // PadState is < 256 bytes; sized generously
            Nx.padConfigureInput(1, /*STANDARD*/ 1);
            Nx.padInitializeDefault(Pad);
            Nx.hidInitializeTouchScreen();
            NativePlatform = true;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException or TypeLoadException)
        {
            NativePlatform = false;
            if (Pad != IntPtr.Zero) { Marshal.FreeHGlobal(Pad); Pad = IntPtr.Zero; }
        }

        // TODO(Phase 3): dkDeviceCreate → dkQueueCreate → build the swapchain on nwindowGetDefault(). Those are
        // ALSO __Internal symbols and need the fork's dl_shim to link deko3d; until then the draw path is no-op.
    }

    public void CloseWindow()
    {
        if (Swapchain != IntPtr.Zero) { Dk.dkSwapchainDestroy(Swapchain); Swapchain = IntPtr.Zero; }
        if (Queue != IntPtr.Zero)     { Dk.dkQueueDestroy(Queue);         Queue = IntPtr.Zero; }
        if (Device != IntPtr.Zero)    { Dk.dkDeviceDestroy(Device);       Device = IntPtr.Zero; }
        if (Pad != IntPtr.Zero)       { Marshal.FreeHGlobal(Pad);         Pad = IntPtr.Zero; }
        if (NativePlatform)
        {
            Nx.appletUnlockExit();
            Nx.romfsExit();
        }
    }

    /// <summary>The Switch has no window to close; the applet loop ending (HOME → Quit) is the signal. In
    /// headless mode (no libnx) there is no applet loop, so never auto-close — the emulator/HOME exits us.</summary>
    public bool ShouldClose => NativePlatform && !Nx.appletMainLoop();

    public void SetWindowIcon(string path) { /* no window icon on Switch */ }
    public void SetWindowSize(int width, int height) { /* fixed to the swapchain surface */ }
    public void ApplyWindowMode(WindowMode mode, int windowedWidth, int windowedHeight) { /* always fullscreen */ }
    public WindowMode CurrentWindowMode => WindowMode.Exclusive;   // the console surface is a fixed fullscreen

    public int WindowWidth => Width_;
    public int WindowHeight => Height_;
    public int MonitorWidth => Width_;
    public int MonitorHeight => Height_;

    public void SetVSync(bool enabled) { /* the swapchain presents on VSync by default */ }
    public void SetTargetFps(int fps) { /* frame pacing is the swapchain's job */ }
    public void DisableExitKey() { /* no exit key */ }

    public double Time => Clock.Elapsed.TotalSeconds;

    private int fps;
    private int frameCount;
    private double lastReport;
    public int Fps => fps;
    public void DrawFpsCounter(int x, int y) { /* TODO: draw once text works */ }

    // =========================================================================================
    // IInput  (Switch pad + touch; no keyboard/mouse)
    // =========================================================================================

    public bool IsKeyDown(KeyCode key) => false;                 // no keyboard
    public bool IsMouseDown(MouseBtn button) => false;           // no mouse
    public Vector2 MousePosition => Vector2.Zero;
    public Vector2 MouseDelta => Vector2.Zero;
    public float MouseWheel => 0f;

    public int GamepadCount => NativePlatform ? 1 : 0;           // the console's own controller (none if headless)
    public void RefreshGamepads() { if (NativePlatform) Nx.padUpdate(Pad); }

    public bool IsPadDown(PadButton button)
    {
        if (!NativePlatform) return false;
        ulong bit = NpadBit(button);
        return bit != 0 && (Nx.padGetButtons(Pad) & bit) != 0;
    }

    public float GetPadAxis(PadAxis axis)
    {
        if (!NativePlatform) return 0f;
        // One pad. Sticks come back already normalised to ±JoystickMax; the engine's axis convention matches
        // Raylib's, where the sticks rest at 0 and Y is positive DOWNWARD — so negate the Switch's up-positive Y.
        // The Switch has no analog triggers: ZL/ZR are digital, so report them Raylib-style (−1 released, +1 held).
        switch (axis)
        {
            case PadAxis.LeftX:  return  Nx.padGetStickPos(Pad, 0).X / HidAnalogStickState.JoystickMax;
            case PadAxis.LeftY:  return -Nx.padGetStickPos(Pad, 0).Y / HidAnalogStickState.JoystickMax;
            case PadAxis.RightX: return  Nx.padGetStickPos(Pad, 1).X / HidAnalogStickState.JoystickMax;
            case PadAxis.RightY: return -Nx.padGetStickPos(Pad, 1).Y / HidAnalogStickState.JoystickMax;
            case PadAxis.LeftTrigger:  return (Nx.padGetButtons(Pad) & HidNpadButton.ZL) != 0 ? 1f : -1f;
            case PadAxis.RightTrigger: return (Nx.padGetButtons(Pad) & HidNpadButton.ZR) != 0 ? 1f : -1f;
            default: return 0f;
        }
    }

    public PadButton? GetPressedPadButton()
    {
        if (!NativePlatform) return null;
        // For the rebinding screen: the first button that went down THIS frame. padGetButtonsDown is edge-triggered.
        ulong down = Nx.padGetButtonsDown(Pad);
        if (down == 0) return null;
        for (PadButton b = PadButton.LeftFaceUp; b <= PadButton.RightThumb; b++)
        {
            ulong bit = NpadBit(b);
            if (bit != 0 && (down & bit) != 0) return b;
        }
        return null;
    }

    /// <summary>
    /// Maps the engine's positional <see cref="PadButton"/> (numbered like Raylib's Xbox layout) to the libnx
    /// <see cref="HidNpadButton"/> bit for the same PHYSICAL slot on a Switch pad. This is deliberately positional,
    /// not by letter: the top-right face button is X on a Switch but Raylib calls that slot RightFaceUp, so
    /// RightFaceUp → X and RightFaceRight → A. config.json persists these as raw Raylib integers, so keeping the
    /// mapping positional means an existing binding lands on the same physical button. Returns 0 for buttons the
    /// pad's button bitmask has no equivalent for (Home is captured through a separate applet path, not here).
    /// </summary>
    private static ulong NpadBit(PadButton b) => b switch
    {
        PadButton.LeftFaceUp     => HidNpadButton.Up,
        PadButton.LeftFaceRight  => HidNpadButton.Right,
        PadButton.LeftFaceDown   => HidNpadButton.Down,
        PadButton.LeftFaceLeft   => HidNpadButton.Left,
        PadButton.RightFaceUp    => HidNpadButton.X,
        PadButton.RightFaceRight => HidNpadButton.A,
        PadButton.RightFaceDown  => HidNpadButton.B,
        PadButton.RightFaceLeft  => HidNpadButton.Y,
        PadButton.LeftTrigger1   => HidNpadButton.L,
        PadButton.LeftTrigger2   => HidNpadButton.ZL,
        PadButton.RightTrigger1  => HidNpadButton.R,
        PadButton.RightTrigger2  => HidNpadButton.ZR,
        PadButton.MiddleLeft     => HidNpadButton.Minus,
        PadButton.Middle         => 0,                 // Home is not in the button bitmask
        PadButton.MiddleRight    => HidNpadButton.Plus,
        PadButton.LeftThumb      => HidNpadButton.StickL,
        PadButton.RightThumb     => HidNpadButton.StickR,
        _ => 0,
    };

    public int TouchCount => 0;                                  // TODO: hidGetTouchScreenStates
    public Vector2 GetTouchPosition(int index) => Vector2.Zero;

    // =========================================================================================
    // IAudio  (libnx audrv — Phase 3)
    // =========================================================================================

    private float sfxVolume = 1f;
    public bool Initialize() => false;                           // silent until audrv is wired
    public bool IsAvailable => false;
    public SoundHandle LoadSound(string path) => SoundHandle.None;
    public void UnloadSound(SoundHandle sound) { }
    public void Play(SoundHandle sound) { }
    public float SfxVolume { get => sfxVolume; set => sfxVolume = value; }

    // =========================================================================================
    // IRenderer — resources
    // =========================================================================================

    public TextureHandle LoadTexture(string path) => TextureHandle.None;      // TODO: DkImage upload
    public TextureHandle LoadTextureFromPixels(byte[] rgba, int width, int height) => TextureHandle.None;  // ditto
    public void UnloadTexture(TextureHandle texture) { }
    public bool IsValid(TextureHandle texture) => texture.Id != 0;
    public Vector2 GetTextureSize(TextureHandle texture) => Vector2.Zero;     // TODO: from DkImage layout
    public void SetTextureFilter(TextureHandle texture, FilterMode filter) { }

    public TargetHandle CreateTarget(int width, int height) => TargetHandle.None;  // TODO: render-to-DkImage
    public void DestroyTarget(TargetHandle target) { }
    public bool IsValid(TargetHandle target) => target.Id != 0;
    public TextureHandle GetTargetTexture(TargetHandle target) => TextureHandle.None;

    public void BeginTarget(TargetHandle target) { /* TODO: bindRenderTargets, push target stack */ }
    public void EndTarget() { /* TODO: re-bind enclosing target (they NEST) down to TargetFloor */ }
    public int TargetFloor { get; set; }
    public void ResetTargets() { /* TODO: unwind the target stack */ }

    // Shaders are offline .dksh (UAM). LoadShaderFromSource cannot exist on Switch — gate its callers out.
    public ShaderHandle LoadShader(string? vertexPath, string fragmentPath) => ShaderHandle.None;  // TODO: load .dksh
    public ShaderHandle LoadShaderFromSource(string? vertexSource, string fragmentSource) =>
        throw new NotSupportedException("deko3d shaders are compiled offline with UAM; runtime source is unavailable on Switch.");
    public void UnloadShader(ShaderHandle shader) { }
    public bool IsValid(ShaderHandle shader) => shader.Id != 0;
    public void BeginShader(ShaderHandle shader) { /* TODO: dkCmdBufBindShaders */ }
    public void EndShader() { /* TODO: restore the default 2D pipeline */ }

    public int GetUniformLocation(ShaderHandle shader, string name) => -1;    // TODO: baked UBO offset table
    public void SetUniform<T>(ShaderHandle shader, int location, T value, UniformType type) where T : unmanaged { }
    public void SetUniformTexture(ShaderHandle shader, int location, TextureHandle texture) { }
    public void SetUniformArray(ShaderHandle shader, int location, float[] values, UniformType type) { }
    public void SetUniform<T>(ShaderHandle shader, string name, T value, UniformType type) where T : unmanaged { }
    public IReadOnlyList<string> GetUniformNames(ShaderHandle shader) => Array.Empty<string>();

    // =========================================================================================
    // IRenderer — fonts / text  (STB-rasterised into a DkImage atlas — Phase 3)
    // =========================================================================================

    public FontHandle LoadFont(string path, int size) => FontHandle.None;
    public void UnloadFont(FontHandle font) { }
    public FontHandle GetDefaultFont() => FontHandle.None;
    public Vector2 MeasureText(FontHandle font, string text, float fontSize, float spacing) => Vector2.Zero;
    public void DrawText(FontHandle font, string text, Vector2 position, float fontSize, float spacing, Rgba tint) { }
    public void DrawTextPro(FontHandle font, string text, Vector2 position, Vector2 origin, float rotation,
        float fontSize, float spacing, Rgba tint) { }

    // =========================================================================================
    // IRenderer — drawing  (all TODO: batch into a vertex ring + the default 2D pipeline)
    // =========================================================================================

    public void Clear(Rgba color) { /* TODO: dkCmdBufClearColorFloat on the bound target */ }
    public void DrawTexture(TextureHandle texture, Vector2 position, Rgba tint) { }
    public void DrawTexture(TextureHandle texture, Vector2 position, float rotation, float scale, Rgba tint) { }
    public void DrawTexture(TextureHandle texture, Rect source, Rect destination, Vector2 origin, float rotation, Rgba tint) { }
    public void DrawNinePatch(TextureHandle texture, NinePatch patch, Rect destination, Vector2 origin, float rotation, Rgba tint) { }
    public void DrawRect(Rect rect, Rgba color) { }
    public void DrawRect(Rect rect, Vector2 origin, float rotation, Rgba color) { }
    public void DrawLine(Vector2 from, Vector2 to, Rgba color) { }

    public void BeginBlend(BlendMode mode) { /* TODO: dkCmdBufBindBlendStates */ }
    public void EndBlend() { /* TODO: restore default (premultiplied alpha) */ }

    // =========================================================================================
    // IRenderer — frame
    // =========================================================================================

    public void BeginFrame()
    {
        // TODO(Phase 3): acquire the next swapchain image, reset the frame command buffer, bind it as target.
        //   int slot = Dk.dkQueueAcquireImage(Queue, Swapchain);
        //   ... record clear + bind render targets ...
    }

    public void EndFrame()
    {
        // Headless heartbeat: once warmed up the runtime log goes silent, so an idle loop and a hang look the
        // same. Print the loop rate once per second — proof-of-life AND the first real perf datapoint (how fast
        // the interpreter pumps the frame loop). This is the only visibility we have until there's a draw path.
        frameCount++;
        double now = Clock.Elapsed.TotalSeconds;
        if (now - lastReport >= 1.0)
        {
            fps = (int)(frameCount / (now - lastReport));
            Console.WriteLine($"[switch] loop alive: {fps} fps");
            frameCount = 0;
            lastReport = now;
        }

        // TODO(Phase 3): finish the command list, submit, present.
        //   ulong list = Dk.dkCmdBufFinishList(cmdbuf);
        //   Dk.dkQueueSubmitCommands(Queue, list);
        //   Dk.dkQueuePresentImage(Queue, Swapchain, slot);
    }

    // =========================================================================================
    // IBackend — debug UI (never on Switch: the ImGui editors are DEBUG desktop tooling)
    // =========================================================================================

    public bool SupportsDebugUi => false;
    public void SetupDebugUi() { }
    public void BeginDebugUi() { }
    public void EndDebugUi() { }
    public void DebugUiImage(TextureHandle texture) { }
    public void DebugUiImage(TargetHandle target) { }

    public void Dispose() => CloseWindow();
}
#endif

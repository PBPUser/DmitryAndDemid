using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

public class TiledLoadingScreen : Screen
{
    public double LoadingTime { get; set; }
    public double Fade { get; set; }
    public Action? Event; 
    private bool EventExecuted = false;
    bool FadeOut = false;
    private TextureHandle FifoLoading;
    private Rect FifoSource, FifoTarget, LoadingSource, LoadingTarget, LoadingBufferSource;
    Vector2 FifoOrigin;
    private double FifoLoadingShowDelay;
    private const double FifoLoadingAppearing = 0.25;
    private TextureHandle
        LoadingTexture = Runtime.CurrentRuntime.Textures["loading.png"];
    private TargetHandle LoadingBuffer;
    public ShaderHandle LoadingShaderSwap;
    public ShaderHandle LoadingShaderTiles;

    /// <summary>A snapshot of the screen the loader was opened from, used to wipe IN over it (see CaptureFrom).</summary>
    private TargetHandle SwapFrom;
    private Rect SwapFromSource;
    private bool HasSwapFrom;
    private Screen? CaptureScreen;
    private bool CaptureDone;
    static TextureHandle ForkTexture = Runtime.CurrentRuntime.Textures["vilkaCut.png"];
    static Vector2 ForkSize;

    static TiledLoadingScreen()
    {
        ForkSize = new Vector2(ForkTexture.Width, ForkTexture.Height);
    }
    
#if SWITCH
    private static int _tlsTrace;
    private static void T(string s) { if (_tlsTrace < 3) Runtime.SwTrace("[tls] " + s); }
#elif ANDROID
    private static int _tlsTrace;
    private static void T(string s) { if (_tlsTrace < 3) Utils.Platform.Trace("[tls] " + s); }
#else
    private static void T(string s) { }
#endif

    public TiledLoadingScreen(double loadingTime, double fade, Action @event, bool fadeOut, double fifoLoadingShowDelay)
    {
        T("ctor start");
        LoadingShaderTiles = Runtime.CurrentRuntime.Shaders["loading"];
        LoadingShaderSwap = Runtime.CurrentRuntime.Shaders["loading_swap"];
        LoadingBuffer = LoadRenderTexture(Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height);
        FifoLoadingShowDelay = fifoLoadingShowDelay;
        LoadingTarget = Helper.GetFullscreenSource();
        LoadingSource = Helper.GetFullSource(LoadingTexture);
        LoadingBufferSource = Helper.GetFullSourceRenderTexture(LoadingBuffer);
        LoadingTime = loadingTime;
        Fade = fade;
        Event = @event;
        FadeOut = fadeOut;
        FifoLoading = Runtime.CurrentRuntime.Textures["fifo_loading.png"];
        FifoSource = Helper.GetFullSource(FifoLoading);
        FifoTarget = Helper.Scale(new Rect(64, 414,52, 97), Runtime.CurrentRuntime.ScaleF);
        FifoOrigin = FifoTarget.Size / 2;
        T("ctor done");
    }

    /// <summary>
    /// Grabs a still of <paramref name="screen"/> into a texture so the loader can wipe IN over it — a real swap
    /// from the menu that opened it, instead of appearing already fully covering the (not-yet-ready) gameplay.
    /// Call right after constructing the loader, before the frame it first renders.
    /// </summary>
    public void CaptureFrom(Screen screen)
    {
        // Only arm it here; the actual grab happens on the first Render() (below). This method is called from the
        // menu's input handler, which runs OUTSIDE BeginDrawing — drawing there renders garbage. Render() runs
        // inside the frame, so the grab is clean.
        CaptureScreen = screen;
        HasSwapFrom = true;
    }

    public override void TopUpdate()
    {
        base.TopUpdate();
        if (EventExecuted)
            return;
        if (GetTime() - TimeAppear < LoadingTime)
            return;
        Event?.Invoke();
        EventExecuted = true;
    }

    public override void Render()
    {
        // First frame: grab the screen we came from into a texture, now that we're inside a draw frame.
        if (HasSwapFrom && !CaptureDone)
        {
            SwapFrom = LoadRenderTexture(Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height);
            BeginTextureMode(SwapFrom);
            ClearBackground(new Rgba(0, 0, 0, 255));
            CaptureScreen?.Render();
            EndTextureMode();
            SwapFromSource = Helper.GetFullSourceRenderTexture(SwapFrom);
            CaptureDone = true;
        }

        float time = (float)GetTime();
        // Cover fully from the very first frame instead of wiping in from transparent. The screen underneath
        // (the gameplay being loaded) is not ready yet, so a swap-in wipe would briefly expose it — the flash
        // this transition is meant to hide. Hold covered, then only wipe OUT at the end to reveal the loaded
        // gameplay.
        // fade drives the swap shader: 0 = uncovered, ~1 = fully covered, 1..2 = wiping back out. When we have a
        // snapshot of the screen we came from, wipe IN over it during the first Fade seconds (a real swap from the
        // menu) instead of appearing already-covered; then hold, then wipe out to reveal the loaded gameplay.
        double since = time - TimeAppear, end = TimeAppear + LoadingTime;
        float fade;
        if (HasSwapFrom && since < Fade)
            fade = (float)(since / Fade);                          // swap IN over the previous screen
        else if (time > end - Fade)
            fade = (float)(1 + (time - (end - Fade)) / Fade);      // swap OUT to reveal the gameplay
        else
            fade = .99f;                                          // held fully covered while it loads
        T("render start");
        BeginTextureMode(LoadingBuffer);
        SetShaderValue(LoadingShaderTiles, GetShaderLocation(LoadingShaderTiles,"time"), time, UniformType.Float);
        SetShaderValue(LoadingShaderTiles, GetShaderLocation(LoadingShaderTiles,"outputRes"), LoadingTarget.Size, UniformType.Vec2);
        SetShaderValue(LoadingShaderTiles, GetShaderLocation(LoadingShaderTiles,"textureRes"), LoadingSource.Size * 4, UniformType.Vec2);
        T("tiles uniforms set");
        BeginShaderMode(LoadingShaderTiles);
        DrawTexturePro(LoadingTexture, LoadingSource, LoadingTarget, Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        EndShaderMode();
        T("tiles pass done");
        // During the swap-in, paint the snapshot of the screen we came from underneath the wiping tiles, so the
        // not-yet-ready gameplay (which sits below this screen in the stack) never shows through the uncovered
        // areas — the tiles appear to sweep in over the menu.
        if (CaptureDone && since < Fade)
            DrawTexturePro(SwapFrom.Texture, SwapFromSource, LoadingTarget, Vector2.Zero, 0, Rgba.White);
        SetShaderValue(LoadingShaderSwap, GetShaderLocation(LoadingShaderSwap,"time"), fade, UniformType.Float);
        BeginShaderMode(LoadingShaderSwap);
        DrawTexturePro(LoadingBuffer.Texture, LoadingBufferSource, LoadingTarget, Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        T("swap pass done");
        DrawTexturePro(FifoLoading, FifoSource, FifoTarget, FifoOrigin,
            time * 1000f,
            Rgba.White
                with { A = 255 });
        base.Render();
        T("render done");
#if SWITCH || ANDROID
        _tlsTrace++;
#endif
    }

    public override void Unload()
    {
        UnloadRenderTexture(LoadingBuffer);
        if (CaptureDone)
            UnloadRenderTexture(SwapFrom);
    }
}
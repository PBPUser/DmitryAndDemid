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
    static TextureHandle ForkTexture = Runtime.CurrentRuntime.Textures["vilkaCut.png"];
    static Vector2 ForkSize;

    static TiledLoadingScreen()
    {
        ForkSize = new Vector2(ForkTexture.Width, ForkTexture.Height);
    }
    
    public TiledLoadingScreen(double loadingTime, double fade, Action @event, bool fadeOut, double fifoLoadingShowDelay)
    {
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
        float time = (float)GetTime();
        // Cover fully from the very first frame instead of wiping in from transparent. The screen underneath
        // (the gameplay being loaded) is not ready yet, so a swap-in wipe would briefly expose it — the flash
        // this transition is meant to hide. Hold covered, then only wipe OUT at the end to reveal the loaded
        // gameplay.
        float fade = (float)(time > TimeAppear + LoadingTime - Fade ? 1 - (TimeAppear + LoadingTime - Fade - time)/Fade : .99f);
        BeginTextureMode(LoadingBuffer);
        SetShaderValue(LoadingShaderTiles, GetShaderLocation(LoadingShaderTiles,"time"), time, UniformType.Float);
        SetShaderValue(LoadingShaderTiles, GetShaderLocation(LoadingShaderTiles,"outputRes"), LoadingTarget.Size, UniformType.Vec2);
        SetShaderValue(LoadingShaderTiles, GetShaderLocation(LoadingShaderTiles,"textureRes"), LoadingSource.Size * 4, UniformType.Vec2);
        BeginShaderMode(LoadingShaderTiles);
        DrawTexturePro(LoadingTexture, LoadingSource, LoadingTarget, Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        EndShaderMode();
        SetShaderValue(LoadingShaderSwap, GetShaderLocation(LoadingShaderSwap,"time"), fade, UniformType.Float);
        BeginShaderMode(LoadingShaderSwap);
        DrawTexturePro(LoadingBuffer.Texture, LoadingBufferSource, LoadingTarget, Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        DrawTexturePro(FifoLoading, FifoSource, FifoTarget, FifoOrigin,
            time * 1000f,
            Rgba.White 
                with { A = 255 });
        base.Render();
    }

    public override void Unload()
    {
        UnloadRenderTexture(LoadingBuffer);
    }
}
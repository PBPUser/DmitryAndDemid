using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

public class BlackLoadingScreen : Screen
{
    public double LoadingTime { get; set; }
    public double Fade { get; set; }
    public Action? Event;
    private bool EventExecuted = false;
    bool FadeOut = false;
    private Rect FifoTarget;
    Vector2 FifoOrigin;
    private double FifoLoadingShowDelay;
    private const double FifoLoadingAppearing = 0.25;

    public BlackLoadingScreen(double loadingTime, double fade, Action @event, bool fadeOut, double fifoLoadingShowDelay)
    {
        FifoLoadingShowDelay = fifoLoadingShowDelay;
        LoadingTime = loadingTime;
        Event = @event;
        Fade = fade;
        FadeOut = fadeOut;
        FifoTarget = Helper.Scale(new(64, 414,52, 97), Runtime.CurrentRuntime.ScaleF);
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
        byte transparency2 = Helper.TimeToTransparency(FadeOut ?
            Helper.ComputeObjectTime(time, TimeAppear+FifoLoadingShowDelay, FifoLoadingAppearing, LoadingTime+FifoLoadingAppearing, FifoLoadingAppearing) :
            Helper.ComputeObjectTimeStart(time, TimeAppear+FifoLoadingShowDelay, FifoLoadingAppearing)
        );
        byte transparency = Helper.TimeToTransparency(FadeOut ?
            Helper.ComputeObjectTime(time, TimeAppear, Fade, LoadingTime+Fade, Fade) :
            Helper.ComputeObjectTimeStart(time, TimeAppear, Fade)
        );
        DrawRectangle(0,0,Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height,
            Rgba.Black with { A = transparency } );
        // Fetched per frame, not cached in the constructor: the demo start/end paths unload and reload the
        // whole texture set right after constructing this screen, which freed the cached handle mid-fade.
        BasicTexture fifo = Runtime.CurrentRuntime.Textures["fifo_loading.png"];
        DrawTexturePro(fifo, Helper.GetFullSource(fifo), FifoTarget, FifoOrigin,
            time * 1000f,
            Rgba.White
                with { A = transparency2 });
        base.Render();
    }
}
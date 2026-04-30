using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Screens;

public class BlackLoadingScreen : Screen
{
    public double LoadingTime { get; set; }
    public double Fade { get; set; }
    public Action? Event;
    private bool EventExecuted = false;
    bool FadeOut = false;
    private Texture2D FifoLoading;
    private Rectangle FifoSource, FifoTarget;
    Vector2 FifoOrigin;
    private double FifoLoadingShowDelay;
    private const double FifoLoadingAppearing = 0.25;
    
    public BlackLoadingScreen(double loadingTime, double fade, Action @event, bool fadeOut, double fifoLoadingShowDelay)
    {
        FifoLoadingShowDelay = fifoLoadingShowDelay;
        LoadingTime = loadingTime;
        Fade = fade;
        Event = @event;
        FadeOut = fadeOut;
        FifoLoading = Runtime.CurrentRuntime.Textures["fifo_loading.png"];
        FifoSource = Helper.GetFullSource(FifoLoading);
        FifoTarget = Helper.Scale(new Rectangle(64, 414,52, 97), Runtime.CurrentRuntime.ScaleF);
        FifoOrigin = FifoTarget.Size / 2;
    }

    public override void TopUpdate()
    {
        base.TopUpdate();
        if (EventExecuted)
            return;
        if (GetTime() - LoadingTime < LoadingTime)
            return;
        Event?.Invoke();
        EventExecuted = true;
    }

    public override void Render()
    {
        float time = (float)GetTime();
        byte transparency = Helper.TimeToTransparency(FadeOut ?
            Helper.ComputeObjectTime(time, TimeAppear, Fade, LoadingTime+Fade, Fade) :
            Helper.ComputeObjectTimeStart(time, TimeAppear, Fade)
        );
        byte transparency2 = Helper.TimeToTransparency(FadeOut ?
            Helper.ComputeObjectTime(time, TimeAppear+FifoLoadingShowDelay, FifoLoadingAppearing, LoadingTime+FifoLoadingAppearing, FifoLoadingAppearing) :
            Helper.ComputeObjectTimeStart(time, TimeAppear+FifoLoadingShowDelay, FifoLoadingAppearing)
        );
        DrawRectangle(0,0,Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height,
            Color.Black with { A = transparency } );
        DrawTexturePro(FifoLoading, FifoSource, FifoTarget, FifoOrigin,
            time * 1000f,
            Color.White 
                with { A = transparency2 });
        
        base.Render();
    }
}
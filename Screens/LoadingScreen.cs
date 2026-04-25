using DmitryAndDemid.Common;
using static Raylib_cs.Raylib;
using static DmitryAndDemid.Runtime;
using Raylib_cs;
using System.Numerics;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

public class LoadingScreen : Screen
{
    public LoadingScreen()
    {
        Padding = (int)(16 * CurrentRuntime.Scale);
        SugarTexture = LoadTexture("Assets/Textures/sugar_logo.png");
        ADPTexture = LoadTexture("Assets/Textures/anti_dolboeb_protect.png");
        FifoLoading = LoadTexture("Assets/Textures/fifo_loading.png");
        int size = (int)(100 * CurrentRuntime.Scale);
        SugarSource = new Rectangle(0, 0, 400, 400);
        SugarTarget = new Rectangle((CurrentRuntime.Width - size) / 2, (CurrentRuntime.Height - size) / 2, size, size);
        int width = (int)(ADPTexture.Width * CurrentRuntime.Scale),
            height = (int)(ADPTexture.Height * CurrentRuntime.Scale);
        int width2 = (int)(ADPTexture.Width * CurrentRuntime.Scale) / 2,
            height2 = (int)(ADPTexture.Height * CurrentRuntime.Scale) / 2;
        ADPSource = new Rectangle(0, 0, ADPTexture.Width, ADPTexture.Height);
        ADPTarget = new Rectangle((CurrentRuntime.Width - width) / 2, (CurrentRuntime.Height - height) / 2, width, height);
        FifoSource = Helper.GetFullSource(FifoLoading);
        FifoTarget = Helper.Scale(new Rectangle(64, 414,52, 97), Runtime.CurrentRuntime.Scale);
        ADPTargetActive = new Rectangle((CurrentRuntime.Width - width2) - Padding, (CurrentRuntime.Height - height2) - Padding, width2, height2);
        TextSize = (int)(16 * CurrentRuntime.Scale);
        FifoOrigin = FifoTarget.Size / 2;
    }

    private Vector2 FifoOrigin;
    
    Texture2D SugarTexture, ADPTexture, FifoLoading;

    Rectangle
        SugarTarget, SugarSource, ADPTarget, ADPTargetActive, ADPSource, FifoSource, FifoTarget;


    int TextSize, Padding;
    double Time = 0;
    string ADPText = "";
    bool ADPActive = false;
    bool PlayMusic = false;

    public override void PreRender(double delta)
    {
        Time += delta;
    }

    public override void Render()
    {
        float time = (float)GetTime();
        DrawTexturePro(SugarTexture, SugarSource, SugarTarget, Vector2.Zero, 0f, Color.White with { A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(), 0, 0.25, 1.5, 0.25)) });
        DrawTexturePro(ADPTexture, ADPSource, Helper.Mix(ADPTarget, ADPTargetActive, Helper.EaseInOutElasticF((float)Helper.ComputeObjectTime(GetTime(), 4, 1, 9999999, .25))), Vector2.Zero, (float)(Helper.ComputeObjectTime(GetTime(), 4, .125, 4.25, .125) * MathF.Sin((float)GetTime())),
            Color.White with { A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(), 1.5, 0.5, ADPActive ? 9999999999 : 3, 0.5)) });
        DrawTexturePro(FifoLoading, FifoSource, FifoTarget, FifoOrigin,
            time * 1000f,
            Color.White 
                with { A = Helper.TimeToTransparency(
                    Helper.ComputeObjectTime(GetTime(), 0, 0.5, 3,
                        0.5)) });
        DrawText(ADPText, 0, 0, TextSize, 
            Color.White with { A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(),
                5, 0.25, 99999, 0.5)) });
    }

    public void SetADPText(string? text, bool music)
    {
        PlayMusic = music;
        ADPActive = true;
        ADPText = text;
    }
}

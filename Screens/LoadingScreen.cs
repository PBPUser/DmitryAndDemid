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
        RaylibTexture = LoadTexture("Assets/Textures/raylib.png");
        RaylibBasicTexture = LoadTexture("Assets/Textures/raylib_basic_libs.png");
        RaylibExtraTexture = LoadTexture("Assets/Textures/raylib_extra_libs.png");
        RaylibCsTexture = LoadTexture("Assets/Textures/raylib_cs.png");
        HuffTexture = LoadTexture("Assets/Textures/huffbuzz.png");
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
        RaylibSource = Helper.GetFullSource(RaylibTexture);
        Vector2 res = new Vector2(640, 480);
        RaylibTarget = new Rectangle((res - RaylibSource.Size) / 2 * CurrentRuntime.ScaleF,
            RaylibSource.Size * CurrentRuntime.ScaleF);
        RaylibBasicSource = new Rectangle(0, 0, 94, 88);
        RaylibBasicTarget=new Rectangle((res-RaylibBasicSource.Size) / 2 * CurrentRuntime.ScaleF, new Vector2(94, 94) * CurrentRuntime.ScaleF);
        RaylibExtraSource = new Rectangle(0, 0, 128, 128);
        RaylibExtraTarget = new Rectangle((res-new Vector2(128))/2*CurrentRuntime.ScaleF, new Vector2(128, 128) * CurrentRuntime.ScaleF);
        RaylibCsSource = Helper.GetFullSource(RaylibCsTexture);
        RaylibCsTarget=new Rectangle((res-RaylibCsSource.Size) / 2 * CurrentRuntime.ScaleF, RaylibCsSource.Size * CurrentRuntime.ScaleF);
        HuffSource = Helper.GetFullSource(HuffTexture);
        HuffTarget = new Rectangle((res-(HuffSource.Size/4)) / 2 * CurrentRuntime.ScaleF, HuffSource.Size / 4 * CurrentRuntime.ScaleF);
    }

    private Vector2 FifoOrigin;
    Texture2D SugarTexture, ADPTexture, FifoLoading, RaylibTexture, RaylibBasicTexture, RaylibExtraTexture, RaylibCsTexture, HuffTexture;
    Rectangle
        SugarTarget, SugarSource, ADPTarget, ADPTargetActive, ADPSource, FifoSource, FifoTarget;

    Rectangle RaylibSource, RaylibTarget;
    Rectangle RaylibBasicSource, RaylibBasicTarget;
    Rectangle RaylibExtraSource, RaylibExtraTarget;
    Rectangle RaylibCsSource, RaylibCsTarget;
    Rectangle HuffSource, HuffTarget;
    

    int TextSize, Padding;
    double Time = 0;
    string ADPText = "";
    bool ADPActive = false;
    bool PlayMusic = false;

    public override void PreRender(double delta)
    {
        Time += delta;
    }

    #if DEBUG
    private const double LoadingTime = 0.5;
    const double FastLoadingTime = .5;
    #else
    private const double LoadingTime = 33;
    const double FastLoadingTime = 3;
    #endif
    
    public override void Render()
    {
        float time = (float)GetTime();
        DrawTexturePro(SugarTexture, SugarSource, SugarTarget, Vector2.Zero, 0f, Color.White with { A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(), 0, 0.25, 1.5, 0.25)) });
        DrawTexturePro(ADPTexture, ADPSource, Helper.Mix(ADPTarget, ADPTargetActive, Helper.EaseInOutElasticF((float)Helper.ComputeObjectTime(GetTime(), ADPActive ? 4 : 999999999, 1, 9999999, .25))), Vector2.Zero, (float)(Helper.ComputeObjectTime(GetTime(), 4, .125, 4.25, .125) * MathF.Sin((float)GetTime())),
            Color.White with { A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(), 1.5, 0.5, ADPActive ? 9999999999 : 3, 0.5)) });
        DrawTexturePro(FifoLoading, FifoSource, FifoTarget, FifoOrigin,
            time * 1000f,
            Color.White 
                with { A = Helper.TimeToTransparency(
                    Helper.ComputeObjectTime(GetTime(), 0, 0.5, ADPActive ? 3.0 : Configuration.Config.FastLoading ? FastLoadingTime : LoadingTime,
                        0.5)) });
        DrawText(ADPText, 0, 0, TextSize, 
            Color.White with { A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(),
                5, 0.25, 99999, 0.5)) });
        if (ADPActive || Configuration.Config.FastLoading)
            return;
        float j = (int)(time / 1.5f)-5;
        DrawTexturePro(RaylibTexture, RaylibSource, RaylibTarget, Vector2.Zero,
            0f,
            Color.White 
                with { A = Helper.TimeToTransparency(
                    Helper.ComputeObjectTime(GetTime(), 3.0, 0.5, 4.5,
                        0.5)) });
        DrawTexturePro(RaylibCsTexture, RaylibCsSource, RaylibCsTarget, Vector2.Zero,
            0f,
            Color.White 
                with { A = Helper.TimeToTransparency(
                    Helper.ComputeObjectTime(GetTime(), 4.5, 0.5, 6,
                        0.5)) });
        DrawTexturePro(HuffTexture, HuffSource, HuffTarget, Vector2.Zero,
            0f,
            Color.White 
                with { A = Helper.TimeToTransparency(
                    Helper.ComputeObjectTime(GetTime(), 6.0, 0.5, 7.5,
                        0.5)) });
        if (j < 0)
            return;
        if (j < 7)
            DrawTexturePro(RaylibBasicTexture, RaylibBasicSource with { X = j * 102 }, RaylibBasicTarget, Vector2.Zero, 0f, 
                Color.White with {A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(), 7.5+(j*1.5), 0.5, 9+(j*1.5), 0.5)) }
            );
        else if (j < 17)
            DrawTexturePro(RaylibExtraTexture, RaylibExtraSource with { X = (j - 7) * 134, Y = (int)((j - 7) / 5) * 133 }, RaylibExtraTarget, Vector2.Zero, 0f, 
                Color.White with {A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(), 7.5+(j*1.5), 0.5, 9+(j*1.5), 0.5)) }
            );
    }

    public void SetADPText(string? text, bool music)
    {
        PlayMusic = music;
        ADPActive = true;
        ADPText = text;
    }
}

using DmitryAndDemid.Rendering;
using DmitryAndDemid.Common;
using static DmitryAndDemid.Rendering.Gfx;
using static DmitryAndDemid.Runtime;
using System.Numerics;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

public class LoadingScreen : Screen
{
    public LoadingScreen()
    {
        Padding = (int)(16 * CurrentRuntime.Scale);
        SugarTexture = LoadTexture(Assets.Resolve("Assets/Textures/sugar_logo.png"));
        ADPTexture = LoadTexture(Assets.Resolve("Assets/Textures/anti_dolboeb_protect.png"));
        FifoLoading = LoadTexture(Assets.Resolve("Assets/Textures/fifo_loading.png"));
        RaylibTexture = LoadTexture(Assets.Resolve("Assets/Textures/raylib.png"));
        RaylibBasicTexture = LoadTexture(Assets.Resolve("Assets/Textures/raylib_basic_libs.png"));
        RaylibExtraTexture = LoadTexture(Assets.Resolve("Assets/Textures/raylib_extra_libs.png"));
        RaylibCsTexture = LoadTexture(Assets.Resolve("Assets/Textures/raylib_cs.png"));
        HuffTexture = LoadTexture(Assets.Resolve("Assets/Textures/huffbuzz.png"));
        int size = (int)(100 * CurrentRuntime.Scale);
        SugarSource = new Rect(0, 0, 400, 400);
        SugarTarget = new Rect((CurrentRuntime.Width - size) / 2, (CurrentRuntime.Height - size) / 2, size, size);
        int width = (int)(ADPTexture.Width * CurrentRuntime.Scale),
            height = (int)(ADPTexture.Height * CurrentRuntime.Scale);
        int width2 = (int)(ADPTexture.Width * CurrentRuntime.Scale) / 2,
            height2 = (int)(ADPTexture.Height * CurrentRuntime.Scale) / 2;
        ADPSource = new Rect(0, 0, ADPTexture.Width, ADPTexture.Height);
        ADPTarget = new Rect((CurrentRuntime.Width - width) / 2, (CurrentRuntime.Height - height) / 2, width, height);
        FifoSource = Helper.GetFullSource(FifoLoading);
        FifoTarget = Helper.Scale(new Rect(64, 414, 52, 97), Runtime.CurrentRuntime.Scale);
        ADPTargetActive = new Rect((CurrentRuntime.Width - width2) - Padding, (CurrentRuntime.Height - height2) - Padding, width2, height2);
        TextSize = (int)(16 * CurrentRuntime.Scale);
        FifoOrigin = FifoTarget.Size / 2;
        RaylibSource = Helper.GetFullSource(RaylibTexture);
        Vector2 res = new Vector2(640, 480);
        RaylibTarget = new Rect((res - RaylibSource.Size) / 2 * CurrentRuntime.ScaleF, RaylibSource.Size * CurrentRuntime.ScaleF);
        RaylibBasicSource = new Rect(0, 0, 94, 88);
        RaylibBasicTarget = new Rect((res - RaylibBasicSource.Size) / 2 * CurrentRuntime.ScaleF, new Vector2(94, 94) * CurrentRuntime.ScaleF);
        RaylibExtraSource = new Rect(0, 0, 128, 128);
        RaylibExtraTarget = new Rect((res - new Vector2(128)) / 2 * CurrentRuntime.ScaleF, new Vector2(128, 128) * CurrentRuntime.ScaleF);
        RaylibCsSource = Helper.GetFullSource(RaylibCsTexture);
        RaylibCsTarget = new Rect((res - RaylibCsSource.Size) / 2 * CurrentRuntime.ScaleF, RaylibCsSource.Size * CurrentRuntime.ScaleF);
        HuffSource = Helper.GetFullSource(HuffTexture);
        HuffTarget = new Rect((res - (HuffSource.Size / 4)) / 2 * CurrentRuntime.ScaleF, HuffSource.Size / 4 * CurrentRuntime.ScaleF);

        // The DLSS NR badge sits along the bottom edge, centred, for the whole load — a "running on" mark, not a
        // slot in the credits sequence, so it shows with fast loading too.
        if (CurrentRuntime.ActiveUpscaler == Rendering.Upscaling.UpscalerKind.DlssNeural)
        {
            NrLogo = LoadTexture(Assets.Resolve("Assets/Textures/dlss_nr_logo.png"));
            NrSource = Helper.GetFullSource(NrLogo.Value);
            // Bottom-left, to the right of the loading spinner: the centre belongs to the credit cards and the
            // bottom-right to the error card when it lands there.
            Vector2 badge = new(180, 180 * NrSource.Height / MathF.Max(1, NrSource.Width));
            NrTarget = new Rect(128 * CurrentRuntime.ScaleF, (res.Y - badge.Y - 12) * CurrentRuntime.ScaleF,
                badge.X * CurrentRuntime.ScaleF, badge.Y * CurrentRuntime.ScaleF);
        }
    }

    private Vector2 FifoOrigin;

    /// <summary>The DLSS 5 Neural Rendering badge, shown through the whole load when that upscaler is the
    /// active one (Runtime.ActiveUpscaler — i.e. chosen AND available here). Null otherwise; the texture is in
    /// its own load group so the startup scan never pays for it.</summary>
    private readonly BasicTexture? NrLogo;
    private Rect NrSource, NrTarget;
    BasicTexture SugarTexture, ADPTexture, FifoLoading, RaylibTexture, RaylibBasicTexture, RaylibExtraTexture, RaylibCsTexture, HuffTexture;
    Rect
        SugarTarget, SugarSource, ADPTarget, ADPTargetActive, ADPSource, FifoSource, FifoTarget;

    Rect RaylibSource, RaylibTarget;
    Rect RaylibBasicSource, RaylibBasicTarget;
    Rect RaylibExtraSource, RaylibExtraTarget;
    Rect RaylibCsSource, RaylibCsTarget;
    Rect HuffSource, HuffTarget;


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
        SugarSource.Y = (int)(MathF.Sin(time * 2) * 10 + 10);
        DrawTexturePro(SugarTexture, SugarSource, SugarTarget, Vector2.Zero, 0f, Rgba.White with { A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(), 0, 0.25, 1.5, 0.25)) });
        if (NrLogo is { } nr)
        {
            // Fades in behind the sugar logo and stays; a slow pulse so it reads as live rather than printed.
            float pulse = 0.85f + 0.15f * MathF.Sin(time * 2.2f);
            DrawTexturePro(nr, NrSource, NrTarget, Vector2.Zero, 0f,
                Rgba.White with { A = (byte)(Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(), 0.5, 0.75, 99999, 0.5)) * pulse) });
        }
        // Build number under the sugar logo — fades in and stays, so it's always clear WHICH build is running
        // (catches a stale aag2.dll on the SD vs the one just deployed). Auto-incremented every build (see csproj).
        DrawTexturePro(ADPTexture, ADPSource, Helper.Mix(ADPTarget, ADPTargetActive, Helper.EaseInOutElasticF((float)Helper.ComputeObjectTime(GetTime(), ADPActive ? 4 : 999999999, 1, 9999999, .25))), Vector2.Zero, (float)(Helper.ComputeObjectTime(GetTime(), 4, .125, 4.25, .125) * MathF.Sin((float)GetTime())),
            Rgba.White with { A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(), 1.5, 0.5, ADPActive ? 9999999999 : 3, 0.5)) });
        DrawTexturePro(FifoLoading, FifoSource, FifoTarget, FifoOrigin,
            time * 1000f,
            Rgba.White
                with
            {
                A = Helper.TimeToTransparency(
                    Helper.ComputeObjectTime(GetTime(), 0, 0.5, ADPActive ? 3.0 : Configuration.Config.FastLoading ? FastLoadingTime : LoadingTime,
                        0.5))
            });
        DrawText(ADPText, 0, 0, TextSize,
            Rgba.White with
            {
                A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(),
                5, 0.25, 99999, 0.5))
            });
        if (ADPActive || Configuration.Config.FastLoading)
            return;
        float j = (int)(time / 1.5f) - 5;
        DrawTexturePro(RaylibTexture, RaylibSource, RaylibTarget, Vector2.Zero,
            0f,
            Rgba.White
                with
            {
                A = Helper.TimeToTransparency(
                    Helper.ComputeObjectTime(GetTime(), 3.0, 0.5, 4.5,
                        0.5))
            });
        DrawTexturePro(RaylibCsTexture, RaylibCsSource, RaylibCsTarget, Vector2.Zero,
            0f,
            Rgba.White
                with
            {
                A = Helper.TimeToTransparency(
                    Helper.ComputeObjectTime(GetTime(), 4.5, 0.5, 6,
                        0.5))
            });
        DrawTexturePro(HuffTexture, HuffSource, HuffTarget, Vector2.Zero,
            0f,
            Rgba.White
                with
            {
                A = Helper.TimeToTransparency(
                    Helper.ComputeObjectTime(GetTime(), 6.0, 0.5, 7.5,
                        0.5))
            });
        if (j < 0)
            return;
        if (j < 7)
            DrawTexturePro(RaylibBasicTexture, RaylibBasicSource with { X = j * 102 }, RaylibBasicTarget, Vector2.Zero, 0f,
                Rgba.White with { A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(), 7.5 + (j * 1.5), 0.5, 9 + (j * 1.5), 0.5)) }
            );
        else if (j < 17)
            DrawTexturePro(RaylibExtraTexture, RaylibExtraSource with { X = (j - 7) * 134, Y = (int)((j - 7) / 5) * 133 }, RaylibExtraTarget, Vector2.Zero, 0f,
                Rgba.White with { A = Helper.TimeToTransparency(Helper.ComputeObjectTime(GetTime(), 7.5 + (j * 1.5), 0.5, 9 + (j * 1.5), 0.5)) }
            );
    }

    public override void TopUpdate()
    {
        // Escape hatch off a stuck error (ADP) screen — e.g. a subsystem that failed to initialise leaves the
        // loader with no way forward. Plus / Start (or Enter on a keyboard) quits the game rather than stranding
        // the player. Only active while an error is actually being shown.
        if (ADPActive && (IsGamepadButtonDown(0, PadButton.MiddleRight) || IsKeyDown(KeyCode.Enter)))
            Environment.Exit(0);
    }

    public void SetADPText(string? text, bool music)
    {
        PlayMusic = music;
        ADPActive = true;
        ADPText = text;
    }
}

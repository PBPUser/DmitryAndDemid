using DmitryAndDemid.Common;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using System.Numerics;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

/// <summary>
/// Runs the sim-throughput benchmark (<see cref="Benchmark"/>) and hands the result to <see cref="StatisticsScreen"/>.
/// The run is a tight, blocking loop — the game freezes for a few seconds by design, since frame overhead would
/// pollute the measurement — so a "running" frame is painted first and the bench executes a couple of updates
/// later. Reached from the settings menu.
/// </summary>
public class BenchmarkScreen : Screen
{
    private readonly FontHandle font;
    private int warmupFrames;

    public BenchmarkScreen() => font = Runtime.CurrentRuntime.Fonts["kodemono"];

    public override void Render()
    {
        ClearBackground(Rgba.Black);
        string msg = "RUNNING BENCHMARK...";
        float size = 28 * Runtime.CurrentRuntime.ScaleF;
        Vector2 m = MeasureTextEx(font, msg, size, 1);
        DrawTextEx(font, msg,
            new Vector2((GetScreenWidth() - m.X) / 2, (GetScreenHeight() - m.Y) / 2), size, 1, Rgba.White);
    }

    public override void TopUpdate()
    {
        // Let the "running" message paint for a couple of frames before the blocking run, so it's actually seen.
        if (warmupFrames++ < 2)
            return;
        BenchmarkResult result = Benchmark.Run();
        Runtime.CurrentRuntime.RemoveScreen(this);
        Runtime.CurrentRuntime.AddScreen(new StatisticsScreen(result));
    }
}

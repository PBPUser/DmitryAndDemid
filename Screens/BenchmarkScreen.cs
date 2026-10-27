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

    string runningMessage = Helper.Translate("benchmark.running");
    string cancelHint = Helper.Translate("benchmark.back");

    public override void Render()
    {
        ClearBackground(Rgba.Black);
        float size = 28 * Runtime.CurrentRuntime.ScaleF;
        Vector2 m = MeasureTextEx(font, runningMessage, size, 1);
        DrawTextEx(font, runningMessage,
            new Vector2((GetScreenWidth() - m.X) / 2, (GetScreenHeight() - m.Y) / 2), size, 1, Rgba.White);
        // Cancel hint, bottom-centre.
        float hs = 16 * Runtime.CurrentRuntime.ScaleF;
        Vector2 hm = MeasureTextEx(font, cancelHint, hs, 1);
        DrawTextEx(font, cancelHint, new Vector2((GetScreenWidth() - hm.X) / 2, GetScreenHeight() - 40 * Runtime.CurrentRuntime.ScaleF),
            hs, 1, Rgba.Gray);
    }

    public override void TopUpdate()
    {
        // Escape / X / Back cancels before the (blocking) run starts, so the benchmark screen isn't a trap.
        if (IsKeyDown(KeyCode.Escape) || IsKeyDown(KeyCode.X) ||
            IsGamepadButtonDown(0, PadButton.RightFaceRight) || IsGamepadButtonDown(0, PadButton.MiddleRight))
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
            return;
        }
        // Let the "running" message paint for a couple of frames before the blocking run, so it's actually seen
        // (and so the cancel above has a window to fire).
        if (warmupFrames++ < 2)
            return;
        BenchmarkResult result = Benchmark.Run();
        Runtime.CurrentRuntime.RemoveScreen(this);
        Runtime.CurrentRuntime.AddScreen(new StatisticsScreen(result));
    }
}

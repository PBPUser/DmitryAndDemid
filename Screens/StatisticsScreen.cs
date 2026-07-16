using DmitryAndDemid.Common;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using System.Numerics;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

/// <summary>
/// Shows a <see cref="BenchmarkResult"/>: sim throughput (ticks/s and its multiple of the 60 TPS budget), peak
/// object count, and GC-heap memory max / avg / median. Reached from <see cref="BenchmarkScreen"/>; Esc / X /
/// Start returns to the menu.
/// </summary>
public class StatisticsScreen : Screen
{
    private readonly BenchmarkResult result;
    private readonly FontHandle font;
    private readonly double appearTime = GetTime();

    public StatisticsScreen(BenchmarkResult result)
    {
        this.result = result;
        font = Runtime.CurrentRuntime.Fonts["kodemono"];
    }

    public override void TopUpdate()
    {
        if (IsKeyDown(KeyCode.Escape) || IsKeyDown(KeyCode.X) || IsKeyDown(KeyCode.Enter) ||
            IsGamepadButtonDown(0, PadButton.RightFaceRight) || IsGamepadButtonDown(0, PadButton.MiddleRight))
            Runtime.CurrentRuntime.RemoveScreen(this);
    }

    public override void Render()
    {
        ClearBackground(new Rgba(12, 12, 18));
        float scale = Runtime.CurrentRuntime.ScaleF;
        float x = 40 * scale;
        // A little jump when the results appear: one quick decaying hop over ~0.4s, then it settles.
        float t = (float)(GetTime() - appearTime);
        float jump = 22f * scale * MathF.Max(0f, MathF.Sin(t * 8f)) * MathF.Exp(-t * 4f);
        float y = 32 * scale - jump;
        float titleSize = 30 * scale, body = 20 * scale, line = 28 * scale;

        DrawTextEx(font, "BENCHMARK RESULTS", new Vector2(x, y), titleSize, 1, Rgba.Yellow);
        y += line * 2;

        if (result.Error != null)
        {
            Row("FAILED", result.Error, Rgba.Red, x, ref y, body, line);
            Hint(scale);
            return;
        }

        Row("Backend", result.Backend, Rgba.White, x, ref y, body, line);
        Row("Target load", $"{result.TargetLoad} bullets", Rgba.White, x, ref y, body, line);
        Row("Peak objects", $"{result.PeakObjects}", Rgba.White, x, ref y, body, line);
        y += line * 0.5f;

        Rgba perf = result.RealtimeMultiple >= 1 ? Rgba.Green : Rgba.Red;
        Row("Sim throughput", $"{result.TicksPerSec:F0} ticks/s", perf, x, ref y, body, line);
        Row("vs 60 TPS budget", $"{result.RealtimeMultiple:F2}x  ({(result.RealtimeMultiple >= 1 ? "PASS" : "TOO SLOW")})",
            perf, x, ref y, body, line);
        Row("Ticks / time", $"{result.Ticks} in {result.Seconds:F2}s", Rgba.Gray, x, ref y, body, line);
        y += line * 0.5f;

        Row("GC heap  max", Benchmark.FormatBytes(result.MemMaxBytes), Rgba.White, x, ref y, body, line);
        Row("GC heap  avg", Benchmark.FormatBytes(result.MemAvgBytes), Rgba.White, x, ref y, body, line);
        Row("GC heap  median", Benchmark.FormatBytes(result.MemMedianBytes), Rgba.White, x, ref y, body, line);

        Hint(scale);
    }

    void Row(string label, string value, Rgba color, float x, ref float y, float size, float line)
    {
        DrawTextEx(font, label, new Vector2(x, y), size, 1, Rgba.Gray);
        DrawTextEx(font, value, new Vector2(x + 280 * Runtime.CurrentRuntime.ScaleF, y), size, 1, color);
        y += line;
    }

    void Hint(float scale) =>
        DrawTextEx(font, "Esc / X — back", new Vector2(40 * scale, GetScreenHeight() - 40 * scale),
            16 * scale, 1, Rgba.Gray);
}

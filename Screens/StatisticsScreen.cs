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

        DrawTextEx(font, Helper.Translate("benchmark.results"), new Vector2(x, y), titleSize, 1, Rgba.Yellow);
        y += line * 2;

        // Host machine info sits in its own right-hand column, so it shows whether or not the run itself failed.
        DrawSystemColumn(scale);

        if (result.Error != null)
        {
            Row(Helper.Translate("benchmark.failed"), result.Error, Rgba.Red, x, ref y, body, line);
            Hint(scale);
            return;
        }

        Row(Helper.Translate("benchmark.backend"), result.Backend, Rgba.White, x, ref y, body, line);
        Row(Helper.Translate("benchmark.load"), $"{result.TargetLoad} bullets", Rgba.White, x, ref y, body, line);
        Row(Helper.Translate("benchmark.peak"), $"{result.PeakObjects}", Rgba.White, x, ref y, body, line);
        y += line * 0.5f;

        Rgba perf = result.RealtimeMultiple >= 1 ? Rgba.Green : Rgba.Red;
        Row(Helper.Translate("benchmark.throughput"), $"{result.TicksPerSec:F0} ticks/s", perf, x, ref y, body, line);
        Row(Helper.Translate("benchmark.budget"),
            $"{result.RealtimeMultiple:F2}x  ({Helper.Translate(result.RealtimeMultiple >= 1 ? "benchmark.pass" : "benchmark.slow")})",
            perf, x, ref y, body, line);
        Row(Helper.Translate("benchmark.ticks"), $"{result.Ticks} in {result.Seconds:F2}s", Rgba.Gray, x, ref y, body, line);
        y += line * 0.5f;

        Row(Helper.Translate("benchmark.mem_max"), Benchmark.FormatBytes(result.MemMaxBytes), Rgba.White, x, ref y, body, line);
        Row(Helper.Translate("benchmark.mem_avg"), Benchmark.FormatBytes(result.MemAvgBytes), Rgba.White, x, ref y, body, line);
        Row(Helper.Translate("benchmark.mem_median"), Benchmark.FormatBytes(result.MemMedianBytes), Rgba.White, x, ref y, body, line);

        Hint(scale);
    }

    void Row(string label, string value, Rgba color, float x, ref float y, float size, float line)
    {
        DrawTextEx(font, label, new Vector2(x, y), size, 1, Rgba.Gray);
        DrawTextEx(font, value, new Vector2(x + 280 * Runtime.CurrentRuntime.ScaleF, y), size, 1, color);
        y += line;
    }

    /// <summary>
    /// The platform snapshot (OS / CPU / RAM / GPU) collected by <see cref="SystemInfo"/>, in a compact column
    /// on the right. Labels are universal abbreviations (kept out of translation.json on purpose). Unknown
    /// fields read "—" in grey; optional ones (clock, topology, API, VRAM, extensions, NPU) are simply omitted
    /// when the platform can't supply them.
    /// </summary>
    void DrawSystemColumn(float scale)
    {
        SystemInfo s = result.System;
        float x = GetScreenWidth() * 0.5f;
        float y = 32 * scale + 28 * scale * 2;   // line up under the title, same as the left column's first row
        float title = 20 * scale, body = 14 * scale, line = 20 * scale;

        DrawTextEx(font, "SYSTEM", new Vector2(x, y), title, 1, Rgba.Yellow);
        y += line * 1.5f;

        SysRow("OS", s.Os, x, ref y, body, line);
        string arch = s.ProcessArchitecture == s.OsArchitecture ? s.OsArchitecture
            : $"{s.ProcessArchitecture} / {s.OsArchitecture}";
        SysRow("ARCH", arch, x, ref y, body, line);

        SysRow("CPU", string.IsNullOrEmpty(s.CpuName) ? "—" : s.CpuName, x, ref y, body, line);
        string cores = s.PhysicalCores > 0 ? $"{s.PhysicalCores}C / {s.LogicalCores}T" : $"{s.LogicalCores}T";
        if (s.CoreTopology != null) cores += $"  ({s.CoreTopology})";
        SysRow("CORES", cores, x, ref y, body, line);
        if (s.MaxClockMHz > 0)
            SysRow("CLOCK", $"{s.MaxClockMHz / 1000.0:F2} GHz", x, ref y, body, line);

        string ram = s.TotalRamBytes > 0 ? Benchmark.FormatBytes(s.TotalRamBytes) : "—";
        if (s.RamClock != null) ram += $"  @ {s.RamClock}";
        SysRow("RAM", ram, x, ref y, body, line);

        SysRow("GPU", string.IsNullOrEmpty(s.Gpu) ? "—" : s.Gpu!, x, ref y, body, line);
        if (!string.IsNullOrEmpty(s.GpuApi))
            SysRow("API", s.GpuApi!, x, ref y, body, line);
        string vram = s.VramBytes > 0 ? Benchmark.FormatBytes(s.VramBytes) : "—";
        if (s.VramClock != null) vram += $"  @ {s.VramClock}";
        SysRow("VRAM", vram, x, ref y, body, line);
        if (s.GpuExtensions.Count > 0)
            SysRow("EXT", $"{s.GpuExtensions.Count} supported", x, ref y, body, line);
        if (!string.IsNullOrEmpty(s.Npu))
            SysRow("NPU", s.Npu!, x, ref y, body, line);
    }

    void SysRow(string label, string value, float x, ref float y, float size, float line)
    {
        float scale = Runtime.CurrentRuntime.ScaleF;
        DrawTextEx(font, label, new Vector2(x, y), size, 1, Rgba.Gray);
        float valX = x + 90 * scale;
        float avail = GetScreenWidth() - valX - 12 * scale;
        Rgba color = value == "—" ? Rgba.Gray : Rgba.White;
        DrawTextEx(font, Fit(value, size, avail), new Vector2(valX, y), size, 1, color);
        y += line;
    }

    /// <summary>Trims a value with an ellipsis so a long GPU/OS string can't run off the right edge.</summary>
    string Fit(string text, float size, float maxWidth)
    {
        if (maxWidth <= 0 || MeasureTextEx(font, text, size, 1).X <= maxWidth)
            return text;
        while (text.Length > 1 && MeasureTextEx(font, text + "…", size, 1).X > maxWidth)
            text = text[..^1];
        return text + "…";
    }

    void Hint(float scale) =>
        DrawTextEx(font, Helper.Translate("benchmark.back"), new Vector2(40 * scale, GetScreenHeight() - 40 * scale),
            16 * scale, 1, Rgba.Gray);
}

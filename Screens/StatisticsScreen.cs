using DmitryAndDemid.Common;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using Silk.NET.Core.Attributes;
using Silk.NET.Vulkan;
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
    string resultsTitle = Helper.Translate("benchmark.results");
    string backendTitle = Helper.Translate("benchmark.backend");
    string loadTitle = Helper.Translate("benchmark.load");
    string loadFormat = Helper.Translate("benchmark.load.format");
    string failedTitle = Helper.Translate("benchmark.failed");
    string peakTitle = Helper.Translate("benchmark.peak");
    string throughputTitle = Helper.Translate("benchmark.throughput");
    string budgetTitle = Helper.Translate("benchmark.budget");
    string ticksTitle = Helper.Translate("benchmark.ticks");
    string memMaxTitle = Helper.Translate("benchmark.mem_max");
    string memAvgTitle = Helper.Translate("benchmark.mem_avg");
    string memMedianTitle = Helper.Translate("benchmark.mem_median");
    string backHint = Helper.Translate("benchmark.back");
    string passHint = Helper.Translate("benchmark.pass");
    string slowHint = Helper.Translate("benchmark.slow");
    string systemHint = Helper.Translate("benchmark.system");
    string osHint = Helper.Translate("benchmark.os");
    string apiHint = Helper.Translate("benchmark.api");
    string archHint = Helper.Translate("benchmark.arch");
    string cpuHint = Helper.Translate("benchmark.cpu");
    string coresHint = Helper.Translate("benchmark.cores");
    string ramHint = Helper.Translate("benchmark.ram");
    string gpuHint = Helper.Translate("benchmark.gpu");
    string vramHint = Helper.Translate("benchmark.vram");
    string npuHint = Helper.Translate("benchmark.npu");
    string extHint = Helper.Translate("benchmark.extensions");
    string clockHint = Helper.Translate("benchmark.clock");
    string extHintFormat = Helper.Translate("benchmark.extensions.format");
    string clockHintFormat = Helper.Translate("benchmark.clock.format");
    string softwareNpuHint = Helper.Translate("benchmark.npu.software");
    string coreTopologyFormatThreadsOnly = Helper.Translate("benchmark.cores.threads_only");
    string coreTopologyFormatClassic = Helper.Translate("benchmark.cores.classic");
    string coreTopologyFormatHybrid = Helper.Translate("benchmark.cores.hybrid");
    string coreTopologyFormatHybridCluster = Helper.Translate("benchmark.cores.hybrid.cluster");


    float XMultiplier = 0;
    int Page = 0;

    // The screen renders its content into its own target (in PreRender — Render() is already nested inside
    // the shared Backbuffer target, and BeginTextureMode can't open there) so the page-switch swipe can be
    // composited through the motion-blur shader.
    private const float MaxBlur = 0.035f;              // blur radius at the swipe's midpoint, in screen UV units
    private RenderedTexture? ContentTarget;
    private ShaderHandle MotionBlur => Runtime.CurrentRuntime.Shaders["motion_blur"];


    public StatisticsScreen(BenchmarkResult result)
    {
        this.result = result;
        font = Runtime.CurrentRuntime.Fonts["kodemono"];
    }

    public override void TopUpdate()
    {
        int z = -1 + 2 * Page;
        XMultiplier = Math.Clamp((float)((1 - Page) + ((GetTime() - MenuScreen.PreviousKeyTimestamp) / MenuScreen.MenuSwitchCooldown * z)), 0, 1);
        if (GetTime() - MenuScreen.PreviousKeyTimestamp < MenuScreen.MenuActivateCooldown)
            return;
        if (IsKeyDown(KeyCode.Escape) || IsKeyDown(KeyCode.X) || IsKeyDown(KeyCode.Enter) ||
            IsGamepadButtonDown(0, PadButton.RightFaceRight) || IsGamepadButtonDown(0, PadButton.MiddleRight))
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
            MenuScreen.PreviousKeyTimestamp = GetTime();
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["extend"]);
        }
        else if (IsKeyDown(KeyCode.Left) || IsKeyDown(KeyCode.Right) || IsGamepadButtonDown(0, PadButton.RightFaceLeft) || IsGamepadButtonDown(0, PadButton.RightFaceRight))
        {
            Page = Page == 0 ? 1 : 0;
            MenuScreen.PreviousKeyTimestamp = GetTime();
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["swap"]);
        }

    }

    public override void PreRender(double delta)
    {
        base.PreRender(delta);
        if (ContentTarget == null || ContentTarget.Value.Texture.Width != Runtime.CurrentRuntime.Width ||
            ContentTarget.Value.Texture.Height != Runtime.CurrentRuntime.Height)
        {
            if (ContentTarget != null)
                UnloadRenderTexture(ContentTarget.Value);
            ContentTarget = LoadRenderTexture(Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height);
        }
        BeginTextureMode(ContentTarget.Value);
        ClearBackground(new Rgba(12, 12, 18));
        DrawContent();
        EndTextureMode();
    }

    public override void Render()
    {
        ClearBackground(new Rgba(12, 12, 18));
        if (ContentTarget == null)
            return;
        // Horizontal motion blur tracking the swipe: |2x-1| is 1 when a page is settled and 0 exactly
        // mid-swap, so the blur peaks when the columns move fastest and is gone once they land.
        float blur = (1 - MathF.Abs(XMultiplier * 2 - 1)) * MaxBlur;
        bool blurring = blur > 0.0005f;
        if (blurring)
        {
            ShaderHandle shader = MotionBlur;
            SetShaderValue(shader, GetShaderLocation(shader, "direction"), new Vector2(1, 0), UniformType.Vec2);
            SetShaderValue(shader, GetShaderLocation(shader, "strength"), blur, UniformType.Float);
            BeginShaderMode(shader);
        }
        DrawTexturePro(ContentTarget.Value.Texture, Helper.GetFullSourceRenderTexture(ContentTarget.Value),
            Helper.GetFullscreenSource(), Vector2.Zero, 0, Rgba.White);
        if (blurring)
            EndShaderMode();
    }

    public override void Unload()
    {
        if (ContentTarget != null)
            UnloadRenderTexture(ContentTarget.Value);
        base.Unload();
    }

    void DrawContent()
    {
        float pre = MathF.Abs(XMultiplier * 2 - 1);
        float scale = Runtime.CurrentRuntime.ScaleF - ((1 -  pre) * 0.2f);


        // Host machine info sits in its own right-hand column, so it shows whether or not the run itself failed.
        DrawResults(scale, XMultiplier * Runtime.CurrentRuntime.Width);
        DrawSystemColumn(scale, (XMultiplier - 1) * Runtime.CurrentRuntime.Width);
    }

    void Row(string label, string value, Rgba color, float x, ref float y, float size, float line)
    {
        DrawTextEx(font, label, new Vector2(x, y), size, 1, Rgba.Gray);
        DrawTextEx(font, value, new Vector2(x + 280 * Runtime.CurrentRuntime.ScaleF, y), size, 1, color);
        y += line;
    }

    void DrawResults(float scale, float offsetX)
    {
        float x = 40 * scale + offsetX;
        // A little jump when the results appear: one quick decaying hop over ~0.4s, then it settles.
        float t = (float)(GetTime() - appearTime);
        float jump = 22f * scale * MathF.Max(0f, MathF.Sin(t * 8f)) * MathF.Exp(-t * 4f);
        float y = 32 * scale - jump;
        float titleSize = 30 * scale, body = 20 * scale, line = 28 * scale;
        DrawTextEx(font, resultsTitle, new Vector2(x, y), titleSize, 1, Rgba.Yellow);
        y += line * 2;
        if (result.Error != null)
        {
            Row(failedTitle, result.Error, Rgba.Red, x, ref y, body, line);
            Hint(scale);
            return;
        }

        Row(backendTitle, result.Backend, Rgba.White, x, ref y, body, line);
        Row(loadTitle, loadFormat.Replace("%s", $"{result.TargetLoad}"), Rgba.White, x, ref y, body, line);
        Row(peakTitle, $"{result.PeakObjects}", Rgba.White, x, ref y, body, line);
        y += line * 0.5f;

        Rgba perf = result.RealtimeMultiple >= 1 ? Rgba.Green : Rgba.Red;
        Row(throughputTitle, $"{result.TicksPerSec:F0} ticks/s", perf, x, ref y, body, line);
        Row(budgetTitle,
            $"{result.RealtimeMultiple:F2}x  ({(result.RealtimeMultiple >= 1 ? passHint : slowHint)})",
            perf, x, ref y, body, line);
        Row(ticksTitle, $"{result.Ticks} in {result.Seconds:F2}s", Rgba.Gray, x, ref y, body, line);
        y += line * 0.5f;

        Row(memMaxTitle, Benchmark.FormatBytes(result.MemMaxBytes), Rgba.White, x, ref y, body, line);
        Row(memAvgTitle, Benchmark.FormatBytes(result.MemAvgBytes), Rgba.White, x, ref y, body, line);
        Row(memMedianTitle, Benchmark.FormatBytes(result.MemMedianBytes), Rgba.White, x, ref y, body, line);

        Hint(scale);
    }

    /// <summary>
    /// The platform snapshot (OS / CPU / RAM / GPU) collected by <see cref="SystemInfo"/>, in a compact column
    /// on the right. Labels are universal abbreviations (kept out of translation.json on purpose). Unknown
    /// fields read "—" in grey; optional ones (clock, topology, API, VRAM, extensions, NPU) are simply omitted
    /// when the platform can't supply them.
    /// </summary>
    void DrawSystemColumn(float scale, float offsetX)
    {
        SystemInfo s = result.System;
        float x = 40 * scale + offsetX;
        float y = 32 * scale;
        float title = 30 * scale, body = 20 * scale, line = 28 * scale;

        DrawTextEx(font, systemHint, new Vector2(x, y), title, 1, Rgba.Yellow);
        y += line * 2;

        Row(backendTitle, result.Backend, Rgba.White, x, ref y, body, line);
        Row(osHint, s.Os, Rgba.White, x, ref y, body, line);
        string arch = s.ProcessArchitecture == s.OsArchitecture ? s.OsArchitecture
            : $"{s.ProcessArchitecture} / {s.OsArchitecture}";
        Row(archHint, arch, Rgba.White, x, ref y, body, line);

        Row(cpuHint, string.IsNullOrEmpty(s.CpuName) ? "—" : s.CpuName, Rgba.White, x, ref y, body, line);
        string cores = s.PhysicalCores > 0 ? $"{s.PhysicalCores}C / {s.LogicalCores}T" : $"{s.LogicalCores}T";
        if (s.CoreTopology != null) cores += $"  ({s.CoreTopology})";
        Row(coresHint, cores, Rgba.White, x, ref y, body, line);
        if (s.MaxClockMHz > 0)
            Row(clockHint, clockHintFormat.Replace("%s", $"{s.MaxClockMHz / 1000.0:F2}"), Rgba.White, x, ref y, body, line);

        string ram = s.TotalRamBytes > 0 ? Benchmark.FormatBytes(s.TotalRamBytes) : "—";
        if (s.RamClock != null) ram += $"  @ {s.RamClock}";
        Row(ramHint, ram, Rgba.White, x, ref y, body, line);

        Row(gpuHint, string.IsNullOrEmpty(s.Gpu) ? "—" : Helper.TranslateGpuName(s.Gpu!), Rgba.White, x, ref y, body, line);
        if (!string.IsNullOrEmpty(s.GpuApi))
            Row(apiHint, s.GpuApi!, Rgba.White, x, ref y, body, line);
        string vram = s.VramBytes > 0 ? Benchmark.FormatBytes(s.VramBytes) : "—";
        if (s.VramClock != null) 
            vram += $"  @ {s.VramClock}";
        Row(vramHint, vram, Rgba.White, x, ref y, body, line);
        if (s.GpuExtensions.Count > 0)
            Row(extHint, extHintFormat.Replace("%s", $"{s.GpuExtensions.Count:2b}"), Rgba.White, x, ref y, body, line);
        Row(npuHint, String.IsNullOrEmpty(s.Npu) ? softwareNpuHint : s.Npu, Rgba.White, x, ref y, body, line);
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
        DrawTextEx(font, backHint, new Vector2(40 * scale, GetScreenHeight() - 40 * scale),
            16 * scale, 1, Rgba.Gray);
}

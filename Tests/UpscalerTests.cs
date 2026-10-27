using DmitryAndDemid.Rendering.Upscaling;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// The upscaler catalogue's rules (Rendering/Upscaling/Upscalers): which mode is offered where, what each
/// renders at, and how the config keys round-trip. Pure — the file probes are passed in as facts, so these
/// hold on any machine regardless of what is installed.
/// </summary>
public class UpscalerTests
{
    [Theory]
    [InlineData("off", UpscalerKind.Off)]
    [InlineData("fsr", UpscalerKind.Fsr)]
    [InlineData("DLAA", UpscalerKind.Dlaa)]
    [InlineData("xess", UpscalerKind.Xess)]
    [InlineData("dlss", UpscalerKind.Dlss)]
    [InlineData("dlssnr", UpscalerKind.DlssNeural)]
    [InlineData("", UpscalerKind.Off)]
    [InlineData(null, UpscalerKind.Off)]
    [InlineData("nonsense", UpscalerKind.Off)]
    public void Keys_parse_and_unknown_means_off(string? key, UpscalerKind kind)
    {
        Assert.Equal(kind, Upscalers.Parse(key));
    }

    [Fact]
    public void Every_kind_round_trips_through_its_key()
    {
        foreach (var e in Upscalers.All)
            Assert.Equal(e.Kind, Upscalers.Parse(Upscalers.KeyOf(e.Kind)));
    }

    [Fact]
    public void Software_modes_are_always_available()
    {
        foreach (UpscalerKind kind in new[] { UpscalerKind.Off, UpscalerKind.Fsr, UpscalerKind.Dlaa })
        {
            Assert.Null(Upscalers.Unavailable(kind, isWindows: false, streamline: false, neural: false, xess: false));
            Assert.Null(Upscalers.Unavailable(kind, isWindows: true, streamline: true, neural: true, xess: true));
        }
    }

    [Fact]
    public void Neural_rendering_is_windows_only_and_needs_its_files()
    {
        Assert.Equal("settings.upscaler.windows_only",
            Upscalers.Unavailable(UpscalerKind.DlssNeural, isWindows: false, streamline: true, neural: true, xess: false));
        Assert.Equal("settings.upscaler.needs_nr_files",
            Upscalers.Unavailable(UpscalerKind.DlssNeural, isWindows: true, streamline: true, neural: false, xess: false));
        Assert.Equal("settings.upscaler.needs_nr_files",
            Upscalers.Unavailable(UpscalerKind.DlssNeural, isWindows: true, streamline: false, neural: false, xess: false));
        Assert.Null(Upscalers.Unavailable(UpscalerKind.DlssNeural, isWindows: true, streamline: true, neural: true, xess: false));
    }

    [Fact]
    public void Dlss_needs_streamline_on_windows_and_xess_needs_its_runtime()
    {
        Assert.Equal("settings.upscaler.needs_streamline",
            Upscalers.Unavailable(UpscalerKind.Dlss, isWindows: true, streamline: false, neural: false, xess: false));
        Assert.Equal("settings.upscaler.windows_only",
            Upscalers.Unavailable(UpscalerKind.Dlss, isWindows: false, streamline: true, neural: false, xess: false));
        Assert.Null(Upscalers.Unavailable(UpscalerKind.Dlss, isWindows: true, streamline: true, neural: false, xess: false));
        Assert.Equal("settings.upscaler.needs_xess",
            Upscalers.Unavailable(UpscalerKind.Xess, isWindows: true, streamline: true, neural: true, xess: false));
        Assert.Null(Upscalers.Unavailable(UpscalerKind.Xess, isWindows: false, streamline: false, neural: false, xess: true));
    }

    [Fact]
    public void Native_modes_render_at_full_scale_and_presets_shrink_it()
    {
        Assert.Equal(1f, Upscalers.RenderScale(UpscalerKind.Off, 3));
        Assert.Equal(1f, Upscalers.RenderScale(UpscalerKind.Dlaa, 3));
        Assert.Equal(1f, Upscalers.RenderScale(UpscalerKind.Fsr, 0));      // Native: sharpen only
        Assert.Equal(0.77f, Upscalers.RenderScale(UpscalerKind.Fsr, 1));
        Assert.Equal(0.67f, Upscalers.RenderScale(UpscalerKind.Fsr, Upscalers.DefaultQuality));
        Assert.Equal(0.5f, Upscalers.RenderScale(UpscalerKind.Dlss, 4));
        Assert.Equal(0.33f, Upscalers.RenderScale(UpscalerKind.Fsr, 5));   // Ultra Performance
        Assert.Equal(0.33f, Upscalers.RenderScale(UpscalerKind.Fsr, 99));  // clamped to the last preset
        Assert.Equal(1f, Upscalers.RenderScale(UpscalerKind.Fsr, -5));     // and to the first
    }

    [Fact]
    public void Missing_runtime_files_are_reported_not_thrown()
    {
        string nowhere = Path.Combine(Path.GetTempPath(), "aag2-no-such-folder-" + Guid.NewGuid());
        Assert.False(Upscalers.HasFiles(nowhere, Upscalers.StreamlineFiles));
    }
}

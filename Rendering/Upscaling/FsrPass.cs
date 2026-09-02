using System.Numerics;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Rendering.Upscaling;

/// <summary>
/// The window upscale: AMD FidelityFX Super Resolution 1.0 in Likhanov32D's own shaders. The frame — the
/// runtime's backbuffer, rendered at the internal resolution — goes through EASU (edge-adaptive spatial
/// upsampling, <c>fsr_easu.fs</c>) into an intermediate target at the presented size, and that through RCAS
/// (robust contrast-adaptive sharpening, <c>fsr_rcas.fs</c>) onto the window. The DLAA-style mode skips EASU
/// and only sharpens the native frame.
///
/// Both shaders read their source with a positive full-size rect and undo the render-target flip themselves
/// (see the shaders), so the pass is the same on every backend. The intermediate is (re)made whenever the
/// presented size changes.
/// </summary>
public sealed class FsrPass
{
    private readonly ShaderHandle Easu, Rcas;
    private readonly int EasuInputSize, EasuOutputSize, RcasSharpness, RcasSize;
    private RenderedTexture Mid;
    private int MidW, MidH;

    public bool Ready => Easu.Id != 0 && Rcas.Id != 0;

    public FsrPass()
    {
        var shaders = Runtime.CurrentRuntime.Shaders;
        shaders.TryGetValue("fsr_easu", out Easu);
        shaders.TryGetValue("fsr_rcas", out Rcas);
        if (Easu.Id != 0)
        {
            EasuInputSize = GetShaderLocation(Easu, "inputSize");
            EasuOutputSize = GetShaderLocation(Easu, "outputSize");
        }
        if (Rcas.Id != 0)
        {
            RcasSharpness = GetShaderLocation(Rcas, "sharpness");
            RcasSize = GetShaderLocation(Rcas, "inputSize");
        }
    }

    /// <summary>
    /// Presents <paramref name="source"/> (a render target of <paramref name="srcW"/> x <paramref name="srcH"/>)
    /// into <paramref name="dest"/> on the window. <paramref name="sharpness"/> is 0..1 (RCAS's 0 = strongest
    /// is mapped so that 1 here is the strongest). <paramref name="sharpenOnly"/> skips the upscale.
    /// </summary>
    public void Present(BasicTexture source, int srcW, int srcH, Rect dest, float sharpness, bool sharpenOnly)
    {
        int w = Math.Max(1, (int)MathF.Round(dest.Width));
        int h = Math.Max(1, (int)MathF.Round(dest.Height));
        BasicTexture toSharpen = source;
        int sw = srcW, sh = srcH;
        if (!sharpenOnly)
        {
            EnsureMid(w, h);
            BeginTextureMode(Mid);
            ClearBackground(Rgba.Black);
            SetShaderValue(Easu, EasuInputSize, new Vector2(srcW, srcH), UniformType.Vec2);
            SetShaderValue(Easu, EasuOutputSize, new Vector2(w, h), UniformType.Vec2);
            BeginBlendMode(BlendMode.CopyRgb);
            BeginShaderMode(Easu);
            DrawTexturePro(source, new Rect(0, 0, srcW, srcH), new Rect(0, 0, w, h), Vector2.Zero, 0, Rgba.White);
            EndShaderMode();
            EndBlendMode();
            EndTextureMode();
            toSharpen = Mid.Texture;
            sw = w;
            sh = h;
        }
        // RCAS's own scale runs 0 (sharpest) to 2 (off); the setting is the friendlier 0..1, 1 sharpest.
        float rcas = (1f - Math.Clamp(sharpness, 0f, 1f)) * 2f;
        SetShaderValue(Rcas, RcasSharpness, rcas, UniformType.Float);
        SetShaderValue(Rcas, RcasSize, new Vector2(sw, sh), UniformType.Vec2);
        BeginBlendMode(BlendMode.CopyRgb);
        BeginShaderMode(Rcas);
        DrawTexturePro(toSharpen, new Rect(0, 0, sw, sh), dest, Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        EndBlendMode();
    }

    private void EnsureMid(int w, int h)
    {
        if (MidW == w && MidH == h)
            return;
        if (MidW != 0)
            UnloadRenderTexture(Mid);
        Mid = LoadRenderTexture(w, h);
        SetTextureFilter(Mid.Texture, FilterMode.Point);
        MidW = w;
        MidH = h;
    }

    public void Unload()
    {
        if (MidW != 0)
            UnloadRenderTexture(Mid);
        MidW = MidH = 0;
    }
}

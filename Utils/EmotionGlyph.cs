using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Rendering;
using StbTrueTypeSharp;

namespace DmitryAndDemid.Utils;

/// <summary>
/// Bakes a dialog line's emotion — one symbol from Noto Sans Symbols 2 (<see cref="FontPath"/>) — into a
/// small RGBA image: the glyph slightly deformed on two soft sine waves, in a random bright colour, with a
/// dark outline and a drop shadow. Rotation is left to the caller, so it can sway at draw time.
///
/// The font is rasterised on the CPU through StbTrueType rather than through the backends' font loaders,
/// which only carry the 95 ASCII glyphs; nothing here touches the GPU, which keeps the whole bake testable
/// headlessly (<c>Tests/DialogEmotionTests.cs</c>). The font file lives under <c>Assets/Fonts/OnDemand</c>, a
/// folder <c>Runtime.LoadFonts</c> does not scan, so it is read only when a chapter with dialog is loaded
/// (<see cref="DmitryAndDemid.Gameplay.RuntimeData.RuntimeChapter"/>) and dropped again with the bytes.
/// </summary>
public static unsafe class EmotionGlyph
{
    public const string FontPath = "Assets/Fonts/OnDemand/NotoSansSymbols2-Regular.ttf";

    /// <summary>How far a line's emotion is tilted, in degrees either way — rolled per line.</summary>
    public const float MaxTiltDegrees = 14f;

    /// <summary>The font's bytes, or null when the file is not there (the emotions are then simply absent).</summary>
    public static byte[]? ReadFont() => Assets.Exists(FontPath) ? Assets.ReadAllBytes(FontPath) : null;

    /// <summary>
    /// Rasterises the first codepoint of <paramref name="emotion"/> at <paramref name="pixelHeight"/> and dresses
    /// it (<see cref="Compose"/>). Null for an empty string, a glyph the font does not have, or a blank glyph.
    /// </summary>
    public static CpuImage? Render(byte[] ttf, string emotion, int pixelHeight, Random rng)
    {
        if (string.IsNullOrEmpty(emotion) || pixelHeight < 4)
            return null;
        int codepoint = char.ConvertToUtf32(emotion, 0);
        StbTrueType.stbtt_fontinfo info = new();
        fixed (byte* data = ttf)
        {
            if (StbTrueType.stbtt_InitFont(info, data, 0) == 0)
                return null;
            if (StbTrueType.stbtt_FindGlyphIndex(info, codepoint) == 0)
                return null;
            float scale = StbTrueType.stbtt_ScaleForPixelHeight(info, pixelHeight);
            int x0, y0, x1, y1;
            StbTrueType.stbtt_GetCodepointBitmapBox(info, codepoint, scale, scale, &x0, &y0, &x1, &y1);
            int gw = x1 - x0, gh = y1 - y0;
            if (gw <= 0 || gh <= 0)
                return null;
            byte[] glyph = new byte[gw * gh];
            fixed (byte* dst = glyph)
                StbTrueType.stbtt_MakeCodepointBitmap(info, dst, gw, gh, gw, scale, scale, codepoint);
            return Compose(glyph, gw, gh, pixelHeight, rng);
        }
    }

    /// <summary>
    /// The dressing, from a plain coverage bitmap: a wobble (each axis displaced by a sine of the other, a few
    /// percent of the height), a dark outline dilated around it, a shadow of that outline offset down-right,
    /// and a random bright fill on top. Pure — it only reads <paramref name="glyph"/> and <paramref name="rng"/>.
    /// </summary>
    public static CpuImage Compose(byte[] glyph, int gw, int gh, int pixelHeight, Random rng)
    {
        float amp = pixelHeight * 0.045f;
        int outline = Math.Max(2, (int)MathF.Round(pixelHeight * 0.07f));
        int shadowX = Math.Max(1, (int)MathF.Round(pixelHeight * 0.06f));
        int shadowY = Math.Max(1, (int)MathF.Round(pixelHeight * 0.08f));
        int pad = outline + Math.Max(shadowX, shadowY) + (int)MathF.Ceiling(amp) + 2;
        int w = gw + pad * 2, h = gh + pad * 2;

        // 1. The deformed glyph: sample the bitmap at coordinates bent by two slow sines.
        float f1 = MathF.PI * 2f / (gh * (0.9f + (float)rng.NextDouble() * 0.7f));
        float f2 = MathF.PI * 2f / (gw * (0.9f + (float)rng.NextDouble() * 0.7f));
        float p1 = (float)rng.NextDouble() * MathF.PI * 2f, p2 = (float)rng.NextDouble() * MathF.PI * 2f;
        float[] body = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float sx = x - pad + amp * MathF.Sin(y * f1 + p1);
                float sy = y - pad + amp * MathF.Sin(x * f2 + p2);
                body[y * w + x] = Sample(glyph, gw, gh, sx, sy);
            }

        // 2. The outline: the body dilated by a disc.
        float[] edge = new float[w * h];
        int r2 = outline * outline;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float best = 0f;
                for (int dy = -outline; dy <= outline && best < 0.999f; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= h) continue;
                    for (int dx = -outline; dx <= outline; dx++)
                    {
                        int xx = x + dx;
                        if (xx < 0 || xx >= w || dx * dx + dy * dy > r2) continue;
                        float v = body[yy * w + xx];
                        if (v > best) best = v;
                    }
                }
                edge[y * w + x] = best;
            }

        // 3. Colours: a random bright fill, an outline in a near-black tint of it, a translucent black shadow.
        (float fr, float fg, float fb) = HsvToRgb((float)rng.NextDouble() * 360f,
            0.78f + (float)rng.NextDouble() * 0.17f, 0.92f + (float)rng.NextDouble() * 0.08f);
        float er = fr * 0.16f, eg = fg * 0.16f, eb = fb * 0.16f;
        const float shadowAlpha = 0.55f;

        // 4. Composite, premultiplied, back to front: shadow, outline, fill.
        byte[] pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float pr = 0f, pg = 0f, pb = 0f, pa = 0f;
                int sx = x - shadowX, sy = y - shadowY;
                float shadow = sx >= 0 && sy >= 0 ? edge[sy * w + sx] * shadowAlpha : 0f;
                Over(ref pr, ref pg, ref pb, ref pa, 0f, 0f, 0f, shadow);
                Over(ref pr, ref pg, ref pb, ref pa, er, eg, eb, edge[y * w + x]);
                Over(ref pr, ref pg, ref pb, ref pa, fr, fg, fb, body[y * w + x]);
                int i = (y * w + x) * 4;
                if (pa > 0.001f)
                {
                    pixels[i] = ToByte(pr / pa);
                    pixels[i + 1] = ToByte(pg / pa);
                    pixels[i + 2] = ToByte(pb / pa);
                    pixels[i + 3] = ToByte(pa);
                }
            }
        return CpuImage.FromPixels(w, h, pixels);
    }

    /// <summary>Source-over onto a premultiplied accumulator.</summary>
    private static void Over(ref float pr, ref float pg, ref float pb, ref float pa, float r, float g, float b, float a)
    {
        if (a <= 0f) return;
        pr = r * a + pr * (1f - a);
        pg = g * a + pg * (1f - a);
        pb = b * a + pb * (1f - a);
        pa = a + pa * (1f - a);
    }

    /// <summary>Bilinear read of the coverage bitmap, 0..1, zero outside it.</summary>
    private static float Sample(byte[] glyph, int gw, int gh, float x, float y)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float tx = x - x0, ty = y - y0;
        float v00 = At(glyph, gw, gh, x0, y0), v10 = At(glyph, gw, gh, x0 + 1, y0);
        float v01 = At(glyph, gw, gh, x0, y0 + 1), v11 = At(glyph, gw, gh, x0 + 1, y0 + 1);
        return (v00 * (1f - tx) + v10 * tx) * (1f - ty) + (v01 * (1f - tx) + v11 * tx) * ty;
    }

    private static float At(byte[] glyph, int gw, int gh, int x, int y) =>
        x < 0 || y < 0 || x >= gw || y >= gh ? 0f : glyph[y * gw + x] / 255f;

    private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0f, 255f);

    private static (float, float, float) HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
        float m = v - c;
        (float r, float g, float b) = ((int)(h / 60f) % 6) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        return (r + m, g + m, b + m);
    }
}

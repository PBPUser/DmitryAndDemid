using System.Numerics;
using System.Text.Json;
using DmitryAndDemid.Rendering;

namespace DmitryAndDemid.Utils;

/// <summary>
/// The pure half of <see cref="Helper"/> — parsing, interpolation, easing, colour packing, the translation
/// tables and Pizzics' collision primitive. It exists because <see cref="Helper"/>'s static constructor
/// allocates an 8192×8192 render texture (<c>AlliasTextureTemp</c>), which means any access to Helper at all
/// needs a live GPU backend; everything here is headless and is what the unit tests drive. Helper keeps its
/// members as thin forwards, so no caller had to change.
/// </summary>
public static class HelperPure
{
    public static bool GetResolutionFromString(string str, out (int width, int height) res)
    {
        res = (0, 0);
        var split = str.Split("x");
        if (split.Length < 2)
            return false;
        if (!int.TryParse(split[0], out res.width))
            return false;
        return int.TryParse(split[1], out res.height);
    }

    public static bool GetMultiplyerFromRes(string str, out double multiplyer)
    {
        multiplyer = 0;
        (int width, int height) res;
        if (!GetResolutionFromString(str, out res))
            return false;
        multiplyer = ((double)res.width) / 640d;
        return true;
    }

    public static Vector4 ColorToVector(Rgba color)
    {
        return new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }

    public static Rect Mix(Rect rc1, Rect rc2, float mix)
    {
        float imix = 1f - mix;
        return new Rect(
            rc1.X * imix + rc2.X * mix,
            rc1.Y * imix + rc2.Y * mix,
            rc1.Width * imix + rc2.Width * mix,
            rc1.Height * imix + rc2.Height * mix
        );
    }

    public static float Mix(float f1, float f2, float mix)
    {
        return f1 * (1 - mix) + f2 * mix;
    }

    public static Vector4 Mix(Vector4 color1, Vector4 color2, float mix)
    {
        float imix = 1f - mix;
        return new Vector4(
            color1[0] * imix + color2[0] * mix,
            color1[1] * imix + color2[1] * mix,
            color1[2] * imix + color2[2] * mix,
            color1[3] * imix + color2[3] * mix
        );
    }

    public static Rgba Mix(Rgba color1, Rgba color2, float mix)
    {
        float imix = 1f - mix;
        return new Rgba(
            (byte)(color1.R * imix + color2.R * mix),
            (byte)(color1.G * imix + color2.G * mix),
            (byte)(color1.B * imix + color2.B * mix),
            (byte)(color1.A * imix + color2.A * mix)
        );
    }

    ///<summary>
    /// Computes object time
    /// </summary>
    public static double ComputeObjectTime(double time, double start, double appearLength, double end, double disappearLength)
    {
        double timeAppear = Math.Clamp((time - start) / appearLength, 0, 1);
        double timeDisappear = Math.Clamp((end - time) / disappearLength, 0, 1);
        return timeAppear * timeDisappear;
    }

    static float Clamp(float value, float min, float max)
    {
        return MathF.Max(MathF.Min(value, max), min);
    }

    public static float ComputeObjectTime(float time, float start, float appearLength, float end, float disappearLength)
    {
        float timeAppear = Clamp((time - start) / appearLength, 0, 1);
        float timeDisappear = Clamp((end - time) / disappearLength, 0, 1);
        return timeAppear * timeDisappear;
    }

    public static float ComputeObjectTime(int time, int start, int appearLength, int end, int disappearLength)
    {
        float timeAppear = Clamp((time - start) / (float)appearLength, 0, 1);
        float timeDisappear = Clamp((end - time) / (float)disappearLength, 0, 1);
        return timeAppear * timeDisappear;
    }

    public static float ComputeObjectTime0To2(float time, float start, float appearLength, float end,
        float disappearLength)
    {
        float timeAppear = Clamp((time - start) / appearLength, 0, 1);
        float timeDisappear = Clamp((time - end) / disappearLength, 0, 1);
        return timeAppear + timeDisappear;
    }

    public static double ComputeObjectTimeStart(double time, double start, double appearLength)
    {
        return Math.Clamp((time - start) / appearLength, 0, 1);
    }

    public static byte TimeToTransparency(double time)
    {
        return (byte)(255 * time);
    }

    public static float Pow2F(float x)
    {
        return x * x;
    }

    public static float EaseInOutElasticF(float x)
    {
        float c5 = (2f * MathF.PI) / 4.5f;
        return x == 0
        ? 0
        : x == 1
        ? 1
        : x < 0.5
        ? -(MathF.Pow(2, 20 * x - 10) * MathF.Sin((20 * x - 11.125f) * c5)) / 2
        : (MathF.Pow(2, -20 * x + 10) * MathF.Sin((20 * x - 11.125f) * c5)) / 2 + 1;
    }

    public static int Vector3ColorToInt(Vector3 vector)
    {
        int r = (int)(0xFF * vector.X);
        int g = (int)(0xFF * vector.Y);
        int b = (int)(0xFF * vector.Z);
        return r << 16 | g << 8 | b;
    }

    public static Vector3 ColorIntToVector3(int color)
    {
        float r = (color >> 16) & 0xFF;
        float g = (color >> 8) & 0xFF;
        float b = color & 0xFF;
        return new Vector3(r / 0xFF, g / 0xFF, b / 0xFF);
    }

    public static string FormatScore(int score, int c)
    {
        string str = string.Join("", $"{(score == 0 ? "" : score)}{c}".Reverse());
        int spacing = ((str.Length + 2) / 3 * 3) - str.Length;
        str = str.PadRight(spacing + str.Length, 'o');
        return string.Join("",string.Join(".", Enumerable.Range(0, str.Length / 3).Select(x => str[(x*3)..(x*3+3)]))
            .Reverse()).Substring(spacing);
    }

    public static bool IsInArea(Vector2 xPositionTo, Vector2 areaStart, Vector2 areaEnd)
    {
        return
            areaStart.X < xPositionTo.X && areaStart.Y < xPositionTo.Y &&
            areaEnd.X > xPositionTo.X && areaEnd.Y > xPositionTo.Y;
    }

    /// <summary>Pizzics' one primitive: two rects overlap if their centres are closer than their half-widths
    /// added together — i.e. they are treated as circles, which is what every collision in the game wants.</summary>
    public static bool IsCollied(Rect rc1, Rect rc2)
    {
        #if DEBUG
        if (rc1.X > rc2.X)
            (rc2.X, rc1.X) = (rc1.X, rc2.X);
        var vecDistance = MathF.Abs(MathUtil.Vector2Distance(rc1.Center, rc2.Center));
        var wDistance = (rc1.Width + rc2.Width) / 2;
        return vecDistance < wDistance;
        #else
        return MathUtil.Vector2Distance(rc1.Center, rc2.Center) < (rc1.Width + rc2.Width) / 2;
        #endif
    }

    /// <summary>
    /// Pizzics' second primitive, and its only rectangle: does a circle (<paramref name="centre"/>,
    /// <paramref name="radius"/>) touch an axis-aligned box of <paramref name="size"/> centred on
    /// <paramref name="boxCentre"/>? Clamp the circle's centre into the box to find the nearest point, and it
    /// is a hit if that point is within the radius. Used for the few objects whose shape a circle cannot
    /// stand in for — the complaints box on Dmitry's fourth stage-3 card — so a player shot passing a corner
    /// of it neither hits air nor gets swallowed by a circle bigger than the sprite.
    /// </summary>
    public static bool CircleTouchesBox(Vector2 centre, float radius, Vector2 boxCentre, Vector2 size)
    {
        Vector2 half = size / 2;
        Vector2 nearest = new(
            Math.Clamp(centre.X, boxCentre.X - half.X, boxCentre.X + half.X),
            Math.Clamp(centre.Y, boxCentre.Y - half.Y, boxCentre.Y + half.Y));
        return MathUtil.Vector2Distance(centre, nearest) < radius;
    }

    public static readonly Dictionary<string, string> TransliterationDictionary =
        JsonSerializer.Deserialize<Dictionary<string, string>>(Assets.ReadAllText("Assets/Data/cyrilic-transliteration-table.json"))!;
    public static readonly Dictionary<string, string> TranslationDictionary =
        JsonSerializer.Deserialize<Dictionary<string, string>>(Assets.ReadAllText("Assets/Data/translation.json"))!;

    /// <summary>True if translation.json carries an entry for this key.</summary>
    public static bool HasTranslation(string key) => TranslationDictionary.ContainsKey(key);

    public static string Transliterate(string text)
    {
        string final = "";
        string[] chars;
        foreach (var c in text)
        {
            if (TransliterationDictionary.ContainsKey(c.ToString()))
            {
                chars = TransliterationDictionary[c.ToString()].Split(";;");
                final += chars[new Random().Next(chars.Length - 1)];
            }
            else
                final += c;
        }
        return final;
    }
}

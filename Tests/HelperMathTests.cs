using System.Numerics;
using System.Text.Json;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// The pure half of Helper, split out into <see cref="HelperPure"/> — parsing, interpolation, easing, colour
/// packing and Pizzics' one collision primitive. Helper itself is untestable headless (its static constructor
/// allocates an 8192×8192 render texture), so these members live where no GPU is needed; they carry real
/// gameplay meaning (object appear/disappear envelopes, the distance test every bullet collision in the game
/// goes through).
///
/// <see cref="HelperPure"/> still reads translation.json and the transliteration table through the asset seam
/// on first touch — hence <see cref="TestEnvironment.UseRepoAssets"/> here even for the math tests.
/// </summary>
public class HelperMathTests
{
    public HelperMathTests() => TestEnvironment.UseRepoAssets();

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData("640x480", 640, 480)]
    public void Resolution_parses(string text, int width, int height)
    {
        Assert.True(HelperPure.GetResolutionFromString(text, out (int width, int height) res));
        Assert.Equal((width, height), res);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1280")]
    [InlineData("12xax")]
    [InlineData("x720")]
    public void Resolution_rejects_garbage(string text)
    {
        Assert.False(HelperPure.GetResolutionFromString(text, out _));
    }

    [Theory]
    [InlineData("640x480", 1.0)]    // the reference width everything is scaled from
    [InlineData("1280x720", 2.0)]
    public void Multiplyer_is_width_over_the_640_reference(string text, double expected)
    {
        Assert.True(HelperPure.GetMultiplyerFromRes(text, out double multiplyer));
        Assert.Equal(expected, multiplyer, 6);
    }

    [Fact]
    public void Mix_hits_both_endpoints_and_the_midpoint()
    {
        Assert.Equal(10f, HelperPure.Mix(10f, 20f, 0f));
        Assert.Equal(20f, HelperPure.Mix(10f, 20f, 1f));
        Assert.Equal(15f, HelperPure.Mix(10f, 20f, 0.5f));

        Rect a = new(0, 0, 10, 20);
        Rect b = new(100, 200, 30, 40);
        Rect mid = HelperPure.Mix(a, b, 0.5f);
        Assert.Equal(50f, mid.X);
        Assert.Equal(100f, mid.Y);
        Assert.Equal(20f, mid.Width);
        Assert.Equal(30f, mid.Height);

        Rgba mixed = HelperPure.Mix(new Rgba(255, 0, 0), new Rgba(0, 0, 255), 0.5f);
        Assert.Equal(new Rgba(127, 0, 127), mixed);   // byte cast truncates the .5
    }

    /// <summary>The appear/disappear envelope every timed object fades by: 0 before <c>start</c>, ramps up
    /// over <c>appearLength</c>, holds at 1, ramps down over <c>disappearLength</c> before <c>end</c>, 0 after.</summary>
    [Fact]
    public void ComputeObjectTime_is_an_appear_hold_disappear_envelope()
    {
        Assert.Equal(0f, HelperPure.ComputeObjectTime(5, 10, 10, 100, 20));     // before start
        Assert.Equal(0.5f, HelperPure.ComputeObjectTime(15, 10, 10, 100, 20)); // mid-appear
        Assert.Equal(1f, HelperPure.ComputeObjectTime(50, 10, 10, 100, 20));   // hold
        Assert.Equal(0.25f, HelperPure.ComputeObjectTime(95, 10, 10, 100, 20));// mid-disappear
        Assert.Equal(0f, HelperPure.ComputeObjectTime(105, 10, 10, 100, 20));   // after end
    }

    [Fact]
    public void ComputeObjectTime0To2_goes_before_during_after()
    {
        Assert.Equal(0f, HelperPure.ComputeObjectTime0To2(5, 10, 10, 100, 20));
        Assert.Equal(1f, HelperPure.ComputeObjectTime0To2(50, 10, 10, 100, 20));
        Assert.Equal(2f, HelperPure.ComputeObjectTime0To2(130, 10, 10, 100, 20));
    }

    [Theory]
    [InlineData(0.0, (byte)0)]
    [InlineData(0.5, (byte)127)]
    [InlineData(1.0, (byte)255)]
    public void TimeToTransparency_scales_to_a_byte(double time, byte expected)
    {
        Assert.Equal(expected, HelperPure.TimeToTransparency(time));
    }

    [Fact]
    public void EaseInOutElastic_pins_its_endpoints()
    {
        Assert.Equal(0f, HelperPure.EaseInOutElasticF(0f));
        Assert.Equal(1f, HelperPure.EaseInOutElasticF(1f));
    }

    [Fact]
    public void Colour_int_packing_round_trips()
    {
        Assert.Equal(0xFF0000, HelperPure.Vector3ColorToInt(Vector3.UnitX));
        Assert.Equal(0x000000, HelperPure.Vector3ColorToInt(Vector3.Zero));

        Vector3 v = HelperPure.ColorIntToVector3(0x804020);
        Assert.Equal(0x80 / 255f, v.X, 4);
        Assert.Equal(0x40 / 255f, v.Y, 4);
        Assert.Equal(0x20 / 255f, v.Z, 4);
        // Back again within the 1/255 quantisation the float form introduces.
        Assert.True(Math.Abs(HelperPure.Vector3ColorToInt(v) - 0x804020) <= 0x010101);
    }

    [Fact]
    public void ColourToVector_normalises_to_unit_floats()
    {
        Vector4 v = HelperPure.ColorToVector(new Rgba(255, 0, 128, 255));
        Assert.Equal(new Vector4(1f, 0f, 128 / 255f, 1f), v);
    }

    /// <summary>Pizzics: two rects collide when their centres are closer than the half-widths summed — a
    /// circle test. Strictly less-than, so exact tangency is NOT a hit.</summary>
    [Fact]
    public void IsCollied_is_a_strict_circle_test()
    {
        Assert.True(HelperPure.IsCollied(new Rect(0, 0, 10, 10), new Rect(6, 0, 10, 10)));
        Assert.False(HelperPure.IsCollied(new Rect(0, 0, 10, 10), new Rect(30, 0, 10, 10)));
        // Centres exactly (w1+w2)/2 apart: tangent, and tangent does not count.
        Assert.False(HelperPure.IsCollied(new Rect(0, 0, 10, 10), new Rect(10, 0, 10, 10)));
    }

    [Fact]
    public void IsInArea_is_strict_on_every_side()
    {
        Vector2 start = new(0, 0), end = new(100, 100);
        Assert.True(HelperPure.IsInArea(new Vector2(50, 50), start, end));
        Assert.False(HelperPure.IsInArea(new Vector2(0, 50), start, end));    // on the border is outside
        Assert.False(HelperPure.IsInArea(new Vector2(150, 50), start, end));
    }

    /// <summary>
    /// FormatScore appends the trailing counter digit, then dot-groups into threes. The expected strings are
    /// what the function produces today — this pins the grouping (and the 'o' padding quirk at zero) rather
    /// than any prettier formatting one might wish it had.
    /// </summary>
    [Theory]
    [InlineData(0, 0, "0")]
    [InlineData(12345, 0, "123.450")]
    [InlineData(1234567, 5, "12.345.675")]
    public void FormatScore_groups_digits_in_threes(int score, int c, string expected)
    {
        Assert.Equal(expected, HelperPure.FormatScore(score, c));
    }

    [Fact]
    public void Transliterate_leaves_text_with_no_table_entries_alone()
    {
        Assert.Equal("abc 123 !?", HelperPure.Transliterate("abc 123 !?"));
    }

    [Fact]
    public void Transliterate_maps_cyrillic_through_the_shipped_table()
    {
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Assets.Resolve("Assets/Data/cyrilic-transliteration-table.json")))!;

        const string source = "Никита";
        string result = HelperPure.Transliterate(source);
        Assert.Equal(source.Length, result.Length);
        for (int i = 0; i < source.Length; i++)
        {
            string key = source[i].ToString();
            if (!table.TryGetValue(key, out string? variants))
                continue;   // untransliterated characters pass through untouched
            Assert.Contains(result[i].ToString(), variants.Split(";;"));
        }
    }

    [Fact]
    public void HasTranslation_agrees_with_the_shipped_translation_file()
    {
        var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Assets.Resolve("Assets/Data/translation.json")))!;
        string someKey = translations.Keys.First();

        Assert.True(HelperPure.HasTranslation(someKey));
        Assert.False(HelperPure.HasTranslation("definitely.not.a.real.key.12345"));
    }
}

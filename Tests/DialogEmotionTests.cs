using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// Dialog emotions: every authored line carries one, the on-demand symbol font is in the tree, and each
/// authored symbol actually bakes through <see cref="EmotionGlyph"/> — the rasteriser and the dressing are
/// CPU-side (StbTrueType into a <see cref="CpuImage"/>), so the whole bake is checked here with no GPU. A
/// symbol the font lacks comes back null from <see cref="EmotionGlyph.Render"/> and would be silently absent
/// in the game, which is exactly what this catches.
/// </summary>
public class DialogEmotionTests
{
    public DialogEmotionTests() => TestEnvironment.UseRepoAssets();

    private static IEnumerable<(string File, string Chapter, int Index, FileDialogInfo Line)> Lines()
    {
        foreach (string path in Assets.Files("Assets/Data/SpellCards", "*.sid"))
        {
            BitPackage package = BitPackage.OpenStreamReadPackage(path);
            FileStageInfo stage = FileStageInfo.Load(ref package);
            package.Dispose();
            foreach (FileChapterInfo chapter in stage.Chapters)
                for (int i = 0; i < chapter.Dialogs.Length; i++)
                    yield return (Path.GetFileName(path), chapter.Id, i, chapter.Dialogs[i]);
        }
    }

    [Fact]
    public void Symbol_font_is_shipped()
    {
        Assert.True(Assets.Exists(EmotionGlyph.FontPath), $"missing {EmotionGlyph.FontPath}");
    }

    [Fact]
    public void Every_dialog_line_has_an_emotion()
    {
        var missing = Lines().Where(l => string.IsNullOrEmpty(l.Line.Emotion))
            .Select(l => $"{l.File}:{l.Chapter}[{l.Index}]").ToList();
        Assert.True(missing.Count == 0, "Dialog lines without an emotion: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_authored_emotion_bakes_from_the_font()
    {
        byte[]? ttf = EmotionGlyph.ReadFont();
        Assert.NotNull(ttf);
        var failed = new List<string>();
        foreach ((string file, string chapter, int index, FileDialogInfo line) in Lines())
        {
            if (string.IsNullOrEmpty(line.Emotion))
                continue;
            CpuImage? image = EmotionGlyph.Render(ttf!, line.Emotion, 48, new Random(index));
            if (image == null || !image.UsesAlpha() || image.Width < 8 || image.Height < 8)
                failed.Add($"{file}:{chapter}[{index}] '{line.Emotion}'");
        }
        Assert.True(failed.Count == 0, "Emotions the font could not bake: " + string.Join(", ", failed));
    }

    /// <summary>The bake is deterministic for a seed and never leaves an outline pixel without a fill or
    /// shadow behind it — a fully transparent image means the glyph was lost in the dressing.</summary>
    [Fact]
    public void Bake_has_opaque_pixels_and_repeats_for_a_seed()
    {
        byte[]? ttf = EmotionGlyph.ReadFont();
        Assert.NotNull(ttf);
        CpuImage a = EmotionGlyph.Render(ttf!, "☠", 64, new Random(7))!;
        CpuImage b = EmotionGlyph.Render(ttf!, "☠", 64, new Random(7))!;
        Assert.Equal(a.Pixels, b.Pixels);
        int opaque = 0;
        for (int i = 3; i < a.Pixels.Length; i += 4)
            if (a.Pixels[i] == 255)
                opaque++;
        Assert.True(opaque > 100, $"only {opaque} opaque pixels in a 64px skull");
    }
}

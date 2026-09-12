namespace NEngineFormat.StaticIllustration;

/// <summary>One way a picture was encoded, and what it came to.</summary>
/// <param name="Name">What the settings were reaching for, for reporting.</param>
/// <param name="Options">The settings used.</param>
/// <param name="Asset">The picture as those settings encoded it.</param>
/// <param name="Size">
/// The bytes the file would occupy, written exactly as it will be - compressed or not.
/// </param>
public sealed record EncoderTrial(string Name, EncoderOptions Options, StaticIllustrationAsset Asset, long Size);

/// <summary>
/// Encodes a picture several ways and keeps whichever comes out smallest as a file.
/// </summary>
/// <remarks>
/// <para>
/// Every choice this encoder makes is measured, but it is measured on the blocks, which is the
/// only size it can see while it is making the choice. What the file finally occupies is decided
/// afterwards by LZMA2, and the two do not always agree: a shape, a difference against a distant
/// tile or a narrower palette can shrink the blocks and grow the file, because what it removed
/// was repetition the compressor was flattening for free.
/// </para>
/// <para>
/// A margin is how that has been settled until now - a guess at how much better a thing must
/// measure before it is worth the disturbance, set from what a handful of pictures did. This
/// settles it instead: encode the picture both ways, compress both, keep the smaller. It costs
/// an encode, which runs alongside the first rather than after it, and it answers for the
/// picture in hand rather than for the pictures the margin was tuned on.
/// </para>
/// </remarks>
public static class EncoderTrials
{
    /// <summary>The name the settings as given are reported under.</summary>
    public const string AsSet = "as set";

    /// <summary>The name the shapes-first alternative is reported under.</summary>
    public const string ShapesFirst = "shapes first";

    /// <summary>
    /// Encodes a picture the settings' way and every alternative worth trying, smallest last to
    /// find rather than first.
    /// </summary>
    public static IReadOnlyList<EncoderTrial> Run(
        byte[] rgba,
        uint width,
        uint height,
        uint tileWidth = 64,
        uint tileHeight = 64,
        EncoderOptions? options = null,
        bool? compress = null)
    {
        EncoderOptions baseline = options ?? new EncoderOptions();

        var settings = new List<(string Name, EncoderOptions Options)> { (AsSet, baseline) };
        settings.AddRange(Alternatives(baseline));

        return Run(rgba, width, height, tileWidth, tileHeight, settings, compress);
    }

    /// <summary>Encodes a picture with each of the given settings and measures every one.</summary>
    /// <exception cref="ArgumentException">No settings were given.</exception>
    public static IReadOnlyList<EncoderTrial> Run(
        byte[] rgba,
        uint width,
        uint height,
        uint tileWidth,
        uint tileHeight,
        IReadOnlyList<(string Name, EncoderOptions Options)> settings,
        bool? compress = null)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Count == 0)
        {
            throw new ArgumentException("There is nothing to encode with.", nameof(settings));
        }

        var assets = new StaticIllustrationAsset[settings.Count];

        // The encodes have nothing in common but the pixels they read, so they run together and
        // a second way of writing the picture costs waiting rather than time.
        Parallel.For(0, settings.Count, i =>
            assets[i] = IllustrationEncoder.Encode(rgba, width, height, tileWidth, tileHeight, settings[i].Options));

        var trials = new EncoderTrial[settings.Count];
        for (int i = 0; i < trials.Length; i++)
        {
            // Measured the way the file will be written: with compression off, what matters is
            // the blocks, and the answer is not always the same one.
            if (compress is bool wanted)
            {
                assets[i].CompressBlocks = wanted;
            }

            // Compressed one after another rather than together: this is where the picture meets
            // LZMA2, and it is the one part of the work that is not ours to run twice at once.
            trials[i] = new EncoderTrial(
                settings[i].Name, settings[i].Options, assets[i], assets[i].ToBytes().LongLength);
        }

        return trials;
    }

    /// <summary>The smallest of several encodings.</summary>
    /// <remarks>
    /// Ties go to the earliest, which is the settings as they were given: where two ways of
    /// writing a picture come to the same size, the one that was asked for wins.
    /// </remarks>
    public static EncoderTrial Smallest(IReadOnlyList<EncoderTrial> trials)
    {
        ArgumentNullException.ThrowIfNull(trials);

        if (trials.Count == 0)
        {
            throw new ArgumentException("There are no encodings to choose between.", nameof(trials));
        }

        EncoderTrial best = trials[0];
        foreach (EncoderTrial trial in trials)
        {
            if (trial.Size < best.Size)
            {
                best = trial;
            }
        }

        return best;
    }

    /// <summary>Encodes a picture every way worth trying and returns the smallest.</summary>
    public static StaticIllustrationAsset Encode(
        byte[] rgba,
        uint width,
        uint height,
        uint tileWidth = 64,
        uint tileHeight = 64,
        EncoderOptions? options = null,
        bool? compress = null) =>
        Smallest(Run(rgba, width, height, tileWidth, tileHeight, options, compress)).Asset;

    /// <summary>
    /// The other ways of writing a picture worth measuring against the settings as given.
    /// </summary>
    /// <remarks>
    /// One for now: the settings with shapes taken wherever they measure smaller at all, rather
    /// than only where they measure much smaller. That is the choice the margins cannot make
    /// well, because which way it falls is not a property of the settings but of the picture -
    /// a drawing of panels and rules gains by it, a photograph loses. Another either-or belongs
    /// here rather than in a new mechanism the moment one is found.
    /// </remarks>
    public static IEnumerable<(string Name, EncoderOptions Options)> Alternatives(EncoderOptions baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        // With shapes off, or already taken at any saving, this would encode the same picture
        // the same way and measure it twice.
        if (baseline.Brushes && baseline.BrushMargin > 0f)
        {
            EncoderOptions shapes = baseline.Clone();
            shapes.BrushMargin = 0f;
            yield return (ShapesFirst, shapes);
        }
    }
}

namespace NEngineFormat.StaticIllustration;

/// <summary>
/// The thresholds that decide what the encoder reaches for: when a colour is common enough to
/// mask out, when a handful of colours are worth a palette, and when an image-wide mask runs
/// flat enough to be worth storing.
/// </summary>
/// <remarks>
/// These were fixed constants. They are settings because the right values depend on the
/// picture, and the only honest way to find them for a given kind of art is to move them and
/// watch the file size.
/// </remarks>
public sealed class EncoderOptions
{
    /// <summary>
    /// How much of a block one colour must cover before masking it out earns the mask bits.
    /// </summary>
    public float DominantColorThreshold { get; set; } = 0.15f;

    /// <summary>
    /// How much of a block a palette of each size must cover to be worth its index width,
    /// widest first.
    /// </summary>
    /// <remarks>
    /// Larger palettes need wider indices, so each size has to earn its width: fifteen colours
    /// cost four bits a pixel and must cover half the block, while three cost two bits and need
    /// only a fifth.
    /// </remarks>
    public (int Colors, float Coverage)[] PaletteThresholds { get; set; } =
    [
        (15, 0.50f),
        (7, 0.25f),
        (3, 0.20f),
    ];

    /// <summary>
    /// How much cheaper a narrower palette must be before it is taken over a wider one, as a
    /// share of its own cost: one means it must be twice as cheap, zero means any saving will do.
    /// </summary>
    /// <remarks>
    /// Measured on its own, a narrower palette usually wins - fewer bits an index, and the
    /// colours it drops were rare. Measured across the picture it often loses, because a wider
    /// palette is likelier to match what the tile beside it chose, and tiles sharing a palette
    /// share its block. On a drawing of a few flat colours, taking every narrow palette that
    /// was cheaper produced sixty per cent more palette blocks and a file seven per cent
    /// larger, even though every tile in it had shrunk. Lower this when the blocks matter more
    /// than the compressed file.
    /// </remarks>
    public float PaletteSizeMargin { get; set; } = 1.0f;

    /// <summary>
    /// How much of a tile may differ from a neighbouring one before storing it as a difference
    /// stops being worth considering.
    /// </summary>
    /// <remarks>
    /// A difference costs a bit for every pixel of the tile plus an entry for each pixel that
    /// actually differs, so it can still pay until the differing share passes
    /// <c>1 - 1/bitsPerPixel</c> - which for the widths this format reaches is around nine
    /// tenths. Set there, the threshold rules out only what could not pay at any bit depth, and
    /// cost decides the rest.
    /// </remarks>
    public float MaskedDifferenceThreshold { get; set; } = 0.90f;

    /// <summary>
    /// How many earlier tiles, beyond the ones touching it, a tile may be measured against
    /// when looking for one to store it as a difference from. Zero looks no further than the
    /// neighbours.
    /// </summary>
    /// <remarks>
    /// Comparing every tile with every earlier tile is square work, and on a large picture most
    /// of it finds nothing. Instead each tile is summarised by a coarse grid of its average
    /// values, and only tiles whose summaries agree are measured properly - so what this bounds
    /// is how many of those the scan may follow up, not how many it looks at.
    /// </remarks>
    public int MaskedScanCandidates { get; set; } = 16;

    /// <summary>
    /// How much cheaper a difference against anything but a straight neighbour must be, as a
    /// share of its own cost: zero takes any saving, one asks it to be twice as cheap.
    /// </summary>
    /// <remarks>
    /// Measured a block at a time, a difference against a tile across the picture is as good as
    /// one against the tile alongside. Measured on the file it is not: the compressor behind
    /// this format works on what it has lately seen, and a block whose entries were fitted to a
    /// tile far away breaks the run of near-identical blocks it was doing well on. So a distant
    /// or turned target has to be worth the disturbance rather than merely equal to it.
    /// </remarks>
    public float MaskedScanMargin { get; set; } = 0.25f;

    /// <summary>
    /// Whether a difference may read the tile it copies from turned or mirrored rather than
    /// straight through.
    /// </summary>
    /// <remarks>
    /// A drawing repeats itself reflected far more often than it repeats itself exactly. It
    /// costs nothing to store - the orientation rides in bits the flags already had free - but
    /// it multiplies the summaries the scan looks up by eight, so it can be turned off where
    /// encoding time matters more than the file.
    /// </remarks>
    public bool MaskedTransforms { get; set; } = true;

    /// <summary>
    /// Whether the picture may describe the colour values no pixel of it uses, so that profiles
    /// can number around the gaps.
    /// </summary>
    /// <remarks>
    /// The table is written once for the whole file and every profile that counts in it says so,
    /// which is why a picture of several layers turns it off: each layer is fitted on its own,
    /// and two tables cannot both be the one the file holds.
    /// </remarks>
    public bool ColorSkips { get; set; } = true;

    /// <summary>
    /// Whether a tile may paint shapes into itself rather than spelling them out pixel by pixel.
    /// </summary>
    /// <remarks>
    /// A drawing is full of shapes a pixel format has to say one pixel at a time: the box behind
    /// a panel, the stroke of a rule, the wedge of a highlight, a ramp of light across a wall. A
    /// brush says the shape once and the pixels it covers cost nothing. Only shapes that come
    /// out exactly right are kept, so this never costs accuracy - only encoding time, which is
    /// why it can be turned off.
    /// </remarks>
    public bool Brushes { get; set; } = true;

    /// <summary>
    /// How much smaller a tile that paints shapes must be than the same tile written plainly,
    /// as a share of its own size: zero takes any saving, one asks it to be twice as small.
    /// </summary>
    /// <remarks>
    /// A shape trades many small repetitive numbers for a few large varied ones. Measured on the
    /// blocks that is a clear win - a picture of panels and rules loses a seventh of its bulk -
    /// but the mask bits and entries it removed were the very stretches the compressor behind
    /// this format was flattening for free, while the coordinates it leaves behind flatten badly.
    /// So a shape is asked to be worth more than the run it breaks up: at one it must halve the
    /// tile, which keeps the panels and the ramps and drops the small boxes that were only just
    /// ahead. Measured on a drawing of panels at tile 16, moving it from one to zero takes the
    /// blocks from 24,262 to 21,297 bytes and the compressed file from 10,194 to 10,990 - so set
    /// it low when the blocks are what matter, which is what turning compression off means.
    /// </remarks>
    public float BrushMargin { get; set; } = 1.0f;

    /// <summary>Most shapes one tile may paint into itself.</summary>
    /// <remarks>
    /// Each shape is fitted against what the ones before it left, so this bounds both the work
    /// and how far a tile can be broken into pieces before writing it out plainly is better.
    /// </remarks>
    public int BrushesPerTile { get; set; } = 4;

    /// <summary>How many colours of a tile are offered a shape, commonest first.</summary>
    public int BrushColorsPerTile { get; set; } = 3;

    /// <summary>
    /// How many pixels a shape must settle before it is worth measuring at all.
    /// </summary>
    /// <remarks>
    /// A shape costs six to ten bytes, and the pixels it settles cost a few bits each, so a
    /// shape over a handful of pixels cannot pay however it is written. Cost decides the rest.
    /// </remarks>
    public int BrushMinimumPixels { get; set; } = 8;

    /// <summary>
    /// How many pixels an image-mask run must cover on average before the mask is worth
    /// storing at all.
    /// </summary>
    /// <remarks>
    /// The image mask is run-length coded, so it is cheap over flat stretches and dear over
    /// broken ones. Requiring a decent average run is what separates the two.
    /// </remarks>
    public int MinimumAverageMaskRun { get; set; } = 8;

    /// <summary>A copy, so a caller can change one without disturbing another.</summary>
    public EncoderOptions Clone() => new()
    {
        DominantColorThreshold = DominantColorThreshold,
        PaletteThresholds = [.. PaletteThresholds],
        MaskedDifferenceThreshold = MaskedDifferenceThreshold,
        MaskedScanCandidates = MaskedScanCandidates,
        MaskedScanMargin = MaskedScanMargin,
        MaskedTransforms = MaskedTransforms,
        PaletteSizeMargin = PaletteSizeMargin,
        MinimumAverageMaskRun = MinimumAverageMaskRun,
        ColorSkips = ColorSkips,
        Brushes = Brushes,
        BrushMargin = BrushMargin,
        BrushesPerTile = BrushesPerTile,
        BrushColorsPerTile = BrushColorsPerTile,
        BrushMinimumPixels = BrushMinimumPixels,
    };
}

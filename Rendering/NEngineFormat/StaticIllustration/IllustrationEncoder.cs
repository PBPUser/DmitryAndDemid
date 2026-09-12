using NEngineFormat.Core;
using NEngineFormat.Core.Utils;
using NEngineFormat.StaticIllustration.Blocks;
using NEngineFormat.StaticIllustration.Data;

namespace NEngineFormat.StaticIllustration;

/// <summary>
/// Turns an RGBA8 pixel buffer into an asset, the inverse of <see cref="IllustrationDecoder"/>.
/// </summary>
/// <remarks>
/// <para>
/// Compression comes from three places. A channel whose value never changes is stored once in
/// the profile and costs nothing per pixel. The remaining channels store only their distance
/// above a per-channel minimum. And a colour common enough to be worth it becomes that block's
/// out-of-mask colour, dropping its pixels from the colour list entirely.
/// </para>
/// <para>
/// That last one is measured rather than assumed: every block is scanned into a colour
/// histogram first, and the mask is only used when one colour covers more than
/// <see cref="DominantColorThreshold"/> of the block. Masking a colour that appears twice would
/// cost a mask bit on every pixel to save two entries.
/// </para>
/// <para>
/// A tile that repeats an earlier one, exactly or nearly, is stored as an
/// <see cref="IllustrationLinkBlock"/> pointing at it. A link replaces only the fields that
/// actually differ, so two tiles alike but for their background colour cost one block and a
/// three-byte link. The choice is made on measured bytes: a link is only used when it comes
/// out smaller than writing the tile in full.
/// </para>
/// <para>
/// A profile is measured <b>per tile</b>, because a tile covering one corner of a picture
/// usually spans a much narrower range than the picture as a whole. Tiles are then merged onto
/// shared profiles, but only where the merge actually pays: the bytes saved by dropping a
/// profile block must exceed the extra colour entries the widened profile forces on every tile
/// that shares it. See <see cref="GroupCost"/>.
/// </para>
/// <para>
/// Encoding is lossless for 8-bit RGBA: <c>Decode(Encode(pixels))</c> returns the original bytes.
/// </para>
/// </remarks>
public static class IllustrationEncoder
{
    /// <summary>
    /// How much of a block one colour must cover before masking it out earns the mask bits.
    /// The default for <see cref="EncoderOptions.DominantColorThreshold"/>.
    /// </summary>
    public const float DominantColorThreshold = 0.15f;

    /// <summary>
    /// How many pixels an image-mask run must cover on average before the mask is worth
    /// storing at all.
    /// </summary>
    /// <remarks>
    /// The image mask is run-length coded, so it is cheap over flat stretches and dear over
    /// broken ones: a picture that alternates every few pixels produces nearly a run per pixel
    /// and costs more than leaving the work to the tiles, which can at least repeat themselves.
    /// Requiring a decent average run is what separates the two.
    /// </remarks>
    public const int MinimumAverageMaskRun = 8;

    /// <summary>
    /// When a handful of colours cover enough of a block, they go in a palette and the mask
    /// stores an index per pixel instead of a single in-or-out bit.
    /// </summary>
    /// <remarks>
    /// Larger palettes need wider indices, so each size has to earn its width: 15 colours cost
    /// four bits a pixel and must cover half the block, while three cost two bits and need only
    /// a fifth. Ordered widest first, since a wider palette that qualifies captures more.
    /// <para>
    /// Meeting a threshold makes a palette <b>eligible</b>, not automatic. A block that is
    /// almost entirely one colour meets every threshold, yet a plain one-bit mask serves it far
    /// better than a four-bit index; the cheaper of the two is what gets used.
    /// </para>
    /// </remarks>
    public static readonly (int Colors, float Coverage)[] PaletteThresholds =
    [
        (15, 0.50f),
        (7, 0.25f),
        (3, 0.20f),
    ];

    private static readonly uint[] ChannelFlags =
    [
        ColorProfileBlock.RedNonStaticFlag,
        ColorProfileBlock.GreenNonStaticFlag,
        ColorProfileBlock.BlueNonStaticFlag,
        ColorProfileBlock.AlphaNonStaticFlag,
    ];

    /// <summary>What a scan of one block's pixels found.</summary>
    /// <param name="DominantColor">The colour appearing most often, packed red-first.</param>
    /// <param name="DominantCount">How many pixels hold it.</param>
    /// <param name="TotalPixels">Pixels scanned, ignoring any that fall outside the image.</param>
    /// <param name="DistinctColors">How many different colours the block uses.</param>
    public readonly record struct ColorStatistics(
        uint DominantColor,
        int DominantCount,
        int TotalPixels,
        int DistinctColors)
    {
        /// <summary>The share of the block its most common colour covers, 0 to 1.</summary>
        public float DominantShare => TotalPixels == 0 ? 0f : (float)DominantCount / TotalPixels;

        /// <summary>
        /// Whether that colour is common enough that masking it out saves more than the mask costs.
        /// </summary>
        public bool WorthMasking => DominantShare > DominantColorThreshold;
    }

    /// <summary>Pixels per run, which is how flat the mask turned out to be.</summary>
    private static double AverageRunLength(ImageMaskBlock mask) =>
        mask.RunLengths.Length == 0 ? 0 : (double)mask.PixelCount / mask.RunLengths.Length;

    /// <summary>The colour histogram of a whole image.</summary>
    private static Dictionary<uint, int> ImageHistogram(byte[] rgba)
    {
        var counts = new Dictionary<uint, int>();
        for (long i = 0; i < rgba.LongLength; i += IllustrationDecoder.BytesPerPixel)
        {
            uint packed = Pack(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
            counts[packed] = counts.GetValueOrDefault(packed) + 1;
        }

        return counts;
    }

    /// <summary>
    /// Walks the image in row-major order and records runs of the same palette index, escaping
    /// the stretches the palette cannot name.
    /// </summary>
    /// <summary>Whichever of the two forms writes smaller, measured as they would be written.</summary>
    private static ImageMaskBlock Cheaper(ImageMaskBlock runs, ImageMaskBlock rectangles, MaskPalleteBlock palette)
    {
        // The runs are only packed at save time, so they are packed here too - an unpacked mask
        // would measure as nothing at all and win every time.
        runs.RunLengthBits = ImageMaskBlock.OptimalRunBits(runs.RunLengths, palette.IndexBits);
        (runs.PackedRuns, runs.RunCount) = ImageMaskBlock.PackRuns(
            runs.RunLengths, runs.RunIndices, runs.RunLengthBits, palette.IndexBits);

        return MeasureBlock(rectangles) < MeasureBlock(runs) ? rectangles : runs;
    }

    /// <summary>
    /// The same claim as the runs, covered by rectangles instead.
    /// </summary>
    /// <remarks>
    /// Greedy, and deliberately simple: take the first claimed pixel not yet covered, stretch
    /// right as far as its colour holds, then down as long as every column of that width still
    /// matches, and write the rectangle out. Worst case each rectangle is one pixel and the form
    /// loses on cost, which is exactly what the comparison is there to catch; best case a flat
    /// background becomes one rectangle where the runs needed one per row.
    /// </remarks>
    private static ImageMaskBlock BuildRectangleMask(
        byte[] rgba,
        uint width,
        uint height,
        MaskPalleteBlock palette)
    {
        var slots = new Dictionary<uint, uint>();
        for (int i = 0; i < palette.Colors.Length; i++)
        {
            slots[palette.Colors[i]] = (uint)i;
        }

        // The palette index of every pixel, or the escape where the palette does not name it.
        var claimed = new uint[(long)width * height];
        for (long i = 0; i < claimed.LongLength; i++)
        {
            long offset = i * IllustrationDecoder.BytesPerPixel;
            uint packed = Pack(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
            claimed[i] = slots.TryGetValue(packed, out uint slot) ? slot : palette.EscapeIndex;
        }

        var covered = new bool[claimed.LongLength];
        var rectangles = new List<(uint Index, uint X, uint Y, uint Width, uint Height)>();

        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                long at = ((long)y * width) + x;
                uint index = claimed[at];

                if (index >= palette.EscapeIndex || covered[at])
                {
                    continue;
                }

                uint spanWidth = 0;
                while (x + spanWidth < width
                    && !covered[at + spanWidth]
                    && claimed[at + spanWidth] == index)
                {
                    spanWidth++;
                }

                uint spanHeight = 1;
                while (y + spanHeight < height && RowMatches(y + spanHeight))
                {
                    spanHeight++;
                }

                for (uint row = y; row < y + spanHeight; row++)
                {
                    for (uint column = x; column < x + spanWidth; column++)
                    {
                        covered[((long)row * width) + column] = true;
                    }
                }

                rectangles.Add((index, x, y, spanWidth, spanHeight));

                bool RowMatches(uint row)
                {
                    long start = ((long)row * width) + x;
                    for (uint column = 0; column < spanWidth; column++)
                    {
                        if (covered[start + column] || claimed[start + column] != index)
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }
        }

        return new ImageMaskBlock
        {
            MaskPaletteId = palette.PaletteId,
            RunLengthBits = 0,
            Rectangles = [.. rectangles],
        };
    }

    private static ImageMaskBlock BuildImageMask(byte[] rgba, uint width, uint height, MaskPalleteBlock palette)
    {
        var slots = new Dictionary<uint, uint>();
        for (int i = 0; i < palette.Colors.Length; i++)
        {
            slots[palette.Colors[i]] = (uint)i;
        }

        var lengths = new List<uint>();
        var indices = new List<uint>();

        long pixels = (long)width * height;
        uint current = 0;
        uint run = 0;

        for (long i = 0; i < pixels; i++)
        {
            long offset = i * IllustrationDecoder.BytesPerPixel;
            uint packed = Pack(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
            uint index = slots.TryGetValue(packed, out uint slot) ? slot : palette.EscapeIndex;

            if (run > 0 && index == current)
            {
                run++;
                continue;
            }

            if (run > 0)
            {
                lengths.Add(run);
                indices.Add(current);
            }

            current = index;
            run = 1;
        }

        if (run > 0)
        {
            lengths.Add(run);
            indices.Add(current);
        }

        return new ImageMaskBlock
        {
            MaskPaletteId = palette.PaletteId,
            RunLengths = [.. lengths],
            RunIndices = [.. indices],
        };
    }

    /// <summary>Scans one tile's pixels into a colour histogram.</summary>
    public static ColorStatistics AnalyseTile(byte[] rgba, Header header, uint tileX, uint tileY)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentNullException.ThrowIfNull(header);

        var counts = new Dictionary<uint, int>();
        int total = 0;

        for (uint y = 0; y < header.BlockHeight; y++)
        {
            for (uint x = 0; x < header.BlockWidth; x++)
            {
                uint imageX = (tileX * header.BlockWidth) + x;
                uint imageY = (tileY * header.BlockHeight) + y;

                if (imageX >= header.ImageWidth || imageY >= header.ImageHeight)
                {
                    continue;
                }

                long offset = ((long)imageY * header.ImageWidth + imageX) * IllustrationDecoder.BytesPerPixel;
                uint packed = Pack(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
                counts[packed] = counts.GetValueOrDefault(packed) + 1;
                total++;
            }
        }

        uint dominant = 0;
        int best = 0;
        foreach ((uint color, int count) in counts)
        {
            if (count > best)
            {
                dominant = color;
                best = count;
            }
        }

        return new ColorStatistics(dominant, best, total, counts.Count);
    }

    /// <summary>
    /// Encodes an RGBA8 buffer into an asset with one illustration block per tile.
    /// </summary>
    /// <param name="rgba">Pixels, four bytes each, row-major from the top left.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="tileWidth">Illustration tile width; the last column may be partial.</param>
    /// <param name="tileHeight">Illustration tile height; the last row may be partial.</param>
    /// <param name="options">
    /// The thresholds to encode by. Omitted, the defaults are used.
    /// </param>
    /// <exception cref="ArgumentException">The buffer is not <c>width * height * 4</c> bytes.</exception>
    public static StaticIllustrationAsset Encode(
        byte[] rgba,
        uint width,
        uint height,
        uint tileWidth = 64,
        uint tileHeight = 64,
        EncoderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        options ??= new EncoderOptions();

        long expected = (long)width * height * IllustrationDecoder.BytesPerPixel;
        if (rgba.LongLength != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} bytes for a {width}x{height} image, got {rgba.LongLength}.", nameof(rgba));
        }

        var asset = new StaticIllustrationAsset();
        asset.Header.ImageWidth = width;
        asset.Header.ImageHeight = height;
        asset.Header.BlockWidth = Math.Max(1, tileWidth);
        asset.Header.BlockHeight = Math.Max(1, tileHeight);

        if (width == 0 || height == 0)
        {
            asset.Header.BlockCount = 0;
            return asset;
        }

        // The picture is analysed as a whole first, on the same terms as a tile: colours common
        // enough across all of it are lifted into one image-wide mask, and the tiles below never
        // see those pixels at all.
        // The picture is analysed as a whole on the same terms as a tile, and the mask it would
        // produce is built before anything commits to it: a mask is only worth storing if it
        // runs in long stretches.
        uint[] imagePalette = ChoosePalette(ImageHistogram(rgba), (int)(width * height), options);
        ImageMaskBlock? imageMask = null;

        if (imagePalette.Length > 0)
        {
            var candidate = new MaskPalleteBlock { PaletteId = 1, Colors = imagePalette };
            ImageMaskBlock runs = BuildImageMask(rgba, width, height, candidate);

            if (AverageRunLength(runs) >= options.MinimumAverageMaskRun)
            {
                // The same claim written two ways. Runs follow the rows; rectangles follow the
                // shapes, and one rectangle stands in for as many runs as it covers rows - so
                // which is smaller is a question about the picture, and it is asked rather than
                // assumed.
                imageMask = Cheaper(runs, BuildRectangleMask(rgba, width, height, candidate), candidate);
            }
            else
            {
                imagePalette = [];
            }
        }

        HashSet<uint> claimed = [.. imagePalette];

        // Measure every tile first: which pixels it covers, and the range each channel spans
        // across them. Nothing is written until the profiles have been settled.
        var tiles = new List<TileScan>();
        for (uint tileY = 0; tileY < asset.Header.TilesY; tileY++)
        {
            for (uint tileX = 0; tileX < asset.Header.TilesX; tileX++)
            {
                tiles.Add(ScanTile(rgba, asset.Header, tileX, tileY, claimed, options));
            }
        }

        // A palette only earns its own block when the tiles sharing it save more between them
        // than the block costs, which is a question about the group rather than any one tile.
        DecidePalettes(tiles);

        // Which channel values go unused, and over what part of the picture to say so. A
        // division only pays where the regions differ, so every division is measured and the
        // cheapest wins - which is often no division, and sometimes none at all.
        ColorSkipsBlock? skips = ChooseColorSkips(rgba, asset.Header, tiles, options);
        if (skips is not null)
        {
            asset.Blocks.Add(skips);

            // Regions describing the same values number them the same way, so their tiles can
            // still share a profile. Only regions that really differ split the profiles up,
            // which is what keeps a division from costing more in profiles than it saves in
            // width.
            int[] byTable = TableGroups(skips);

            for (int i = 0; i < tiles.Count; i++)
            {
                tiles[i].Region = skips.RegionOf(
                    (uint)(i % (int)asset.Header.TilesX),
                    (uint)(i / (int)asset.Header.TilesX),
                    asset.Header.TilesX,
                    asset.Header.TilesY);

                tiles[i].Numbering = byTable[tiles[i].Region];
            }
        }

        foreach (TileScan tile in tiles)
        {
            tile.Finalise(rgba, skips);
        }

        List<ProfileGroup> groups = MergeProfiles(tiles);

        // A profile only the covered tiles named is information about blocks that are not
        // stored, so it is dropped and the rest renumbered.
        groups = [.. groups.Where(group => group.Tiles.Any(tile => !tile.FullyCovered))];
        for (int i = 0; i < groups.Count; i++)
        {
            foreach (TileScan tile in groups[i].Tiles)
            {
                tile.GroupIndex = i;
            }
        }

        for (int i = 0; i < groups.Count; i++)
        {
            asset.Blocks.Add(groups[i].Range.ToProfile(
                (uint)(i + 1), groups[i].CoveredPixels, skips is not null));
        }

        // Palettes are shared the same way profiles are: identical ones become a single block.
        var paletteIds = new Dictionary<uint[], uint>(SequenceComparer<uint>.Instance);

        if (imageMask is not null)
        {
            paletteIds[imagePalette] = 1;
            asset.Blocks.Add(new MaskPalleteBlock { PaletteId = 1, Colors = imagePalette });
            asset.Blocks.Add(imageMask);
        }

        foreach (TileScan tile in tiles)
        {
            if (tile.Palette.Length > 0 && !paletteIds.ContainsKey(tile.Palette))
            {
                uint paletteId = (uint)(paletteIds.Count + 1);
                paletteIds[tile.Palette] = paletteId;
                asset.Blocks.Add(new MaskPalleteBlock { PaletteId = paletteId, Colors = tile.Palette });
            }
        }

        ColorProfileBlock[] profiles = [.. asset.Profiles];

        // Tiles that repeat an earlier one become links. Candidates are found by the two
        // fields that dominate a block's size - the mask and the colour entries - so a tile
        // sharing either can be reached without comparing it against every tile before it.
        // Delta blocks measure their differences against profiles of their own, numbered after
        // the ones the tiles share; two tiles that shift the same way reuse one.
        var deltaProfiles = new DeltaProfiles((uint)groups.Count + 1);

        // Which earlier tiles a tile could be stored as a difference from, found by a coarse
        // summary of each rather than by comparing every pair.
        var similar = new TileSimilarity(rgba, asset.Header, options, tiles.Count);

        var written = new Dictionary<int, IllustrationBlock>();
        var byContent = new Dictionary<IllustrationBlock, int>(TileContentComparer.Instance);
        var byMask = new Dictionary<byte[], int>(SequenceComparer<byte>.Instance);
        var byColors = new Dictionary<uint[], int>(SequenceComparer<uint>.Instance);

        for (int slot = 0; slot < tiles.Count; slot++)
        {
            // A difference may only point backwards, so a tile joins the scan once the tile
            // after it has begun - covered tiles included, since the mask has drawn them by then
            // and their pixels are as good to copy as any.
            if (slot > 0)
            {
                similar.Add(slot - 1);
            }

            // The image mask painted every pixel of this tile, so there is nothing left to say
            // about it. Storing an empty block, or even a link to one, would be storing that
            // nothing; the reader works the same tiles out from the mask.
            if (tiles[slot].FullyCovered)
            {
                continue;
            }

            IllustrationBlock? cheapest = BestMask(
                rgba, asset.Header, tiles[slot], profiles[tiles[slot].GroupIndex], paletteIds, skips, options);

            if (cheapest is not { } block)
            {
                // Nothing could be written for this tile against the profile it was given,
                // which the scan should have made impossible.
                throw new InvalidOperationException(
                    $"Tile {slot} cannot be written against profile {profiles[tiles[slot].GroupIndex].ProfileId}.");
            }

            IllustrationLinkBlock? link = BestLink(block, written, byContent, byMask, byColors);
            BlockBase chosen = link is not null ? link : block;

            // A link answers "this tile repeats a field of that one". Where no field matches
            // but nearly every pixel does - a neighbouring tile over the same background, a
            // gradient a shade further along - the difference between them is smaller than
            // either way of writing the tile whole.
            long best = MeasureBlock(chosen);
            (MaskedBlock Block, long Cost)? difference = BestMasked(
                rgba, asset.Header, tiles, slot, profiles[tiles[slot].GroupIndex], options, skips, similar);

            MaskedBlock? masked = null;
            if (difference is { } chosenDifference && chosenDifference.Cost < best)
            {
                masked = chosenDifference.Block;
                best = chosenDifference.Cost;
            }

            // Where a neighbour is close without being equal, what changed is narrower than
            // what is there: the same shape a shade darker needs three bits a channel, not
            // eight. That wants a profile of its own, so its cost is counted here too.
            (MaskedBlock Block, ColorProfileBlock? Profile, long Cost)? delta =
                BestDelta(rgba, asset.Header, tiles, slot, deltaProfiles, options, similar);

            if (delta is { } chosenDelta && chosenDelta.Cost < best)
            {
                if (chosenDelta.Profile is not null)
                {
                    deltaProfiles.Commit(chosenDelta.Profile);
                    asset.Blocks.Add(chosenDelta.Profile);
                }

                asset.Blocks.Add(chosenDelta.Block);
                continue;
            }

            if (masked is not null)
            {
                asset.Blocks.Add(masked);

                // A masked slot is deliberately not registered as a link target. A link merges
                // fields onto what it points at, and merging them onto a difference would mean
                // "that difference, with these fields changed", which is not this tile.
                continue;
            }

            asset.Blocks.Add(chosen);

            // Every slot is registered by the content it resolves to, not by how it was
            // stored. A slot written as a link is still a valid target - chains resolve - and
            // without this a tile that became a link would stop being reachable, so later
            // duplicates of it would have to be written out in full.
            written[slot] = block;
            byContent.TryAdd(block, slot);
            byMask.TryAdd(block.Mask, slot);
            byColors.TryAdd(block.Colors, slot);
        }

        GroupPlainLinks(asset);
        CombineBlocks(asset);
        LinkProfiles(asset);

        asset.Header.BlockCount = (uint)asset.Blocks.Count;
        return asset;
    }

    /// <summary>
    /// Rewrites profiles as links to earlier ones wherever saying the difference is smaller
    /// than saying everything again.
    /// </summary>
    /// <remarks>
    /// A picture carries dozens of profiles and they are rarely unalike: tiles of the same
    /// drawing differ by where their colours start far more often than by how wide they are. A
    /// link names the one field that moved. Only earlier profiles are borrowed from, so the
    /// chain always ends and no link can point at one that borrows from it.
    /// </remarks>
    /// <summary>Bytes a profile link has to save before it is worth taking.</summary>
    private const int ProfileLinkMargin = 4;

    private static void LinkProfiles(StaticIllustrationAsset asset)
    {
        var written = new List<ColorProfileBlock>();

        for (int i = 0; i < asset.Blocks.Count; i++)
        {
            if (asset.Blocks[i] is not ColorProfileBlock profile)
            {
                continue;
            }

            long alone = MeasureBlock(profile);
            ColorProfileLinkBlock? best = null;
            long bestCost = alone;

            foreach (ColorProfileBlock target in written)
            {
                ColorProfileLinkBlock link = MakeProfileLink(profile, target);
                long cost = MeasureBlock(link);

                // A link has to save something worth having, not a byte. Profiles are small
                // and highly alike, so a link that barely wins trades a run of near-identical
                // blocks - which compress to almost nothing - for a scattering of different
                // ones, and the file comes out larger for it.
                if (cost + ProfileLinkMargin < bestCost)
                {
                    bestCost = cost;
                    best = link;
                }
            }

            // The profile stays in the list either way: a link resolves to the same profile, so
            // a later one may borrow from it whichever form it took.
            written.Add(profile);

            if (best is not null)
            {
                asset.Blocks[i] = best;
            }
        }
    }

    /// <summary>Builds a link to one profile, replacing exactly the fields that differ.</summary>
    private static ColorProfileLinkBlock MakeProfileLink(ColorProfileBlock profile, ColorProfileBlock target)
    {
        var link = new ColorProfileLinkBlock
        {
            ProfileId = profile.ProfileId,
            TargetProfileId = target.ProfileId,
        };

        if (profile.Flags != target.Flags)
        {
            link.SetOverride(ColorProfileLinkBlock.OverrideFlagsFlag, true);
            link.Flags = profile.Flags;
        }

        if (profile.Mirrors != target.Mirrors)
        {
            link.SetOverride(ColorProfileLinkBlock.OverrideMirrorsFlag, true);
            link.Mirrors = profile.Mirrors;
        }

        if (profile.NonStaticColorSize != target.NonStaticColorSize)
        {
            link.SetOverride(ColorProfileLinkBlock.OverrideSizesFlag, true);
            link.NonStaticColorSize = profile.NonStaticColorSize;
        }

        if (profile.StaticColorR != target.StaticColorR)
        {
            link.SetOverride(ColorProfileLinkBlock.OverrideRedFlag, true);
            link.StaticColorR = profile.StaticColorR;
        }

        if (profile.StaticColorG != target.StaticColorG)
        {
            link.SetOverride(ColorProfileLinkBlock.OverrideGreenFlag, true);
            link.StaticColorG = profile.StaticColorG;
        }

        if (profile.StaticColorB != target.StaticColorB)
        {
            link.SetOverride(ColorProfileLinkBlock.OverrideBlueFlag, true);
            link.StaticColorB = profile.StaticColorB;
        }

        if (profile.StaticColorA != target.StaticColorA)
        {
            link.SetOverride(ColorProfileLinkBlock.OverrideAlphaFlag, true);
            link.StaticColorA = profile.StaticColorA;
        }

        if (!profile.ColorSkipCounts.AsSpan().SequenceEqual(target.ColorSkipCounts)
            || !profile.ColorSkips.AsSpan().SequenceEqual(target.ColorSkips))
        {
            link.SetOverride(ColorProfileLinkBlock.OverrideSkipsFlag, true);
            link.ColorSkipCounts = profile.ColorSkipCounts;
            link.ColorSkips = profile.ColorSkips;
        }

        return link;
    }

    /// <summary>
    /// Writes runs of neighbouring tiles that agree on their settings as one block, where doing
    /// so is smaller.
    /// </summary>
    /// <remarks>
    /// Most of what a tile stores besides its pixels is agreement with its neighbours: the same
    /// flags, the same profile, the same nothing-in-particular about a palette. Written
    /// together they say it once and share one bit stream, so the entries no longer round up to
    /// a byte per tile.
    /// </remarks>
    private static void CombineBlocks(StaticIllustrationAsset asset)
    {
        var result = new List<BlockBase>(asset.Blocks.Count);
        int at = 0;

        while (at < asset.Blocks.Count)
        {
            // A difference points at a tile of its own, so it cannot share a header with
            // anything; links and repeats hold no pixels to combine.
            if (asset.Blocks[at] is not IllustrationBlock first || first is MaskedBlock || first.HasBrushes)
            {
                result.Add(asset.Blocks[at++]);
                continue;
            }

            int end = at + 1;
            while (end < asset.Blocks.Count
                && end - at < CombinedBlock.MaxTiles
                && asset.Blocks[end] is IllustrationBlock next
                && next is not MaskedBlock
                && Agrees(first, next))
            {
                end++;
            }

            if (end - at >= 2)
            {
                var members = new List<IllustrationBlock>(end - at);
                long apart = 0;
                for (int i = at; i < end; i++)
                {
                    var block = (IllustrationBlock)asset.Blocks[i];
                    members.Add(block);
                    apart += MeasureBlock(block);
                }

                CombinedBlock combined = Combine(members, asset.FindProfile(first.ColorProfileId));
                if (MeasureBlock(combined) < apart)
                {
                    result.Add(combined);
                    at = end;
                    continue;
                }
            }

            // The whole run did not pay. Moving on by one rather than past the run lets a
            // shorter stretch starting inside it be tried on its own.
            result.Add(asset.Blocks[at++]);
        }

        asset.Blocks.Clear();
        asset.Blocks.AddRange(result);
    }

    /// <summary>Whether two tiles agree on everything a combined block states once.</summary>
    private static bool Agrees(IllustrationBlock first, IllustrationBlock other) =>
        // Shapes are per tile and a combined block has nowhere to put them, so a tile carrying
        // any is written on its own.
        !first.HasBrushes
        && !other.HasBrushes
        && first.Flags == other.Flags
        && first.ColorProfileId == other.ColorProfileId
        && first.MaskPaletteId == other.MaskPaletteId
        && first.OutOfMaskColor == other.OutOfMaskColor;

    /// <summary>Writes several agreeing tiles as one block.</summary>
    private static CombinedBlock Combine(List<IllustrationBlock> members, ColorProfileBlock? profile)
    {
        IllustrationBlock first = members[0];

        var mask = new List<byte>();
        var colors = new List<uint>();
        var maskLengths = new List<uint>(members.Count);
        var colorCounts = new List<uint>(members.Count);

        foreach (IllustrationBlock member in members)
        {
            maskLengths.Add((uint)member.Mask.Length);
            colorCounts.Add((uint)member.Colors.Length);
            mask.AddRange(member.Mask);
            colors.AddRange(member.Colors);
        }

        var combined = new CombinedBlock
        {
            Flags = first.Flags,
            ColorProfileId = first.ColorProfileId,
            MaskPaletteId = first.MaskPaletteId,
            OutOfMaskColor = first.OutOfMaskColor,
            MaskLengths = first.HasMask ? [.. maskLengths] : [],
            ColorCounts = [.. colorCounts],
            Mask = [.. mask],
            Colors = [.. colors],
        };

        // Packed here as well as at save time, because deciding whether to combine means
        // comparing what one block would occupy against what the separate ones do.
        combined.PackedColors = profile is null
            ? []
            : ColorBitPacker.Pack(profile, combined.Colors);

        return combined;
    }

    /// <summary>
    /// Replaces runs of override-free links with one repeat block per target, where doing so
    /// is smaller.
    /// </summary>
    /// <remarks>
    /// A link with nothing to override still pays its own block framing plus a target and an
    /// empty override mask. Naming the tiles in one list costs a byte or two each instead,
    /// which is most of what a flat background was spending.
    /// </remarks>
    private static void GroupPlainLinks(StaticIllustrationAsset asset)
    {
        // Slot order, not file order. A tile the image mask covers is not stored at all, so a
        // block's position among its peers says nothing about which tile it fills; naming the
        // wrong tiles here builds repeats that claim the wrong slots and links that loop.
        IReadOnlyList<BlockBase?> sequence = IllustrationTiles.Slots(asset.Header, asset.Blocks);

        // Slot numbers survive the swap: a repeat claims the slots it names and the remaining
        // positional blocks close up behind it, keeping their order and therefore their tiles.
        var byTarget = new Dictionary<uint, List<(int Slot, IllustrationLinkBlock Link)>>();
        for (int slot = 0; slot < sequence.Count; slot++)
        {
            if (sequence[slot] is IllustrationLinkBlock link && link.Overrides == 0)
            {
                if (!byTarget.TryGetValue(link.TargetIndex, out List<(int, IllustrationLinkBlock)>? members))
                {
                    members = [];
                    byTarget[link.TargetIndex] = members;
                }

                members.Add((slot, link));
            }
        }

        foreach ((uint target, List<(int Slot, IllustrationLinkBlock Link)> members) in byTarget)
        {
            var repeat = new IllustrationRepeatBlock
            {
                TargetTile = target,
                Tiles = [.. members.Select(m => (uint)m.Slot)],
            };

            long grouped = MeasureBlock(repeat);
            long separate = 0;
            foreach ((int _, IllustrationLinkBlock link) in members)
            {
                separate += MeasureBlock(link);
            }

            if (grouped >= separate)
            {
                continue;
            }

            foreach ((int _, IllustrationLinkBlock link) in members)
            {
                asset.Blocks.Remove(link);
            }

            asset.Blocks.Add(repeat);
        }
    }

    /// <summary>
    /// Groups tiles onto shared profiles, merging only where doing so makes the file smaller.
    /// </summary>
    /// <remarks>
    /// Two passes. The first walks the tiles and drops each into whichever existing group it
    /// saves most bytes in, opening a new group when no merge pays. The second repeatedly
    /// merges the best-paying pair of groups until none is worth merging, which catches
    /// savings the first pass could not see because the group did not exist yet.
    /// </remarks>
    private static List<ProfileGroup> MergeProfiles(List<TileScan> tiles)
    {
        var groups = new List<ProfileGroup>();

        foreach (TileScan tile in tiles)
        {
            // A profile counts in one numbering, so tiles reading their colours by different
            // tables cannot share one however alike those colours look. Two regions describing
            // the same values share a numbering and so may still share profiles.
            int region = tile.Numbering;

            long alone = GroupCost(tile.Range, tile.CoveredPixels);

            int best = -1;
            long bestSaving = 0;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Region != region)
                {
                    continue;
                }

                long saving = groups[i].Cost + alone - MergedCost(groups[i], tile.Range, tile.CoveredPixels);
                if (saving > bestSaving)
                {
                    bestSaving = saving;
                    best = i;
                }
            }

            if (best >= 0)
            {
                groups[best].Add(tile);
            }
            else
            {
                var group = new ProfileGroup(tile.Range.Clone(), alone) { Region = region };
                group.Add(tile);
                groups.Add(group);
            }
        }

        Consolidate(groups);

        if (groups.Count == 0)
        {
            // Nothing to encode at all; one flat profile still has to exist to be named.
            groups.Add(new ProfileGroup(ChannelRange.Empty(), 0));
        }

        for (int i = 0; i < groups.Count; i++)
        {
            foreach (TileScan tile in groups[i].Tiles)
            {
                tile.GroupIndex = i;
            }
        }

        return groups;
    }

    /// <summary>Merges the best-paying pair of groups until no pair is worth merging.</summary>
    private static void Consolidate(List<ProfileGroup> groups)
    {
        while (groups.Count > 1)
        {
            int bestA = -1;
            int bestB = -1;
            long bestSaving = 0;

            for (int a = 0; a < groups.Count; a++)
            {
                for (int b = a + 1; b < groups.Count; b++)
                {
                    long saving = groups[a].Cost + groups[b].Cost
                        - MergedCost(groups[a], groups[b].Range, groups[b].CoveredPixels);

                    if (saving > bestSaving)
                    {
                        bestSaving = saving;
                        bestA = a;
                        bestB = b;
                    }
                }
            }

            if (bestA < 0)
            {
                return;
            }

            groups[bestA].Absorb(groups[bestB]);
            groups.RemoveAt(bestB);
        }
    }

    /// <summary>What a group would cost after taking on another range.</summary>
    private static long MergedCost(ProfileGroup group, ChannelRange other, long coveredPixels)
    {
        ChannelRange merged = group.Range.Clone();
        merged.Absorb(other);
        return GroupCost(merged, group.CoveredPixels + coveredPixels);
    }

    /// <summary>
    /// Bytes a group costs in the file: its one profile block, plus the colour entries every
    /// tile sharing it has to store.
    /// </summary>
    /// <remarks>
    /// This is the whole merge criterion. Entries are bit-packed, so widening a channel by one
    /// bit really does cost one bit for every covered pixel in the group, and a channel turning
    /// from static to varying costs its full width. Against that sits the single profile block
    /// a merge saves.
    /// </remarks>
    private static long GroupCost(ChannelRange range, long coveredPixels)
    {
        ColorProfileBlock profile = range.ToProfile(1, coveredPixels);
        long bitsPerPixel = ColorBitPacker.BitsPerPixel(profile);
        return MeasureBlock(profile) + (((coveredPixels * bitsPerPixel) + 7) / 8);
    }

    /// <summary>Bytes a block occupies in the file, framing included.</summary>
    private static long MeasureBlock(BlockBase block)
    {
        var package = new BitPackage();
        block.Write(package);
        long payload = package.Export().LongLength;
        return VarSize(block.Type) + VarSize((ulong)payload) + payload;
    }

    /// <summary>Bytes a value occupies as a variable-length integer.</summary>
    private static long VarSize(ulong value)
    {
        long size = 1;
        while ((value >>= 7) != 0)
        {
            size++;
        }

        return size;
    }

    /// <summary>Works out which pixels a tile covers and the range each channel spans.</summary>
    private static TileScan ScanTile(
        byte[] rgba,
        Header header,
        uint tileX,
        uint tileY,
        HashSet<uint> claimed,
        EncoderOptions options)
    {
        uint originX = tileX * header.BlockWidth;
        uint originY = tileY * header.BlockHeight;
        int tilePixels = (int)(header.BlockWidth * header.BlockHeight);

        var offsets = new List<long>(tilePixels);
        var counts = new Dictionary<uint, int>();
        int inImage = 0;

        for (int i = 0; i < tilePixels; i++)
        {
            uint x = originX + (uint)(i % (int)header.BlockWidth);
            uint y = originY + (uint)(i / (int)header.BlockWidth);

            // A tile on the right or bottom edge can hang off the image. Those pixels are left
            // uncovered so they consume no colour, exactly as the decoder skips them.
            if (x >= header.ImageWidth || y >= header.ImageHeight)
            {
                offsets.Add(-1);
                continue;
            }

            long offset = ((long)y * header.ImageWidth + x) * IllustrationDecoder.BytesPerPixel;
            uint packed = Pack(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);

            if (claimed.Contains(packed))
            {
                // The image mask has this pixel. It takes no mask position and no entry here,
                // so it is cut from the tile rather than merely marked uncovered.
                offsets.Add(TileScan.ClaimedPixel);
                continue;
            }

            offsets.Add(offset);
            inImage++;
            counts[packed] = counts.GetValueOrDefault(packed) + 1;
        }

        uint dominant = 0;
        int bestCount = 0;
        foreach ((uint color, int count) in counts)
        {
            if (count > bestCount)
            {
                dominant = color;
                bestCount = count;
            }
        }

        var stats = new ColorStatistics(dominant, bestCount, inImage, counts.Count);

        // A tile hanging off the edge always needs a mask to mark the absent pixels, otherwise
        // the decoder would consume a colour for each of them.
        // Claimed pixels leave the tile entirely, so "complete" means every position the tile
        // still owns lies inside the image.
        int positions = 0;
        foreach (long offset in offsets)
        {
            if (offset != TileScan.ClaimedPixel)
            {
                positions++;
            }
        }

        bool complete = inImage == positions;

        uint[] palette = ChoosePalette(counts, inImage, options);

        // What each way of spending the mask costs this tile, in bits. The palette carries the
        // id field the tile itself stores; the block it points at is charged to the group
        // later, once it is known how many tiles share it.
        const int PaletteIdBits = 8;

        long paletteCost = palette.Length > 0
            ? MaskCost(counts, inImage, MaskPalleteBlock.BitsFor(palette.Length), [.. palette]) + PaletteIdBits
            : long.MaxValue;

        bool worthMasking = stats.DominantShare > options.DominantColorThreshold;

        long backgroundCost = worthMasking
            ? MaskCost(counts, inImage, 1, [dominant])
            : long.MaxValue;

        long plainCost = complete ? MaskCost(counts, inImage, 0, []) : long.MaxValue;

        long withoutPalette = Math.Min(backgroundCost, plainCost);
        bool preferBackground = backgroundCost <= plainCost && worthMasking;

        long saving = palette.Length > 0 && withoutPalette != long.MaxValue
            ? withoutPalette - paletteCost
            : 0;

        // Nothing of this tile survived the image mask, so it will not be written at all.
        bool fullyCovered = inImage == 0 && offsets.Count > 0;

        return new TileScan(offsets, stats, palette, saving, preferBackground, complete, fullyCovered);
    }

    /// <summary>
    /// The cheapest link that could stand in for <paramref name="block"/>, or
    /// <see langword="null"/> when writing the tile in full is smaller.
    /// </summary>
    private static IllustrationLinkBlock? BestLink(
        IllustrationBlock block,
        Dictionary<int, IllustrationBlock> written,
        Dictionary<IllustrationBlock, int> byContent,
        Dictionary<byte[], int> byMask,
        Dictionary<uint[], int> byColors)
    {
        var candidates = new HashSet<int>();

        if (byContent.TryGetValue(block, out int identical))
        {
            candidates.Add(identical);
        }

        if (byMask.TryGetValue(block.Mask, out int sharesMask))
        {
            candidates.Add(sharesMask);
        }

        if (byColors.TryGetValue(block.Colors, out int sharesColors))
        {
            candidates.Add(sharesColors);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        long best = MeasureBlock(block);
        IllustrationLinkBlock? chosen = null;

        foreach (int candidate in candidates)
        {
            // A link takes its target's shapes - there is no override that could replace them -
            // so it can only stand in for a tile that paints the very same ones.
            if (!SameBrushes(written[candidate].Brushes, block.Brushes))
            {
                continue;
            }

            IllustrationLinkBlock link = MakeLink(candidate, written[candidate], block);
            long cost = MeasureBlock(link);

            if (cost < best)
            {
                best = cost;
                chosen = link;
            }
        }

        return chosen;
    }

    /// <summary>Builds a link to one tile, overriding exactly the fields that differ.</summary>
    private static IllustrationLinkBlock MakeLink(
        int targetIndex,
        IllustrationBlock target,
        IllustrationBlock block)
    {
        var link = new IllustrationLinkBlock { TargetIndex = (uint)targetIndex };

        if (block.Flags != target.Flags)
        {
            link.SetOverride(IllustrationLinkBlock.OverrideFlagsFlag, true);
            link.Flags = block.Flags;
        }

        if (block.ColorProfileId != target.ColorProfileId)
        {
            link.SetOverride(IllustrationLinkBlock.OverrideColorProfileFlag, true);
            link.ColorProfileId = block.ColorProfileId;
        }

        if (block.MaskPaletteId != target.MaskPaletteId)
        {
            link.SetOverride(IllustrationLinkBlock.OverrideMaskPaletteFlag, true);
            link.MaskPaletteId = block.MaskPaletteId;
        }

        if (block.OutOfMaskColor != target.OutOfMaskColor)
        {
            link.SetOverride(IllustrationLinkBlock.OverrideOutOfMaskColorFlag, true);
            link.OutOfMaskColor = block.OutOfMaskColor;
        }

        if (!block.Mask.AsSpan().SequenceEqual(target.Mask))
        {
            link.SetOverride(IllustrationLinkBlock.OverrideMaskFlag, true);
            link.Mask = block.Mask;
        }

        if (!block.Colors.AsSpan().SequenceEqual(target.Colors))
        {
            // Any profile difference was already overridden above, so these entries are
            // packed at the same widths the reader will resolve them against.
            link.SetOverride(IllustrationLinkBlock.OverrideColorsFlag, true);
            link.Colors = block.Colors;
            link.ColorCount = (uint)block.Colors.Length;
            link.PackedColors = block.PackedColors;
        }

        return link;
    }

    /// <summary>
    /// Settles which tiles actually use a palette: those whose group saves more between them
    /// than the single palette block costs.
    /// </summary>
    /// <remarks>
    /// Tiles wanting the same colours share one block, so its cost is paid once however many
    /// use it. A palette only one tile wants has to carry that cost alone and usually cannot;
    /// the same palette wanted by twenty tiles almost always can.
    /// </remarks>
    private static void DecidePalettes(List<TileScan> tiles)
    {
        var groups = new Dictionary<uint[], List<TileScan>>(SequenceComparer<uint>.Instance);

        foreach (TileScan tile in tiles)
        {
            if (tile.CandidatePalette.Length == 0 || tile.PaletteSaving <= 0)
            {
                continue;
            }

            if (!groups.TryGetValue(tile.CandidatePalette, out List<TileScan>? members))
            {
                members = [];
                groups[tile.CandidatePalette] = members;
            }

            members.Add(tile);
        }

        foreach ((uint[] palette, List<TileScan> members) in groups)
        {
            long saved = 0;
            foreach (TileScan tile in members)
            {
                saved += tile.PaletteSaving;
            }

            // Measured before the palette works out its cheapest storage, which makes this an
            // upper bound rather than the exact size. That is deliberate: the saving on the
            // other side of the comparison is itself optimistic - it counts what each tile
            // stops storing without counting what a palette costs it elsewhere - so pricing the
            // block at its dearest is what keeps the two honest against each other. Measured
            // exactly, the encoder takes palettes it should not: dimauebanCut grows by 5%.
            long blockBits = MeasureBlock(new MaskPalleteBlock { PaletteId = 1, Colors = palette }) * 8;
            if (saved <= blockBits)
            {
                continue;
            }

            foreach (TileScan tile in members)
            {
                tile.UsesPalette = true;
            }
        }
    }

    /// <summary>
    /// What one mask choice costs a tile, in bits: the mask itself, plus what the pixels it
    /// leaves behind cost as colour entries.
    /// </summary>
    /// <remarks>
    /// The entry width is derived from the colours that actually remain, not assumed. That
    /// matters: pulling a tile's two colours into a palette can leave every remaining pixel
    /// sharing one colour, at which point every channel is static and the entries cost nothing
    /// at all. A flat estimate makes those blocks look far more expensive than they are, and
    /// hands the palette wins it has not earned.
    /// </remarks>
    private static long MaskCost(
        Dictionary<uint, int> counts,
        long pixels,
        int maskBitsPerPixel,
        HashSet<uint> removed)
    {
        Span<byte> min = [255, 255, 255, 255];
        Span<byte> max = [0, 0, 0, 0];
        long remaining = 0;

        foreach ((uint color, int count) in counts)
        {
            if (removed.Contains(color))
            {
                continue;
            }

            remaining += count;
            for (int c = 0; c < 4; c++)
            {
                byte value = (byte)((color >> (c * 8)) & 0xFF);
                min[c] = Math.Min(min[c], value);
                max[c] = Math.Max(max[c], value);
            }
        }

        int entryBits = 0;
        if (remaining > 0)
        {
            for (int c = 0; c < 4; c++)
            {
                if (max[c] > min[c])
                {
                    entryBits += BitsNeeded((uint)(max[c] - min[c]));
                }
            }
        }

        return (pixels * maskBitsPerPixel) + (remaining * entryBits);
    }

    /// <summary>
    /// The colours worth putting in a palette for one tile, most common first, or empty when
    /// none of the sizes earns its index width.
    /// </summary>
    private static uint[] ChoosePalette(Dictionary<uint, int> counts, int totalPixels, EncoderOptions options)
    {
        if (totalPixels == 0 || counts.Count == 0)
        {
            return [];
        }

        uint[] ordered = [.. counts.OrderByDescending(pair => pair.Value).Select(pair => pair.Key)];

        uint[] best = [];
        long bestCost = long.MaxValue;

        foreach ((int size, float coverage) in options.PaletteThresholds)
        {
            int take = Math.Min(size, ordered.Length);

            long covered = 0;
            for (int i = 0; i < take; i++)
            {
                covered += counts[ordered[i]];
            }

            if ((float)covered / totalPixels < coverage)
            {
                continue;
            }

            // Every size that clears its threshold is weighed, not just the first. A wider
            // palette covers more pixels but numbers them in more bits, and where the colours
            // beyond the seventh are rare the extra bit costs more across the whole tile than
            // those colours ever save. Taking the first size that qualifies would always reach
            // for the widest and miss that.
            uint[] candidate = ordered[..take];
            Array.Sort(candidate);

            long cost = MaskCost(counts, totalPixels, MaskPalleteBlock.BitsFor(take), [.. candidate]);
            if (cost + (long)(cost * options.PaletteSizeMargin) < bestCost)
            {
                bestCost = cost;

                // Sorted, not left in frequency order. Index width is fixed either way, so
                // ordering costs nothing to read - but a canonical order means two tiles holding
                // the same colours produce the same palette and can share one block, and which
                // order is canonical decides whether that palette can be written as a ramp.
                best = CheapestOrder(candidate);
            }
        }

        return best;
    }

    /// <summary>
    /// The cheapest difference against an earlier tile, or <see langword="null"/> when none is
    /// close enough to be worth one.
    /// </summary>
    /// <remarks>
    /// The tiles touching this one are always measured; past them the scan supplies whatever
    /// earlier tiles summarise the same way, in whichever orientation made them summarise alike.
    /// Fewest differing positions wins, which is decided without building a block for each: a
    /// replaced position costs the same wherever in the tile it falls.
    /// </remarks>
    private static (MaskedBlock Block, long Cost)? BestMasked(
        byte[] rgba,
        Header header,
        List<TileScan> tiles,
        int slot,
        ColorProfileBlock profile,
        EncoderOptions options,
        ColorSkipsBlock? skips,
        TileSimilarity similar)
    {
        int nearTarget = -1;
        int nearFewest = int.MaxValue;

        int farTarget = -1;
        TileTransform farTransform = TileTransform.None;
        int farFewest = int.MaxValue;

        foreach ((int target, TileTransform transform, bool local) in similar.Candidates(slot))
        {
            int differing = CountDifferences(rgba, header, tiles[slot], target, transform, out int positions);
            if (positions == 0)
            {
                continue;
            }

            // Past this share of the tile the entries dominate and the mask is just an extra
            // bit a pixel, so writing it whole wins anyway; measuring it would be wasted work.
            if (differing > positions * options.MaskedDifferenceThreshold)
            {
                continue;
            }

            // The two kinds are kept apart because they are not judged alike: one has a margin
            // to clear and the other does not, so the best of each is built and they are then
            // measured against one another.
            if (local)
            {
                if (differing < nearFewest)
                {
                    nearFewest = differing;
                    nearTarget = target;
                }
            }
            else if (differing < farFewest)
            {
                farFewest = differing;
                farTarget = target;
                farTransform = transform;
            }
        }

        (MaskedBlock Block, long Cost)? best = null;
        Consider(nearTarget, TileTransform.None, false);
        Consider(farTarget, farTransform, true);
        return best;

        void Consider(int target, TileTransform transform, bool far)
        {
            if (target < 0
                || MaskedFrom(rgba, header, tiles[slot], target, transform, profile, skips) is not MaskedBlock block)
            {
                return;
            }

            long cost = MeasureBlock(block);
            if (far)
            {
                cost += (long)(cost * options.MaskedScanMargin);
            }

            if (best is null || cost < best.Value.Cost)
            {
                best = (block, cost);
            }
        }
    }

    /// <summary>
    /// The cheapest way to store a tile as differences from a neighbour, counting the profile
    /// those differences need, or <see langword="null"/> when no neighbour is close enough.
    /// </summary>
    /// <remarks>
    /// Every stored pixel is compared with the pixel at the same place in the neighbour, and
    /// what is written is how far it has moved. A pixel that has not moved at all is left to
    /// the copy, exactly as in a plain difference.
    /// </remarks>
    private static (MaskedBlock Block, ColorProfileBlock? Profile, long Cost)? BestDelta(
        byte[] rgba,
        Header header,
        List<TileScan> tiles,
        int slot,
        DeltaProfiles profiles,
        EncoderOptions options,
        TileSimilarity similar)
    {
        // Nothing at all is stored as a difference at zero, deltas included: the threshold is
        // the one switch for the whole idea, however the differences are written down.
        if (header.TilesX == 0 || slot == 0 || options.MaskedDifferenceThreshold <= 0f)
        {
            return null;
        }

        int nearTarget = -1;
        long nearEstimate = long.MaxValue;

        int farTarget = -1;
        TileTransform farTransform = TileTransform.None;
        long farEstimate = long.MaxValue;

        foreach ((int target, TileTransform transform, bool local) in similar.Candidates(slot))
        {
            if (EstimateDelta(rgba, header, tiles[slot], target, transform) is not long estimate)
            {
                continue;
            }

            if (local)
            {
                if (estimate < nearEstimate)
                {
                    nearEstimate = estimate;
                    nearTarget = target;
                }
            }
            else if (estimate < farEstimate)
            {
                farEstimate = estimate;
                farTarget = target;
                farTransform = transform;
            }
        }

        (MaskedBlock Block, ColorProfileBlock? Profile, long Cost)? best = null;
        Consider(nearTarget, TileTransform.None, false);
        Consider(farTarget, farTransform, true);
        return best;

        void Consider(int target, TileTransform transform, bool far)
        {
            if (target < 0)
            {
                return;
            }

            (MaskedBlock Block, ColorProfileBlock? Profile, long Cost) candidate =
                DeltaFrom(rgba, header, tiles[slot], target, transform, profiles);

            // Differences against a tile that is not the one alongside have a margin to clear,
            // for the same reason plain ones do: they read as well and compress worse.
            if (far)
            {
                candidate.Cost += (long)(candidate.Cost * options.MaskedScanMargin);
            }

            if (best is null || candidate.Cost < best.Value.Cost)
            {
                best = candidate;
            }
        }
    }

    /// <summary>
    /// Roughly what differences against one candidate would cost, in bits, or
    /// <see langword="null"/> when they cannot be taken against it at all.
    /// </summary>
    /// <remarks>
    /// Rough is enough to pick between a handful of candidates, and it avoids building a block
    /// for each to throw all but one away. The winner is then measured for real.
    /// </remarks>
    private static long? EstimateDelta(
        byte[] rgba,
        Header header,
        TileScan tile,
        int target,
        TileTransform transform)
    {
        int tilesX = (int)header.TilesX;
        uint fromX = (uint)(target % tilesX) * header.BlockWidth;
        uint fromY = (uint)(target / tilesX) * header.BlockHeight;

        Span<uint> lowest = [uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue];
        Span<uint> highest = [0, 0, 0, 0];

        int positions = 0;
        int covered = 0;

        for (int i = 0; i < tile.Offsets.Count; i++)
        {
            long offset = tile.Offsets[i];
            if (offset == TileScan.ClaimedPixel)
            {
                continue;
            }

            positions++;

            if (offset < 0)
            {
                continue;
            }

            // Without a pixel to move away from there is no difference to store, so the whole
            // candidate goes rather than one position of it.
            if (TargetOffset(header, fromX, fromY, transform, i) is not long from)
            {
                return null;
            }

            bool moved = false;
            for (int c = 0; c < 4; c++)
            {
                int difference = rgba[offset + c] - rgba[from + c];

                // Beyond this a difference is as wide as the colour it replaces, and the zigzag
                // would not fit the byte a channel minimum is stored in either.
                if (difference is < -127 or > 127)
                {
                    return null;
                }

                if (difference == 0)
                {
                    continue;
                }

                moved = true;
            }

            if (!moved)
            {
                continue;
            }

            covered++;
            for (int c = 0; c < 4; c++)
            {
                uint zigzag = MaskedBlock.ToZigzag(rgba[offset + c] - rgba[from + c]);
                lowest[c] = Math.Min(lowest[c], zigzag);
                highest[c] = Math.Max(highest[c], zigzag);
            }
        }

        if (positions == 0 || covered == 0)
        {
            return null;
        }

        // No threshold here, unlike a plain difference. There, every replaced pixel costs a
        // whole entry, so replacing most of the tile cannot pay; here the entries are narrower
        // than the colours they stand in for, and a tile where every pixel shifted by one is
        // the case this is for. Cost decides on its own.
        int bits = 0;
        for (int c = 0; c < 4; c++)
        {
            if (highest[c] > lowest[c])
            {
                bits += BitsNeeded(highest[c] - lowest[c]);
            }
        }

        // The mask, the entries, and a profile of a dozen or so bytes to read them by. A
        // difference that left nothing alone needs no mask at all.
        return (covered == positions ? 0 : positions) + ((long)covered * bits) + (12 * 8);
    }

    /// <summary>Builds the differences against one target, and the profile that reads them.</summary>
    private static (MaskedBlock Block, ColorProfileBlock? Profile, long Cost) DeltaFrom(
        byte[] rgba,
        Header header,
        TileScan tile,
        int target,
        TileTransform transform,
        DeltaProfiles profiles)
    {
        int tilesX = (int)header.TilesX;
        uint fromX = (uint)(target % tilesX) * header.BlockWidth;
        uint fromY = (uint)(target / tilesX) * header.BlockHeight;

        int positions = 0;
        foreach (long each in tile.Offsets)
        {
            if (each != TileScan.ClaimedPixel)
            {
                positions++;
            }
        }

        // The profile has to cover the differences before any of them can be written, so the
        // tile is walked twice: once to see how far its pixels moved, once to write it down.
        ChannelRange range = ChannelRange.Empty();
        long covered = 0;

        Walk((position, zigzag) =>
        {
            covered++;
            range.Include((byte)zigzag[0], (byte)zigzag[1], (byte)zigzag[2], (byte)zigzag[3]);
        });

        ColorProfileBlock shaped = range.ToProfile(0, covered);
        ColorProfileBlock? existing = profiles.Find(shaped);
        ColorProfileBlock profile = existing ?? shaped;
        ColorProfileBlock? added = existing is null ? shaped : null;

        if (existing is null)
        {
            // Numbered as though it were being written, so the cost measured below is the cost
            // this block would really carry. It only keeps the number if it is chosen.
            profile.ProfileId = profiles.NextId;
        }

        // A mask of solid ones says nothing: when every position moved, the mask goes and the
        // entries are read straight through.
        bool everything = covered == positions;
        var mask = everything ? [] : new byte[(positions + 7) / 8];
        var colors = new List<uint>();

        Walk((position, zigzag) =>
        {
            if (!everything)
            {
                mask[position >> 3] |= (byte)(1 << (position & 7));
            }

            AppendDeltaOffsets(profile, zigzag, colors);
        });

        uint[] entries = [.. colors];

        var block = new MaskedBlock
        {
            TargetTile = (uint)target,
            Transform = transform,
            MaskMode = everything ? MaskMode.Disabled : MaskMode.PerPixel,
            IsDelta = true,
            ColorProfileId = profile.ProfileId,
            Mask = mask,
            Colors = entries,
            ColorCount = (uint)entries.Length,
            PackedColors = ColorBitPacker.Pack(profile, entries),
        };

        long cost = MeasureBlock(block) + (added is null ? 0 : MeasureBlock(added));
        return (block, added, cost);

        // One pass over the tile, handing every moved pixel to the caller.
        void Walk(Action<int, uint[]> moved)
        {
            var zigzag = new uint[4];
            int position = -1;

            for (int i = 0; i < tile.Offsets.Count; i++)
            {
                long offset = tile.Offsets[i];
                if (offset == TileScan.ClaimedPixel)
                {
                    continue;
                }

                position++;

                if (offset < 0 || TargetOffset(header, fromX, fromY, transform, i) is not long from)
                {
                    continue;
                }

                bool any = false;
                for (int c = 0; c < 4; c++)
                {
                    int difference = rgba[offset + c] - rgba[from + c];
                    zigzag[c] = MaskedBlock.ToZigzag(difference);
                    any |= difference != 0;
                }

                if (any)
                {
                    moved(position, zigzag);
                }
            }
        }
    }

    /// <summary>
    /// Appends one pixel's differences, one entry per varying channel, exactly as
    /// <see cref="AppendOffsets"/> does for colours.
    /// </summary>
    private static void AppendDeltaOffsets(ColorProfileBlock profile, uint[] zigzag, List<uint> colors)
    {
        Append(ColorProfileBlock.RedNonStaticFlag, profile.StaticColorR, zigzag[0]);
        Append(ColorProfileBlock.GreenNonStaticFlag, profile.StaticColorG, zigzag[1]);
        Append(ColorProfileBlock.BlueNonStaticFlag, profile.StaticColorB, zigzag[2]);
        Append(ColorProfileBlock.AlphaNonStaticFlag, profile.StaticColorA, zigzag[3]);

        void Append(uint flag, uint minimum, uint value)
        {
            if (profile.IsNonStatic(flag))
            {
                colors.Add(profile.CompactOffset(flag, value - minimum));
            }
        }
    }

    /// <summary>
    /// How many of a tile's stored positions differ from the same positions of another tile.
    /// </summary>
    /// <remarks>
    /// Positions, not pixels: what the image mask already claimed is part of neither tile, and
    /// a position where this tile leaves the image draws nothing, so neither counts as a
    /// difference. A position the target lacks does, because nothing can be inherited for it.
    /// </remarks>
    private static int CountDifferences(
        byte[] rgba,
        Header header,
        TileScan tile,
        int target,
        TileTransform transform,
        out int positions)
    {
        int tilesX = (int)header.TilesX;
        uint fromX = (uint)(target % tilesX) * header.BlockWidth;
        uint fromY = (uint)(target / tilesX) * header.BlockHeight;

        positions = 0;
        int differing = 0;

        for (int i = 0; i < tile.Offsets.Count; i++)
        {
            long offset = tile.Offsets[i];
            if (offset == TileScan.ClaimedPixel)
            {
                continue;
            }

            positions++;

            if (offset < 0)
            {
                continue;
            }

            if (TargetOffset(header, fromX, fromY, transform, i) is not long from
                || Pack(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3])
                    != Pack(rgba[from], rgba[from + 1], rgba[from + 2], rgba[from + 3]))
            {
                differing++;
            }
        }

        return differing;
    }

    /// <summary>
    /// Where the same position within another tile lands in the buffer, or
    /// <see langword="null"/> when that tile does not reach it.
    /// </summary>
    private static long? TargetOffset(
        Header header,
        uint fromX,
        uint fromY,
        TileTransform transform,
        int position)
    {
        uint x = (uint)(position % (int)header.BlockWidth);
        uint y = (uint)(position / (int)header.BlockWidth);
        transform.Map(x, y, header.BlockWidth, header.BlockHeight, out uint sourceX, out uint sourceY);

        uint imageX = fromX + sourceX;
        uint imageY = fromY + sourceY;

        return imageX >= header.ImageWidth || imageY >= header.ImageHeight
            ? null
            : (((long)imageY * header.ImageWidth) + imageX) * IllustrationDecoder.BytesPerPixel;
    }

    /// <summary>
    /// Builds the difference against one target, or <see langword="null"/> when a differing
    /// pixel is one the tile's profile cannot express.
    /// </summary>
    /// <remarks>
    /// The profile was fitted to the pixels the full block would store, and a masked block
    /// stores a different set: a pixel the full block would have left to a palette or to the
    /// background colour has to be written out here, and may fall outside the profile's range
    /// or into one of the gaps it skips. Rather than widen a profile other tiles are already
    /// sharing, the candidate is abandoned and the tile written whole.
    /// </remarks>
    private static MaskedBlock? MaskedFrom(
        byte[] rgba,
        Header header,
        TileScan tile,
        int target,
        TileTransform transform,
        ColorProfileBlock profile,
        ColorSkipsBlock? skips)
    {
        int tilesX = (int)header.TilesX;
        uint fromX = (uint)(target % tilesX) * header.BlockWidth;
        uint fromY = (uint)(target / tilesX) * header.BlockHeight;

        int positions = 0;
        foreach (long each in tile.Offsets)
        {
            if (each != TileScan.ClaimedPixel)
            {
                positions++;
            }
        }

        var mask = new byte[(positions + 7) / 8];
        var colors = new List<uint>();
        int position = -1;

        for (int i = 0; i < tile.Offsets.Count; i++)
        {
            long offset = tile.Offsets[i];
            if (offset == TileScan.ClaimedPixel)
            {
                continue;
            }

            position++;

            // Nothing is drawn beyond the edge of the image, so those positions inherit and
            // cost a bit rather than an entry.
            if (offset < 0)
            {
                continue;
            }

            long? from = TargetOffset(header, fromX, fromY, transform, i);
            if (from is long at
                && Pack(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3])
                    == Pack(rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]))
            {
                continue;
            }

            if (!Representable(profile, rgba, offset, skips, tile.Region))
            {
                return null;
            }

            mask[position >> 3] |= (byte)(1 << (position & 7));
            AppendOffsets(profile, rgba, offset, colors, skips, tile.Region);
        }

        uint[] entries = [.. colors];

        return new MaskedBlock
        {
            TargetTile = (uint)target,
            Transform = transform,
            MaskMode = MaskMode.PerPixel,
            ColorProfileId = profile.ProfileId,
            Mask = mask,
            Colors = entries,
            ColorCount = (uint)entries.Length,
            PackedColors = ColorBitPacker.Pack(profile, entries),
        };
    }

    /// <summary>Whether a profile can store one pixel exactly.</summary>
    private static bool Representable(
        ColorProfileBlock profile,
        byte[] rgba,
        long offset,
        ColorSkipsBlock? skips,
        int region)
    {
        ColorSkipsBlock? renumber = profile.UsesGlobalSkips ? skips : null;

        return Channel(ColorProfileBlock.RedNonStaticFlag, 0, profile.StaticColorR, rgba[offset])
            && Channel(ColorProfileBlock.GreenNonStaticFlag, 1, profile.StaticColorG, rgba[offset + 1])
            && Channel(ColorProfileBlock.BlueNonStaticFlag, 2, profile.StaticColorB, rgba[offset + 2])
            && Channel(ColorProfileBlock.AlphaNonStaticFlag, 3, profile.StaticColorA, rgba[offset + 3]);

        bool Channel(uint flag, int channel, uint minimumRaw, byte raw)
        {
            // A channel that copies another holds nothing of its own, so the pixel can only be
            // written if the two really do agree - which they need not, for a pixel the profile
            // was never fitted to.
            if (profile.IsMirrored(flag))
            {
                return raw == rgba[offset + ColorProfileBlock.IndexOf(profile.MirrorSource(flag))];
            }

            uint minimum = minimumRaw;

            // A difference stores pixels the whole block would not have: one the palette named,
            // or one the background covered. Those never entered the region's numbering, so
            // there is no number for them and the candidate goes.
            if (renumber is not null && !renumber.Holds(region, channel, raw))
            {
                return false;
            }

            uint value = renumber is null ? raw : Renumber(renumber, region, channel, raw);
            // A static channel stores nothing, so it can only express the one value it holds.
            if (!profile.IsNonStatic(flag))
            {
                return value == minimum;
            }


            if (value < minimum)
            {
                return false;
            }

            uint compact;
            try
            {
                compact = profile.CompactOffset(flag, value - minimum);
            }
            catch (InvalidOperationException)
            {
                // The value sits in a range the profile skipped over.
                return false;
            }

            return compact < (1u << (int)profile.GetChannelSize(flag));
        }
    }

    /// <summary>
    /// How much of the undivided cost a division has to beat, as a fraction: one twentieth.
    /// </summary>
    private const int DivisionMargin = 20;

    /// <summary>Bytes one colour profile block costs, near enough to weigh a division by.</summary>
    private const int ProfileEstimate = 12;

    /// <summary>
    /// How many distinct profiles a division would leave, counted by the range each tile ends
    /// up covering once its region has renumbered it.
    /// </summary>
    /// <remarks>
    /// Tiles covering the same range merge into one profile, so counting the distinct ranges is
    /// a fair estimate of how many profiles the file will carry - and it is the only part of
    /// what dividing costs that the width model does not already see. Dividing makes ranges
    /// differ that used to line up, and on a photograph those extra profiles cost more than the
    /// narrower channels save.
    /// </remarks>
    private static int ProfileShapes(
        Header header,
        int tileCount,
        int[] lowest,
        int[] highest,
        ColorSkipsBlock skips)
    {
        var shapes = new HashSet<ulong>();

        for (int tile = 0; tile < tileCount; tile++)
        {
            int region = skips.RegionOf(
                (uint)(tile % (int)header.TilesX),
                (uint)(tile / (int)header.TilesX),
                header.TilesX,
                header.TilesY);

            ulong shape = 0;
            bool any = false;

            for (int channel = 0; channel < ChannelMirrors.ChannelCount; channel++)
            {
                int low = lowest[(tile * ChannelMirrors.ChannelCount) + channel];
                int high = highest[(tile * ChannelMirrors.ChannelCount) + channel];

                if (low < 0)
                {
                    continue;
                }

                any = true;
                ReadOnlySpan<uint> runs = skips.For(region, channel);
                ulong start = SkipRuns.Holds(runs, (uint)low) ? SkipRuns.Compact(runs, (uint)low) : 0;
                ulong end = SkipRuns.Holds(runs, (uint)high) ? SkipRuns.Compact(runs, (uint)high) : 0;

                shape |= (start & 0xFF) << (channel * 16);
                shape |= (end & 0xFF) << ((channel * 16) + 8);
            }

            if (any)
            {
                shapes.Add(shape);
            }
        }

        return shapes.Count;
    }

    /// <summary>Words of a bitset holding which of a channel's 256 values are used.</summary>
    private const int UsedWords = ColorSkipsBlock.ChannelValues / 64;

    /// <summary>Words one tile's four channel bitsets take together.</summary>
    private const int TileUsedWords = UsedWords * ChannelMirrors.ChannelCount;

    /// <summary>
    /// How to describe the values the picture leaves unused, or <see langword="null"/> when
    /// describing them is not worth the bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every division from one region up to six by six is measured against the same yardstick:
    /// what the entries would cost at the width each region's numbering allows, plus what the
    /// tables themselves cost. A picture that is much the same throughout wins nothing by being
    /// divided and keeps its single table; one whose corners differ pays for a few tables and
    /// gets narrower channels everywhere.
    /// </para>
    /// <para>
    /// The values are gathered once per tile, so every division is answered by combining tiles
    /// rather than by walking the picture again - and a tile belongs to one region whole, which
    /// is what lets its profile count in a single numbering.
    /// </para>
    /// </remarks>
    private static ColorSkipsBlock? ChooseColorSkips(
        byte[] rgba,
        Header header,
        List<TileScan> tiles,
        EncoderOptions options)
    {
        // The table belongs to the whole file, so a picture that is one of several layers
        // cannot have one: the layers are fitted apart and only one table can be the file's.
        if (!options.ColorSkips || tiles.Count == 0 || header.TilesX == 0)
        {
            return null;
        }

        // One bitset per channel per tile: which values the tile will actually store.
        var used = new ulong[(long)tiles.Count * TileUsedWords];
        var pixels = new long[tiles.Count];

        // The lowest and highest value each tile holds, per channel: what a profile narrows to.
        var lowest = new int[tiles.Count * ChannelMirrors.ChannelCount];
        var highest = new int[tiles.Count * ChannelMirrors.ChannelCount];

        for (int i = 0; i < tiles.Count; i++)
        {
            pixels[i] = tiles[i].CollectUsed(rgba, used.AsSpan(i * TileUsedWords, TileUsedWords));

            for (int channel = 0; channel < ChannelMirrors.ChannelCount; channel++)
            {
                int low = -1;
                int high = -1;

                for (int value = 0; value < ColorSkipsBlock.ChannelValues; value++)
                {
                    int word = (i * TileUsedWords) + (channel * UsedWords) + (value >> 6);
                    if ((used[word] & (1UL << (value & 63))) == 0)
                    {
                        continue;
                    }

                    low = low < 0 ? value : low;
                    high = value;
                }

                lowest[(i * ChannelMirrors.ChannelCount) + channel] = low;
                highest[(i * ChannelMirrors.ChannelCount) + channel] = high;
            }
        }

        ColorSkipsBlock? best = null;
        long bestCost = long.MaxValue;
        long undivided = long.MaxValue;

        for (int regionsY = 1; regionsY <= ColorSkipsBlock.MaxRegions; regionsY++)
        {
            for (int regionsX = 1; regionsX <= ColorSkipsBlock.MaxRegions; regionsX++)
            {
                // More regions than tiles would leave empty tables describing nothing.
                if (regionsX > header.TilesX || regionsY > header.TilesY)
                {
                    continue;
                }

                ColorSkipsBlock candidate = BuildRegions(
                    header, tiles.Count, used, pixels, lowest, highest, regionsX, regionsY, out long cost);

                // What the division costs in profiles, which is the half of the bargain the
                // width model cannot see.
                cost += ProfileShapes(header, tiles.Count, lowest, highest, candidate) * ProfileEstimate;

                if (regionsX == 1 && regionsY == 1)
                {
                    undivided = cost;
                }

                // Dividing has to win by a clear margin, not by a hair. What the model measures
                // is the width of the entries, and the encoder has other ways of getting that
                // width down - per-profile skips, palettes, differences from a neighbour - so a
                // narrow win here usually turns into a loss once those have had their turn.
                long ceiling = regionsX == 1 && regionsY == 1
                    ? long.MaxValue
                    : undivided - (undivided / DivisionMargin);

                if (cost < bestCost && cost < ceiling)
                {
                    bestCost = cost;
                    best = candidate;
                }
            }
        }

        return best is null || best.IsEmpty ? null : best;
    }

    /// <summary>
    /// The tables one division would produce, and what the picture would cost with them.
    /// </summary>
    /// <remarks>
    /// Costed tile by tile rather than region by region, because that is how the file is
    /// written: a profile narrows to the span its own tiles cover, so what renumbering buys is
    /// the difference it makes to those spans, not to the region's.
    /// </remarks>
    private static ColorSkipsBlock BuildRegions(
        Header header,
        int tileCount,
        ulong[] used,
        long[] pixels,
        int[] lowest,
        int[] highest,
        int regionsX,
        int regionsY,
        out long cost)
    {
        var block = new ColorSkipsBlock { RegionsX = regionsX, RegionsY = regionsY };
        block.Regions = new uint[regionsX * regionsY][][];

        var combined = new ulong[regionsX * regionsY * TileUsedWords];
        var members = new List<int>[regionsX * regionsY];

        for (int region = 0; region < members.Length; region++)
        {
            members[region] = [];
        }

        for (int tile = 0; tile < tileCount; tile++)
        {
            int region = block.RegionOf(
                (uint)(tile % (int)header.TilesX),
                (uint)(tile / (int)header.TilesX),
                header.TilesX,
                header.TilesY);

            members[region].Add(tile);
            for (int w = 0; w < TileUsedWords; w++)
            {
                combined[(region * TileUsedWords) + w] |= used[(tile * TileUsedWords) + w];
            }
        }

        cost = 0;

        for (int region = 0; region < block.Regions.Length; region++)
        {
            block.Regions[region] = new uint[ChannelMirrors.ChannelCount][];

            for (int channel = 0; channel < ChannelMirrors.ChannelCount; channel++)
            {
                var flags = new bool[ColorSkipsBlock.ChannelValues];
                for (int value = 0; value < flags.Length; value++)
                {
                    int word = (region * TileUsedWords) + (channel * UsedWords) + (value >> 6);
                    flags[value] = (combined[word] & (1UL << (value & 63))) != 0;
                }

                uint[] runs = SkipRuns.Build(flags);
                long plain = EntryCost(members[region], channel, pixels, lowest, highest, []);

                if (runs.Length == 0 || runs.Length / 2 > ColorSkipsBlock.MaxRunsPerChannel)
                {
                    block.Regions[region][channel] = [];
                    cost += plain;
                    continue;
                }

                long table = 1;
                foreach (uint value in runs)
                {
                    table += VarSize(value);
                }

                long narrowed = table + EntryCost(members[region], channel, pixels, lowest, highest, runs);

                // A channel keeps its runs only where describing the gaps costs less than the
                // width they save across the tiles that read them.
                if (narrowed < plain)
                {
                    block.Regions[region][channel] = runs;
                    cost += narrowed;
                }
                else
                {
                    block.Regions[region][channel] = [];
                    cost += plain;
                }
            }
        }

        return block;
    }

    /// <summary>What one channel's entries cost across a region's tiles, at a given numbering.</summary>
    private static long EntryCost(
        List<int> tiles,
        int channel,
        long[] pixels,
        int[] lowest,
        int[] highest,
        ReadOnlySpan<uint> runs)
    {
        long bits = 0;

        foreach (int tile in tiles)
        {
            int low = lowest[(tile * ChannelMirrors.ChannelCount) + channel];
            int high = highest[(tile * ChannelMirrors.ChannelCount) + channel];

            if (low < 0 || high == low)
            {
                // Flat across the tile, so the profile stores it once and no pixel pays.
                continue;
            }

            uint span = runs.IsEmpty
                ? (uint)(high - low)
                : SkipRuns.Compact(runs, (uint)high) - SkipRuns.Compact(runs, (uint)low);

            bits += BitsNeeded(span) * pixels[tile];
        }

        return (bits + 7) / 8;
    }


    /// <summary>
    /// Which regions describe the same values, numbered so that regions sharing a table share
    /// a number.
    /// </summary>
    private static int[] TableGroups(ColorSkipsBlock skips)
    {
        var groups = new int[skips.RegionCount];
        var seen = new List<uint[][]>();

        for (int region = 0; region < groups.Length; region++)
        {
            groups[region] = -1;

            for (int other = 0; other < seen.Count; other++)
            {
                if (SameTable(seen[other], skips.Regions[region]))
                {
                    groups[region] = other;
                    break;
                }
            }

            if (groups[region] < 0)
            {
                groups[region] = seen.Count;
                seen.Add(skips.Regions[region]);
            }
        }

        return groups;

        static bool SameTable(uint[][] first, uint[][] second)
        {
            for (int c = 0; c < first.Length; c++)
            {
                if (!first[c].AsSpan().SequenceEqual(second[c]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>One channel value in the numbering a region's skips leave behind.</summary>
    private static uint Renumber(ColorSkipsBlock? skips, int region, int channel, byte value) =>
        skips is null ? value : skips.Compact(region, channel, value);

    /// <summary>
    /// The ordering of a palette that writes smallest.
    /// </summary>
    /// <remarks>
    /// A palette ramps in a channel when that channel steps evenly from one colour to the next,
    /// which depends entirely on the order the colours are in. Sorting by the whole packed
    /// value orders them by alpha first and leaves red almost arbitrary, so a palette that would
    /// ramp cleanly in red often does not appear to. Every ordering costs the same to read, so
    /// the one that writes smallest is simply taken - and the rule is the same wherever it is
    /// asked, so two tiles holding the same colours still agree and share one block.
    /// </remarks>
    private static uint[] CheapestOrder(uint[] colors)
    {
        if (colors.Length < 3)
        {
            return colors;
        }

        uint[] best = colors;
        long bestCost = OrderCost(colors);

        for (int channel = 0; channel < ChannelMirrors.ChannelCount; channel++)
        {
            uint[] candidate = [.. colors];
            int by = channel;

            // Ties broken by the whole value, so the ordering does not depend on where the
            // colours happened to arrive from.
            Array.Sort(candidate, (first, second) =>
            {
                int order = ChannelMirrors.ChannelOf(first, by).CompareTo(ChannelMirrors.ChannelOf(second, by));
                return order != 0 ? order : first.CompareTo(second);
            });

            long cost = OrderCost(candidate);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>What one ordering of a palette would occupy.</summary>
    private static long OrderCost(uint[] colors)
    {
        var palette = new MaskPalleteBlock { PaletteId = 1, Colors = colors };
        palette.ChooseStorage();
        return MeasureBlock(palette);
    }

    /// <summary>Writes one tile's mask and colour entries against its assigned profile.</summary>
    private static IllustrationBlock? BuildTile(
        byte[] rgba,
        TileScan tile,
        ColorProfileBlock profile,
        Dictionary<uint[], uint> paletteIds,
        ColorSkipsBlock? skips,
        MaskMode mode,
        bool useBackground,
        Brush[] brushes,
        bool[]? settled)
    {
        bool masked = mode == MaskMode.PerPixel;
        bool linear = mode == MaskMode.Linear;
        bool paletted = mode == MaskMode.Palette;

        int positions = 0;
        for (int i = 0; i < tile.Offsets.Count; i++)
        {
            // A position a shape painted is settled, exactly as one the image mask claimed is:
            // the mask and the entries never see it.
            if (tile.Offsets[i] != TileScan.ClaimedPixel && !(settled is not null && settled[i]))
            {
                positions++;
            }
        }

        int indexBits = paletted ? MaskPalleteBlock.BitsFor(tile.Palette.Length) : 0;
        var mask = masked ? new byte[(positions + 7) / 8]
            : paletted ? new byte[MaskPalleteBlock.MaskBytes(positions, indexBits)]
            : [];

        // A linear mask is written as run lengths afterwards, so what it needs first is simply
        // which positions are covered.
        var covered = linear ? new bool[positions] : [];

        var slots = new Dictionary<uint, uint>();
        for (int i = 0; i < tile.Palette.Length; i++)
        {
            slots[tile.Palette[i]] = (uint)i;
        }

        uint escape = (uint)tile.Palette.Length;
        var colors = new List<uint>();

        int position = -1;
        for (int i = 0; i < tile.Offsets.Count; i++)
        {
            long offset = tile.Offsets[i];
            if (offset == TileScan.ClaimedPixel || (settled is not null && settled[i]))
            {
                continue;
            }

            position++;

            if (paletted)
            {
                uint index;
                if (offset < 0)
                {
                    // Outside the image. It must still name a palette entry rather than escape,
                    // because the escape tells the decoder to take an entry and none is written
                    // for a pixel that does not exist. The colour is discarded on the way out.
                    index = 0;
                }
                else
                {
                    uint pixel = Pack(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
                    index = slots.TryGetValue(pixel, out uint slot) ? slot : escape;
                }

                MaskPalleteBlock.WriteIndex(mask, position, indexBits, index);
            }

            if (offset < 0)
            {
                // Nothing is drawn beyond the edge of the image. Only a mask can say so, which
                // is why a tile hanging off the edge cannot go without one.
                if (!masked && !linear && !paletted)
                {
                    return null;
                }

                continue;
            }

            uint packed = Pack(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
            if (useBackground && packed == tile.Stats.DominantColor)
            {
                continue;
            }

            if (paletted && slots.ContainsKey(packed))
            {
                continue;
            }

            // The profile was fitted to the pixels one way of masking stores. Another way may
            // store a pixel it never saw - the background colour, or one the palette named -
            // and there may be no number for it.
            if (!Representable(profile, rgba, offset, skips, tile.Region))
            {
                return null;
            }

            if (masked)
            {
                mask[position >> 3] |= (byte)(1 << (position & 7));
            }
            else if (linear)
            {
                covered[position] = true;
            }

            AppendOffsets(profile, rgba, offset, colors, skips, tile.Region);
        }

        if (linear)
        {
            mask = LinearMask(covered);
        }

        uint[] entries = [.. colors];

        return new IllustrationBlock
        {
            MaskMode = mode,
            ColorProfileId = profile.ProfileId,
            MaskPaletteId = tile.Palette.Length > 0 ? paletteIds[tile.Palette] : 0,
            HasBrushes = brushes.Length > 0,
            Brushes = brushes,
            UseOutOfMaskColor = useBackground,
            OutOfMaskColor = useBackground ? tile.Stats.DominantColor : 0,
            ColorCount = (uint)entries.Length,
            Mask = mask,
            Colors = entries,

            // Packed here as well as at save time, because choosing between a block and a link
            // means comparing what each would actually occupy.
            PackedColors = ColorBitPacker.Pack(profile, entries),
        };
    }

    /// <summary>
    /// The cheapest way to mask one tile, measured rather than guessed.
    /// </summary>
    /// <remarks>
    /// Every way of masking a tile is built and weighed: no mask at all, a bit per pixel, runs
    /// of covered and uncovered, and a palette index per pixel - each with and without the
    /// dominant colour held back. Which wins depends on the tile: a bit a pixel suits scattered
    /// detail, runs suit a shape with clean edges, and a palette suits a handful of colours
    /// repeated. Building all of them costs a few passes over a tile and settles the question
    /// exactly, where the estimate that used to decide it could only guess.
    /// </remarks>
    private static IllustrationBlock? BestMask(
        byte[] rgba,
        Header header,
        TileScan tile,
        ColorProfileBlock profile,
        Dictionary<uint[], uint> paletteIds,
        ColorSkipsBlock? skips,
        EncoderOptions options)
    {
        (MaskMode Mode, bool Background)[] candidates =
        [
            (MaskMode.Disabled, false),
            (MaskMode.PerPixel, false),
            (MaskMode.PerPixel, true),
            (MaskMode.Linear, false),
            (MaskMode.Linear, true),
            (MaskMode.Palette, false),
        ];

        // The shapes are fitted once and then every way of masking what is left is measured
        // against every way of masking the whole tile. A shape settles its pixels for a handful
        // of bytes, but it also breaks up the mask, so which wins is not a thing to assume.
        Brush[] brushes = FitBrushes(rgba, header, tile, options);

        IllustrationBlock? best = null;
        long bestCost = long.MaxValue;

        Brush[][] attempts = brushes.Length > 0 ? [[], brushes] : [[]];

        foreach (Brush[] shapes in attempts)
        {
            bool[]? settled = shapes.Length > 0 ? Settled(header, shapes) : null;

            foreach ((MaskMode mode, bool background) in candidates)
            {
                if (mode == MaskMode.Palette && tile.Palette.Length == 0)
                {
                    continue;
                }

                // Holding a colour back only means anything when there is one worth holding.
                if (background && tile.Stats.DominantCount == 0)
                {
                    continue;
                }

                IllustrationBlock? block = BuildTile(
                    rgba, tile, profile, paletteIds, skips, mode, background, shapes, settled);

                if (block is null)
                {
                    continue;
                }

                long cost = MeasureBlock(block);

                // A shape has a margin to clear: it reads as well as the pixels it replaced but
                // compresses worse, so it is taken when it is worth the run it breaks up rather
                // than merely equal to it.
                if (shapes.Length > 0)
                {
                    cost += (long)(cost * options.BrushMargin);
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = block;
                }
            }
        }

        return best;
    }

    /// <summary>The shapes worth painting into one tile, in the order they are painted.</summary>
    /// <remarks>
    /// <para>
    /// Shapes are proposed from what the tile actually holds and then painted with the very code
    /// the decoder paints them with: a candidate is kept only where every pixel it covers comes
    /// out exactly as the picture has it. Nothing here can cost accuracy - a shape that is not
    /// quite right is simply dropped, and the tile writes those pixels out as it always did.
    /// </para>
    /// <para>
    /// A gradient is tried over the whole tile first, because a tile that is one ramp of light
    /// is answered by a single shape and there is nothing left to fit. Otherwise the commonest
    /// colours are offered a box, a wedge and a stroke in turn, the one settling the most pixels
    /// is taken, and what it leaves goes round again.
    /// </para>
    /// <para>
    /// Positions outside the picture and positions the image mask already claimed are free to
    /// cover: nothing is drawn there either way, so a shape may reach across them, and doing so
    /// often lets one box stand where two would otherwise be needed.
    /// </para>
    /// </remarks>
    private static Brush[] FitBrushes(byte[] rgba, Header header, TileScan tile, EncoderOptions options)
    {
        int width = (int)header.BlockWidth;
        int height = (int)header.BlockHeight;
        int count = width * height;

        if (!options.Brushes || options.BrushesPerTile <= 0 || count == 0
            || tile.Offsets.Count < count || width > Brush.MaxCoordinate || height > Brush.MaxCoordinate)
        {
            return [];
        }

        var colors = new uint[count];
        var real = new bool[count];

        for (int i = 0; i < count; i++)
        {
            long offset = tile.Offsets[i];
            real[i] = offset >= 0;

            if (real[i])
            {
                colors[i] = Pack(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
            }
        }

        var settled = new bool[count];

        // One ramp across the whole tile leaves nothing to fit beside it.
        if (FitGradient(colors, real, width, height) is Brush ramp)
        {
            return [ramp];
        }

        var brushes = new List<Brush>();

        while (brushes.Count < options.BrushesPerTile)
        {
            Brush? best = null;
            int most = options.BrushMinimumPixels - 1;

            foreach (uint color in CommonColors(colors, real, settled, options.BrushColorsPerTile))
            {
                foreach (Brush candidate in ShapesFor(color, colors, real, settled, width, height))
                {
                    int paints = Paints(candidate, colors, real, settled, width, height);
                    if (paints > most)
                    {
                        most = paints;
                        best = candidate;
                    }
                }
            }

            if (best is null)
            {
                break;
            }

            brushes.Add(best);

            for (uint y = 0; y < height; y++)
            {
                for (uint x = 0; x < width; x++)
                {
                    if (best.Covers(x, y))
                    {
                        settled[(y * width) + x] = true;
                    }
                }
            }
        }

        return [.. brushes];
    }

    /// <summary>
    /// How many of a tile's own pixels a shape would settle, or -1 when it would paint one of
    /// them the wrong colour.
    /// </summary>
    private static int Paints(
        Brush brush,
        uint[] colors,
        bool[] real,
        bool[] settled,
        int width,
        int height)
    {
        int paints = 0;

        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                if (!brush.Covers(x, y))
                {
                    continue;
                }

                int at = (int)((y * width) + x);

                // Painting over a shape already accepted would change what that one drew, so
                // the two are never allowed to overlap.
                if (settled[at])
                {
                    return -1;
                }

                if (!real[at])
                {
                    continue;
                }

                if (brush.ColorAt(x, y) != colors[at])
                {
                    return -1;
                }

                paints++;
            }
        }

        return paints;
    }

    /// <summary>The colours a tile still has most of, commonest first.</summary>
    private static uint[] CommonColors(uint[] colors, bool[] real, bool[] settled, int take)
    {
        var counts = new Dictionary<uint, int>();

        for (int i = 0; i < colors.Length; i++)
        {
            if (real[i] && !settled[i])
            {
                counts[colors[i]] = counts.GetValueOrDefault(colors[i]) + 1;
            }
        }

        return [.. counts.OrderByDescending(each => each.Value).Take(Math.Max(0, take)).Select(each => each.Key)];
    }

    /// <summary>Every shape worth trying for one colour of a tile.</summary>
    private static IEnumerable<Brush> ShapesFor(
        uint color,
        uint[] colors,
        bool[] real,
        bool[] settled,
        int width,
        int height)
    {
        // Where the colour is, and where a shape may reach without painting anything wrong.
        var wanted = new List<(int X, int Y)>();
        var allowed = new bool[colors.Length];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int at = (y * width) + x;
                if (settled[at])
                {
                    continue;
                }

                if (!real[at])
                {
                    allowed[at] = true;
                }
                else if (colors[at] == color)
                {
                    allowed[at] = true;
                    wanted.Add((x, y));
                }
            }
        }

        if (wanted.Count == 0)
        {
            yield break;
        }

        if (LargestBox(allowed, width, height) is (int boxX, int boxY, int boxWidth, int boxHeight))
        {
            yield return new Brush
            {
                Kind = BrushKind.Rectangle,
                X = (uint)boxX,
                Y = (uint)boxY,
                Width = (uint)boxWidth,
                Height = (uint)boxHeight,
                Color = color,
            };
        }

        // Three corners and everything between them: the shape a wedge of light or the corner of
        // a drawn form makes, which no box can say.
        List<(int X, int Y)> corners = Hull(wanted);
        if (corners.Count == 3)
        {
            yield return new Brush
            {
                Kind = BrushKind.Triangle,
                X0 = (uint)corners[0].X,
                Y0 = (uint)corners[0].Y,
                X1 = (uint)corners[1].X,
                Y1 = (uint)corners[1].Y,
                X2 = (uint)corners[2].X,
                Y2 = (uint)corners[2].Y,
                Color = color,
            };
        }

        if (FitLine(wanted, color) is Brush stroke)
        {
            yield return stroke;
        }
    }

    /// <summary>The largest box of allowed positions, or <see langword="null"/> when there is none.</summary>
    /// <remarks>
    /// The usual histogram walk: how far the allowed run above each position reaches, then the
    /// widest box each of those heights can be part of, kept with a stack so the whole tile is
    /// one pass rather than one per candidate box.
    /// </remarks>
    private static (int X, int Y, int Width, int Height)? LargestBox(bool[] allowed, int width, int height)
    {
        var heights = new int[width];
        (int X, int Y, int Width, int Height)? best = null;
        int bestArea = 0;

        // A stack of columns whose box is still open, each with the position it started at.
        var stack = new Stack<(int Start, int Height)>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                heights[x] = allowed[(y * width) + x] ? heights[x] + 1 : 0;
            }

            stack.Clear();

            for (int x = 0; x <= width; x++)
            {
                int now = x < width ? heights[x] : 0;
                int start = x;

                while (stack.Count > 0 && stack.Peek().Height >= now)
                {
                    (int openedAt, int tall) = stack.Pop();
                    int area = tall * (x - openedAt);

                    if (area > bestArea && tall > 0)
                    {
                        bestArea = area;
                        best = (openedAt, y - tall + 1, x - openedAt, tall);
                    }

                    start = openedAt;
                }

                if (now > 0)
                {
                    stack.Push((start, now));
                }
            }
        }

        return best;
    }

    /// <summary>The corners of the smallest shape containing every given position.</summary>
    /// <remarks>
    /// Andrew's monotone chain, with positions on a straight edge dropped: what comes back is
    /// the corners alone, so three of them means the colour really does fill a triangle.
    /// </remarks>
    private static List<(int X, int Y)> Hull(List<(int X, int Y)> points)
    {
        if (points.Count < 3)
        {
            return points;
        }

        var sorted = new List<(int X, int Y)>(points);
        sorted.Sort((first, second) => first.X != second.X ? first.X.CompareTo(second.X) : first.Y.CompareTo(second.Y));

        var hull = new List<(int X, int Y)>();

        for (int pass = 0; pass < 2; pass++)
        {
            int floor = hull.Count;

            foreach ((int X, int Y) point in pass == 0 ? sorted : Enumerable.Reverse(sorted))
            {
                while (hull.Count >= floor + 2 && Turn(hull[^2], hull[^1], point) <= 0)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(point);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;

        static long Turn((int X, int Y) from, (int X, int Y) by, (int X, int Y) to) =>
            ((long)(by.X - from.X) * (to.Y - from.Y)) - ((long)(by.Y - from.Y) * (to.X - from.X));
    }

    /// <summary>
    /// A stroke through the given positions, or <see langword="null"/> when no width covers
    /// them all.
    /// </summary>
    /// <remarks>
    /// The two positions furthest apart are the ends, and the width is then the narrowest that
    /// reaches every one of the rest - found by asking the stroke itself rather than by
    /// measuring, so what is proposed is what the decoder will paint.
    /// </remarks>
    private static Brush? FitLine(List<(int X, int Y)> points, uint color)
    {
        if (points.Count < 2)
        {
            return null;
        }

        (int X, int Y) from = points[0];
        (int X, int Y) to = points[0];
        long apart = -1;

        // The ends of a stroke are among the positions furthest out, so only those are compared
        // rather than every position against every other.
        foreach ((int X, int Y) first in Extremes(points))
        {
            foreach ((int X, int Y) second in Extremes(points))
            {
                long across = (long)first.X - second.X;
                long down = (long)first.Y - second.Y;
                long distance = (across * across) + (down * down);

                if (distance > apart)
                {
                    apart = distance;
                    from = first;
                    to = second;
                }
            }
        }

        var stroke = new Brush
        {
            Kind = BrushKind.Line,
            X0 = (uint)from.X,
            Y0 = (uint)from.Y,
            X1 = (uint)to.X,
            Y1 = (uint)to.Y,
            Color = color,
        };

        // Wider always covers more, so the narrowest that covers everything is found by halving
        // the range rather than by trying every width.
        uint low = 1;
        uint high = Brush.MaxCoordinate;

        if (!Reaches(high))
        {
            return null;
        }

        while (low < high)
        {
            uint middle = low + ((high - low) / 2);
            if (Reaches(middle))
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        stroke.Stroke = low;
        return stroke;

        bool Reaches(uint width)
        {
            stroke.Stroke = width;
            return points.All(point => stroke.Covers((uint)point.X, (uint)point.Y));
        }
    }

    /// <summary>The positions furthest out in each direction, which is where an end can be.</summary>
    private static IEnumerable<(int X, int Y)> Extremes(List<(int X, int Y)> points)
    {
        (int X, int Y)[] found = new (int X, int Y)[8];
        var best = new long[8];

        for (int i = 0; i < 8; i++)
        {
            best[i] = long.MinValue;
        }

        foreach ((int X, int Y) point in points)
        {
            Consider(0, point.X);
            Consider(1, -point.X);
            Consider(2, point.Y);
            Consider(3, -point.Y);
            Consider(4, point.X + point.Y);
            Consider(5, -(point.X + point.Y));
            Consider(6, point.X - point.Y);
            Consider(7, point.Y - point.X);

            void Consider(int which, long score)
            {
                if (score > best[which])
                {
                    best[which] = score;
                    found[which] = point;
                }
            }
        }

        return found.Distinct();
    }

    /// <summary>
    /// A ramp across the whole tile, or <see langword="null"/> when the tile is not one.
    /// </summary>
    /// <remarks>
    /// Each channel is fitted on its own, because they are stored on their own: a wall that
    /// darkens to the left while its alpha stays flat is still one ramp. The step is guessed
    /// from how far the channel moved between two positions and then tried against every pixel,
    /// with a step either side of the guess in case the division rounded the wrong way.
    /// </remarks>
    private static Brush? FitGradient(uint[] colors, bool[] real, int width, int height)
    {
        var steps = new int[ChannelMirrors.ChannelCount];
        var down = new int[ChannelMirrors.ChannelCount];
        var bases = new uint[ChannelMirrors.ChannelCount];

        for (int channel = 0; channel < ChannelMirrors.ChannelCount; channel++)
        {
            if (!FitChannel(channel, out steps[channel], out down[channel], out bases[channel]))
            {
                return null;
            }
        }

        uint color = 0;
        for (int channel = 0; channel < ChannelMirrors.ChannelCount; channel++)
        {
            color |= bases[channel] << (channel * 8);
        }

        return new Brush
        {
            Kind = BrushKind.Gradient,
            X = 0,
            Y = 0,
            Width = (uint)width,
            Height = (uint)height,
            Color = color,
            StepX = steps,
            StepY = down,
        };

        bool FitChannel(int channel, out int across, out int downward, out uint start)
        {
            across = 0;
            downward = 0;
            start = 0;

            (int X, int Y)? anchor = null;
            for (int y = 0; y < height && anchor is null; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (real[(y * width) + x])
                    {
                        anchor = (x, y);
                        break;
                    }
                }
            }

            if (anchor is not (int anchorX, int anchorY))
            {
                // Nothing of this tile is drawn, so any ramp fits it and none is worth writing.
                return false;
            }

            int at = Value(channel, anchorX, anchorY);

            foreach (int guessX in Guesses(channel, anchorX, anchorY, true))
            {
                foreach (int guessY in Guesses(channel, anchorX, anchorY, false))
                {
                    // The colour at the tile's own corner, worked back from the anchor through
                    // the very shift the decoder applies, so the two cannot disagree.
                    long origin = at - (((guessX * (long)anchorX) + (guessY * (long)anchorY)) >> 8);
                    if (origin is < 0 or > 255)
                    {
                        continue;
                    }

                    if (Holds(channel, guessX, guessY, (uint)origin))
                    {
                        across = guessX;
                        downward = guessY;
                        start = (uint)origin;
                        return true;
                    }
                }
            }

            return false;
        }

        // How far the channel might move per pixel, from the furthest position sharing a row or
        // a column with the anchor, and a step either side of that in case of rounding.
        IEnumerable<int> Guesses(int channel, int anchorX, int anchorY, bool across)
        {
            int at = Value(channel, anchorX, anchorY);
            int step = 0;

            for (int i = (across ? width : height) - 1; i > (across ? anchorX : anchorY); i--)
            {
                int x = across ? i : anchorX;
                int y = across ? anchorY : i;

                if (!real[(y * width) + x])
                {
                    continue;
                }

                int span = across ? i - anchorX : i - anchorY;
                step = (Value(channel, x, y) - at) * 256 / span;
                break;
            }

            yield return step;
            yield return step - 1;
            yield return step + 1;
            yield return 0;
        }

        bool Holds(int channel, int across, int downward, uint origin)
        {
            var brush = new Brush
            {
                Kind = BrushKind.Gradient,
                Width = (uint)width,
                Height = (uint)height,
                Color = origin << (channel * 8),
                StepX = [0, 0, 0, 0],
                StepY = [0, 0, 0, 0],
            };

            brush.StepX[channel] = across;
            brush.StepY[channel] = downward;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (real[(y * width) + x]
                        && ((brush.ColorAt((uint)x, (uint)y) >> (channel * 8)) & 0xFF) != Value(channel, x, y))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        int Value(int channel, int x, int y) => (int)((colors[(y * width) + x] >> (channel * 8)) & 0xFF);
    }

    /// <summary>Whether two tiles paint the same shapes in the same order.</summary>
    private static bool SameBrushes(Brush[] left, Brush[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!left[i].Matches(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Which positions of a tile a set of shapes paints.</summary>
    private static bool[] Settled(Header header, Brush[] brushes)
    {
        int width = (int)header.BlockWidth;
        int height = (int)header.BlockHeight;
        var settled = new bool[width * height];

        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                foreach (Brush brush in brushes)
                {
                    if (brush.Covers(x, y))
                    {
                        settled[(y * width) + x] = true;
                        break;
                    }
                }
            }
        }

        return settled;
    }

    /// <summary>
    /// Run lengths alternating covered and uncovered, starting covered, as a linear mask reads.
    /// </summary>
    /// <remarks>
    /// A run is a byte, so a longer stretch is written as several with a run of nothing between
    /// them - which keeps the alternation intact and costs one byte per 255 pixels.
    /// </remarks>
    private static byte[] LinearMask(bool[] covered)
    {
        var runs = new List<byte>();
        bool expecting = true;
        int at = 0;

        while (at < covered.Length)
        {
            int run = 0;
            while (at + run < covered.Length && covered[at + run] == expecting && run < 255)
            {
                run++;
            }

            runs.Add((byte)run);

            if (run == 255 && at + run < covered.Length && covered[at + run] == expecting)
            {
                // The stretch goes on: a run of nothing hands it back to the same side.
                runs.Add(0);
                at += run;
                continue;
            }

            at += run;
            expecting = !expecting;
        }

        return [.. runs];
    }

    /// <summary>
    /// Appends one pixel's offsets above their minimums, one entry per varying channel in
    /// R, G, B, A order. Static channels contribute nothing.
    /// </summary>
    private static void AppendOffsets(
        ColorProfileBlock profile,
        byte[] rgba,
        long offset,
        List<uint> colors,
        ColorSkipsBlock? skips,
        int region)
    {
        ColorSkipsBlock? renumber = profile.UsesGlobalSkips ? skips : null;

        Append(ColorProfileBlock.RedNonStaticFlag, 0, profile.StaticColorR, rgba[offset]);
        Append(ColorProfileBlock.GreenNonStaticFlag, 1, profile.StaticColorG, rgba[offset + 1]);
        Append(ColorProfileBlock.BlueNonStaticFlag, 2, profile.StaticColorB, rgba[offset + 2]);
        Append(ColorProfileBlock.AlphaNonStaticFlag, 3, profile.StaticColorA, rgba[offset + 3]);

        void Append(uint flag, int channel, uint minimum, byte value)
        {
            if (profile.IsNonStatic(flag))
            {
                colors.Add(profile.CompactOffset(flag, Renumber(renumber, region, channel, value) - minimum));
            }
        }
    }

    /// <summary>Packs a colour with red in the lowest byte, matching the decoder.</summary>
    private static uint Pack(byte r, byte g, byte b, byte a) =>
        r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    /// <summary>How many bits it takes to hold every value from 0 to <paramref name="range"/>.</summary>
    private static int BitsNeeded(uint range)
    {
        int bits = 1;
        while (bits < 32 && (1u << bits) - 1 < range)
        {
            bits++;
        }

        return bits;
    }

    /// <summary>
    /// The profiles delta blocks read their differences by, numbered after the shared ones.
    /// </summary>
    /// <remarks>
    /// Tiles that shift the same way want the same profile, and a picture where a whole region
    /// steps one shade at a time produces the same one over and over. Keeping them by shape
    /// means that profile is written once.
    /// </remarks>
    private sealed class DeltaProfiles(uint firstId)
    {
        private readonly Dictionary<byte[], ColorProfileBlock> _byShape = new(SequenceComparer<byte>.Instance);
        private uint _nextId = firstId;

        /// <summary>The id a profile would take if one were written now.</summary>
        /// <remarks>
        /// Candidates are built and measured whether or not they are used, so a profile is
        /// numbered speculatively and only claims the number if its block is kept. Claiming it
        /// on the way past would leave later tiles naming a profile the file never wrote.
        /// </remarks>
        public uint NextId => _nextId;

        /// <summary>An identical profile already written, or <see langword="null"/>.</summary>
        public ColorProfileBlock? Find(ColorProfileBlock candidate) =>
            _byShape.TryGetValue(Shape(candidate), out ColorProfileBlock? existing) ? existing : null;

        /// <summary>Records a profile that is being written, claiming its number.</summary>
        public void Commit(ColorProfileBlock profile)
        {
            _byShape[Shape(profile)] = profile;
            _nextId++;
        }

        /// <summary>
        /// What a profile looks like to a reader, which is what decides whether two of them are
        /// the same one. The id is left out, since that is what is being decided.
        /// </summary>
        private static byte[] Shape(ColorProfileBlock profile)
        {
            uint id = profile.ProfileId;
            profile.ProfileId = 0;

            var package = new BitPackage();
            profile.Write(package);
            profile.ProfileId = id;

            return package.Export();
        }
    }

    /// <summary>Compares arrays by their contents, for looking tiles up by mask or entries.</summary>
    private sealed class SequenceComparer<T> : IEqualityComparer<T[]>
        where T : IEquatable<T>
    {
        public static readonly SequenceComparer<T> Instance = new();

        public bool Equals(T[]? left, T[]? right) =>
            left is null || right is null ? ReferenceEquals(left, right) : left.AsSpan().SequenceEqual(right);

        public int GetHashCode(T[] value)
        {
            var hash = new HashCode();
            hash.Add(value.Length);

            foreach (T item in value)
            {
                hash.Add(item);
            }

            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// Compares illustration blocks by what they hold, so two tiles that would encode
    /// identically are recognised as the same.
    /// </summary>
    private sealed class TileContentComparer : IEqualityComparer<IllustrationBlock>
    {
        public static readonly TileContentComparer Instance = new();

        public bool Equals(IllustrationBlock? left, IllustrationBlock? right)
        {
            if (left is null || right is null)
            {
                return ReferenceEquals(left, right);
            }

            // The colour profile counts: identical entries mean different colours under
            // different profiles, and so do the shapes: two tiles with the same mask over
            // different boxes are different pictures.
            return SameBrushes(left.Brushes, right.Brushes)
                && left.Flags == right.Flags
                && left.ColorProfileId == right.ColorProfileId
                && left.MaskPaletteId == right.MaskPaletteId
                && left.OutOfMaskColor == right.OutOfMaskColor
                && left.Mask.AsSpan().SequenceEqual(right.Mask)
                && left.Colors.AsSpan().SequenceEqual(right.Colors);
        }

        public int GetHashCode(IllustrationBlock block)
        {
            var hash = new HashCode();
            hash.Add(block.Flags);
            hash.Add(block.ColorProfileId);
            hash.Add(block.MaskPaletteId);
            hash.Add(block.OutOfMaskColor);
            hash.AddBytes(block.Mask);
            hash.Add(block.Brushes.Length);

            foreach (uint color in block.Colors)
            {
                hash.Add(color);
            }

            // Enough of a shape to spread tiles across buckets; the comparison settles the rest.
            foreach (Brush brush in block.Brushes)
            {
                hash.Add(brush.Kind);
                hash.Add(brush.Color);
            }

            return hash.ToHashCode();
        }

    }

    /// <summary>
    /// Finds earlier tiles that a tile nearly repeats, in any of the eight orientations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measuring every tile against every tile before it is square work: eight thousand tiles ask
    /// thirty million questions and nearly every answer is no. Instead each tile is boiled down
    /// to a four-by-four grid of average values, coarsely rounded, and only tiles whose grids
    /// come out identical are measured properly. Two tiles alike but for a handful of pixels have
    /// all but identical averages, so what this misses is a pair that straddles a rounding
    /// boundary - which is why every tile is filed twice, on boundaries half a step apart, so a
    /// pair has to straddle both to be lost.
    /// </para>
    /// <para>
    /// The grid is looked up once per orientation, turned and mirrored exactly as the pixels
    /// would be. That is how a tile repeating another one upside down is found without comparing
    /// anything pixel by pixel: the summary of the one, turned, is the summary of the other.
    /// </para>
    /// </remarks>
    private sealed class TileSimilarity
    {
        /// <summary>Cells the summary grid has along each axis.</summary>
        private const int CellsPerAxis = 4;

        /// <summary>Cells the summary grid has in all.</summary>
        private const int Cells = CellsPerAxis * CellsPerAxis;

        /// <summary>How coarsely a cell's average is rounded before it is filed.</summary>
        /// <remarks>
        /// Coarse enough that a few changed pixels cannot move a cell into another bucket, fine
        /// enough that unalike tiles do not crowd into one: sixteen levels leave four bits a
        /// cell, and sixteen cells of four bits are exactly one key.
        /// </remarks>
        private const int Levels = 16;

        private readonly byte[] _rgba;
        private readonly Header _header;
        private readonly EncoderOptions _options;
        private readonly Dictionary<int, byte[]> _summaries;
        private readonly Dictionary<ulong, List<int>>[] _grids;
        private readonly TileTransform[] _transforms;

        public TileSimilarity(byte[] rgba, Header header, EncoderOptions options, int count)
        {
            _rgba = rgba;
            _header = header;
            _options = options;
            _summaries = new Dictionary<int, byte[]>(count);
            _grids = [new Dictionary<ulong, List<int>>(), new Dictionary<ulong, List<int>>()];

            // Rows swapped for columns only land inside a square tile, so on any other shape the
            // four orientations that turn a tile on its side are not offered at all.
            bool square = header.BlockWidth == header.BlockHeight;
            var transforms = new List<TileTransform>();

            foreach (TileTransform transform in TileTransforms.All)
            {
                bool allowed = transform == TileTransform.None
                    || (options.MaskedTransforms && (square || !transform.NeedsSquare()));

                if (allowed)
                {
                    transforms.Add(transform);
                }
            }

            _transforms = [.. transforms];
        }

        /// <summary>Files a tile, so the tiles after it may be measured against it.</summary>
        public void Add(int slot)
        {
            if (Summarise(slot) is not byte[] summary)
            {
                return;
            }

            for (int at = 0; at < _grids.Length; at++)
            {
                ulong key = Key(summary, at * (Levels / 2));

                if (!_grids[at].TryGetValue(key, out List<int>? slots))
                {
                    slots = [];
                    _grids[at][key] = slots;
                }

                slots.Add(slot);
            }
        }

        /// <summary>Earlier tiles worth measuring this one against, likeliest first.</summary>
        /// <remarks>
        /// A tile may come back more than once under different orientations; each is a different
        /// candidate, since which one is cheapest is not something a summary can say. The tiles
        /// touching this one come back marked local, because a difference against one of those
        /// is judged without the margin the rest have to clear.
        /// </remarks>
        public IEnumerable<(int Target, TileTransform Transform, bool Local)> Candidates(int slot)
        {
            var seen = new HashSet<(int Target, TileTransform Transform)>();

            // The tiles touching this one are always worth a look, whatever they summarise to: a
            // difference pays most often where a drawing simply carries on.
            foreach (int neighbour in Neighbours(slot))
            {
                if (neighbour >= 0 && seen.Add((neighbour, TileTransform.None)))
                {
                    yield return (neighbour, TileTransform.None, true);
                }
            }

            if (_options.MaskedScanCandidates <= 0 || Summarise(slot) is not byte[] summary)
            {
                yield break;
            }

            var permuted = new byte[Cells];
            int found = 0;

            foreach (TileTransform transform in _transforms)
            {
                Permute(summary, transform, permuted);

                for (int at = 0; at < _grids.Length; at++)
                {
                    if (!_grids[at].TryGetValue(Key(permuted, at * (Levels / 2)), out List<int>? slots))
                    {
                        continue;
                    }

                    // Backwards, so the nearest tile that summarises alike is taken first. Near
                    // is what a drawing repeats over, and a near target is what the compressor
                    // behind this has most recently seen.
                    for (int i = slots.Count - 1; i >= 0; i--)
                    {
                        if (found >= _options.MaskedScanCandidates)
                        {
                            yield break;
                        }

                        if (slots[i] < slot && seen.Add((slots[i], transform)))
                        {
                            found++;
                            yield return (slots[i], transform, false);
                        }
                    }
                }
            }
        }

        /// <summary>The already-drawn tiles touching one.</summary>
        private int[] Neighbours(int slot)
        {
            int tilesX = (int)_header.TilesX;
            if (tilesX == 0 || slot == 0)
            {
                return [];
            }

            int column = slot % tilesX;
            int row = slot / tilesX;

            // Left, then the three above.
            return
            [
                column > 0 ? slot - 1 : -1,
                row > 0 ? slot - tilesX : -1,
                row > 0 && column > 0 ? slot - tilesX - 1 : -1,
                row > 0 && column + 1 < tilesX ? slot - tilesX + 1 : -1,
            ];
        }

        /// <summary>Lays one tile's summary out in the order another tile would read it.</summary>
        /// <remarks>
        /// The cell a position lands in is scattered, not gathered: the summary of this tile at
        /// cell <c>d</c> has to end up where the target's own summary keeps the cell that
        /// <c>d</c> copies from, so that the two keys come out equal when the tiles match.
        /// </remarks>
        private static void Permute(byte[] summary, TileTransform transform, byte[] into)
        {
            for (uint y = 0; y < CellsPerAxis; y++)
            {
                for (uint x = 0; x < CellsPerAxis; x++)
                {
                    transform.Map(x, y, CellsPerAxis, CellsPerAxis, out uint sourceX, out uint sourceY);
                    into[(sourceY * CellsPerAxis) + sourceX] = summary[(y * CellsPerAxis) + x];
                }
            }
        }

        /// <summary>The key a summary is filed under, rounded from a given starting point.</summary>
        private static ulong Key(byte[] cells, int offset)
        {
            ulong key = 0;

            for (int i = 0; i < Cells; i++)
            {
                ulong level = (ulong)Math.Min((cells[i] + offset) / Levels, Levels - 1);
                key = (key << 4) | level;
            }

            return key;
        }

        /// <summary>
        /// One tile boiled down to a cell per sixteenth of it, or <see langword="null"/> when
        /// the picture has no tiles at all.
        /// </summary>
        /// <remarks>
        /// The four channels are averaged together rather than kept apart. A summary only has to
        /// tell tiles that might match from tiles that cannot; keeping the channels separate
        /// would quarter the cells the key can hold to sharpen a decision that is measured
        /// exactly a moment later anyway.
        /// </remarks>
        private byte[]? Summarise(int slot)
        {
            if (_summaries.TryGetValue(slot, out byte[]? cached))
            {
                return cached;
            }

            int tilesX = (int)_header.TilesX;
            if (tilesX == 0)
            {
                return null;
            }

            uint originX = (uint)(slot % tilesX) * _header.BlockWidth;
            uint originY = (uint)(slot / tilesX) * _header.BlockHeight;

            var totals = new long[Cells];
            var counts = new int[Cells];

            for (uint y = 0; y < _header.BlockHeight; y++)
            {
                uint imageY = originY + y;
                if (imageY >= _header.ImageHeight)
                {
                    break;
                }

                int row = (int)(y * CellsPerAxis / _header.BlockHeight) * CellsPerAxis;

                for (uint x = 0; x < _header.BlockWidth; x++)
                {
                    uint imageX = originX + x;
                    if (imageX >= _header.ImageWidth)
                    {
                        break;
                    }

                    int cell = row + (int)(x * CellsPerAxis / _header.BlockWidth);
                    long at = (((long)imageY * _header.ImageWidth) + imageX) * IllustrationDecoder.BytesPerPixel;

                    totals[cell] += _rgba[at] + _rgba[at + 1] + _rgba[at + 2] + _rgba[at + 3];
                    counts[cell]++;
                }
            }

            var summary = new byte[Cells];
            for (int i = 0; i < Cells; i++)
            {
                // A cell with nothing in it - a tile hanging over the edge of the picture - reads
                // as zero, which is why such a tile rarely finds anything but its neighbours.
                summary[i] = counts[i] == 0 ? (byte)0 : (byte)(totals[i] / (counts[i] * 4L));
            }

            _summaries[slot] = summary;
            return summary;
        }
    }

    /// <summary>What a single tile turned out to contain.</summary>
    private sealed class TileScan(
        List<long> offsets,
        ColorStatistics stats,
        uint[] candidatePalette,
        long paletteSaving,
        bool preferBackground,
        bool complete,
        bool fullyCovered)
    {
        /// <summary>Marks a pixel the image mask already took, which the tile skips entirely.</summary>
        public const long ClaimedPixel = -2;

        /// <summary>
        /// Buffer offset of each tile pixel: -1 where the tile leaves the image, and
        /// <see cref="ClaimedPixel"/> where the image mask has already dealt with it.
        /// </summary>
        public List<long> Offsets { get; } = offsets;

        /// <summary>What the colour scan found in this tile.</summary>
        public ColorStatistics Stats { get; } = stats;

        /// <summary>The palette this tile would like, before its group decides.</summary>
        public uint[] CandidatePalette { get; } = candidatePalette;

        /// <summary>Bits a palette would save this tile, not counting the palette block.</summary>
        public long PaletteSaving { get; } = paletteSaving;

        /// <summary>Whether masking the dominant colour beats storing every pixel.</summary>
        public bool PreferBackground { get; } = preferBackground;

        /// <summary>Whether the tile lies wholly inside the image.</summary>
        public bool Complete { get; } = complete;

        /// <summary>
        /// Whether the image mask claims every pixel of this tile, leaving nothing to store.
        /// </summary>
        public bool FullyCovered { get; } = fullyCovered;

        /// <summary>Set once the group has decided the palette is worth its block.</summary>
        public bool UsesPalette { get; set; }

        /// <summary>The colours the mask names directly, empty when no palette is used.</summary>
        public uint[] Palette => UsesPalette ? CandidatePalette : [];

        /// <summary>Whether the dominant colour is being masked out rather than stored.</summary>
        public bool UsesBackground { get; private set; }

        /// <summary>The mask mode this tile will be written with.</summary>
        public MaskMode Mode { get; private set; }

        /// <summary>The span each channel covers across the pixels this tile stores.</summary>
        public ChannelRange Range { get; private set; } = ChannelRange.Empty();

        /// <summary>How many pixels the tile stores a colour for.</summary>
        public long CoveredPixels { get; private set; }

        /// <summary>Which merged profile this tile ended up sharing.</summary>
        public int GroupIndex { get; set; }

        /// <summary>Which region of the picture this tile falls in.</summary>
        public int Region { get; set; }

        /// <summary>
        /// Which numbering this tile counts in: regions describing the same values share one.
        /// </summary>
        public int Numbering { get; set; }

        /// <summary>
        /// Works out the mask mode and the channel ranges, once it is known whether the palette
        /// survived. The ranges have to wait for that: a pixel the palette names never reaches
        /// the colour entries, so it must not widen the profile either.
        /// </summary>
        /// <summary>
        /// Marks the values this tile will store, and returns how many pixels it stores.
        /// </summary>
        /// <remarks>
        /// The same pixels <see cref="Finalise"/> measures, and for the same reason: a pixel
        /// the palette names or the background covers never reaches an entry, so it must not
        /// hold a value open in the numbering either.
        /// </remarks>
        public long CollectUsed(byte[] rgba, Span<ulong> used)
        {
            bool usesBackground = !UsesPalette && PreferBackground;
            var inPalette = new HashSet<uint>(Palette);
            long stored = 0;

            foreach (long offset in Offsets)
            {
                if (offset < 0)
                {
                    continue;
                }

                uint packed = Pack(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
                if ((usesBackground && packed == Stats.DominantColor) || inPalette.Contains(packed))
                {
                    continue;
                }

                stored++;
                for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
                {
                    byte value = rgba[offset + c];
                    used[(c * (ColorSkipsBlock.ChannelValues / 64)) + (value >> 6)] |= 1UL << (value & 63);
                }
            }

            return stored;
        }

        public void Finalise(byte[] rgba, ColorSkipsBlock? skips)
        {
            int region = Region;

            UsesBackground = !UsesPalette && PreferBackground;

            Mode = UsesPalette
                ? MaskMode.Palette
                : UsesBackground || !Complete ? MaskMode.PerPixel : MaskMode.Disabled;

            var inPalette = new HashSet<uint>(Palette);
            Range = ChannelRange.Empty();
            CoveredPixels = 0;

            foreach (long offset in Offsets)
            {
                // Both an absent pixel and one the image mask already took are nothing to this
                // tile: neither reaches the entries, so neither may widen the profile.
                if (offset < 0)
                {
                    continue;
                }

                uint packed = Pack(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]);
                if (UsesBackground && packed == Stats.DominantColor)
                {
                    continue;
                }

                if (inPalette.Contains(packed))
                {
                    continue;
                }

                CoveredPixels++;

                // Measured in the numbering the picture's skips leave behind, so the profile
                // this range becomes counts in the same values its entries will.
                Range.Include(
                    [
                        (byte)Renumber(skips, region, 0, rgba[offset]),
                        (byte)Renumber(skips, region, 1, rgba[offset + 1]),
                        (byte)Renumber(skips, region, 2, rgba[offset + 2]),
                        (byte)Renumber(skips, region, 3, rgba[offset + 3]),
                    ],
                    [rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3]]);
            }
        }
    }

    /// <summary>A set of tiles sharing one profile, and what that costs.</summary>
    private sealed class ProfileGroup(ChannelRange range, long cost)
    {
        /// <summary>Which region's numbering this group's tiles count in.</summary>
        public int Region { get; init; }

        public ChannelRange Range { get; private set; } = range;

        /// <summary>Bytes this group currently costs, kept in step with the range.</summary>
        public long Cost { get; private set; } = cost;

        public long CoveredPixels { get; private set; }

        public List<TileScan> Tiles { get; } = [];

        public void Add(TileScan tile)
        {
            Tiles.Add(tile);
            Range.Absorb(tile.Range);
            CoveredPixels += tile.CoveredPixels;
            Cost = GroupCost(Range, CoveredPixels);
        }

        public void Absorb(ProfileGroup other)
        {
            Tiles.AddRange(other.Tiles);
            Range.Absorb(other.Range);
            CoveredPixels += other.CoveredPixels;
            Cost = GroupCost(Range, CoveredPixels);
        }
    }

    /// <summary>The lowest and highest value each channel takes across a set of pixels.</summary>
    private sealed class ChannelRange
    {
        /// <summary>Words per channel in <see cref="_used"/>: 256 values, 64 bits each.</summary>
        private const int WordsPerChannel = 4;

        private readonly byte[] _min = [255, 255, 255, 255];
        private readonly byte[] _max = [0, 0, 0, 0];

        // Which of the 256 values each channel actually takes. Knowing the gaps, not just the
        // extremes, is what lets a channel using only 0-16 and 240-255 be stored in six bits.
        private readonly ulong[] _used = new ulong[4 * WordsPerChannel];

        /// <summary>True while no pixel has been seen, so the range constrains nothing.</summary>
        public bool IsEmpty { get; private set; } = true;

        /// <summary>
        /// Which channel pairs have held the same value for every pixel, indexed
        /// <c>(first * 4) + second</c>.
        /// </summary>
        private readonly bool[] _equal = [.. Enumerable.Repeat(true, 16)];

        public static ChannelRange Empty() => new();

        public void Include(byte r, byte g, byte b, byte a) => Include([r, g, b, a], [r, g, b, a]);

        /// <summary>
        /// Takes one pixel in, measured in the numbering the profile will count in but compared
        /// in the values it will decode to.
        /// </summary>
        /// <remarks>
        /// The two differ once the picture renumbers its channels: each channel drops its own
        /// unused values, so two channels holding the same colour can come out as different
        /// numbers, and two holding different colours as the same number. A channel copies
        /// another only when their colours match, because copying takes the value the source
        /// decodes to, not the number it was stored as.
        /// </remarks>
        public void Include(ReadOnlySpan<byte> renumbered, ReadOnlySpan<byte> raw)
        {
            ReadOnlySpan<byte> values = renumbered;

            // Two channels that have held the same value for every pixel so far can be stored
            // once. One pixel where they part is enough to settle it, so this only ever goes
            // from true to false.
            for (int first = 0; first < 4; first++)
            {
                for (int second = first + 1; second < 4; second++)
                {
                    if (raw[first] != raw[second])
                    {
                        _equal[(first * 4) + second] = false;
                    }
                }
            }

            for (int c = 0; c < 4; c++)
            {
                byte value = values[c];
                _min[c] = Math.Min(_min[c], value);
                _max[c] = Math.Max(_max[c], value);
                _used[(c * WordsPerChannel) + (value >> 6)] |= 1UL << (value & 63);
            }

            IsEmpty = false;
        }

        public ChannelRange Clone()
        {
            var copy = new ChannelRange { IsEmpty = IsEmpty };
            Array.Copy(_min, copy._min, 4);
            Array.Copy(_max, copy._max, 4);
            Array.Copy(_used, copy._used, _used.Length);
            return copy;
        }

        public void Absorb(ChannelRange other)
        {
            if (other.IsEmpty)
            {
                return;
            }

            for (int c = 0; c < 4; c++)
            {
                _min[c] = Math.Min(_min[c], other._min[c]);
                _max[c] = Math.Max(_max[c], other._max[c]);
            }

            for (int i = 0; i < _used.Length; i++)
            {
                _used[i] |= other._used[i];
            }

            // Two channels are only the same channel where they matched in both: a pair that
            // parted anywhere in either range has parted in the merged one.
            for (int i = 0; i < _equal.Length; i++)
            {
                _equal[i] &= other._equal[i];
            }

            IsEmpty = false;
        }

        /// <summary>Builds the profile that represents this range.</summary>
        /// <param name="coveredPixels">
        /// How many pixels the profile will encode, which decides whether a channel's skip runs
        /// are worth the bytes they cost.
        /// </param>
        public ColorProfileBlock ToProfile(uint profileId, long coveredPixels, bool usesGlobalSkips = false)
        {
            if (IsEmpty)
            {
                return new ColorProfileBlock { ProfileId = profileId };
            }

            var profile = new ColorProfileBlock
            {
                ProfileId = profileId,
                Flags = usesGlobalSkips ? ColorProfileBlock.GlobalSkipsFlag : 0,
                StaticColorR = _min[0],
                StaticColorG = _min[1],
                StaticColorB = _min[2],
                StaticColorA = _min[3],
            };

            // Copies first: a channel that always matched an earlier one stores nothing at all,
            // so it must be settled before anything works out widths or skips.
            for (int c = 1; c < 4; c++)
            {
                for (int source = 0; source < c; source++)
                {
                    if (!_equal[(source * 4) + c] || profile.IsMirrored(ChannelFlags[source]))
                    {
                        continue;
                    }

                    profile.SetMirror(ChannelFlags[c], ChannelFlags[source]);
                    break;
                }
            }

            var counts = new List<uint>();
            var runs = new List<uint>();

            for (int c = 0; c < 4; c++)
            {
                if (profile.IsMirrored(ChannelFlags[c]) || _min[c] == _max[c])
                {
                    // Flat across every pixel this profile covers, so it is stored once.
                    continue;
                }

                profile.SetNonStatic(ChannelFlags[c], true);

                int plainBits = BitsNeeded((uint)(_max[c] - _min[c]));
                uint[] channelRuns = SkipRuns(c, out int compactValues);
                int compactBits = BitsNeeded((uint)Math.Max(1, compactValues - 1));

                if (channelRuns.Length > 0 && compactBits < plainBits
                    && SkipsPay(channelRuns, plainBits - compactBits, coveredPixels))
                {
                    counts.Add((uint)(channelRuns.Length / 2));
                    runs.AddRange(channelRuns);
                    plainBits = compactBits;
                }
                else
                {
                    counts.Add(0);
                }

                profile.SetChannelSize(
                    ChannelFlags[c],
                    (uint)Math.Clamp(plainBits, 1, (int)ColorProfileBlock.MaxNonStaticColorSize));
            }

            if (runs.Count > 0)
            {
                profile.SetNonStatic(ColorProfileBlock.ColorSkipsFlag, true);
                profile.ColorSkipCounts = [.. counts];
                profile.ColorSkips = [.. runs];
            }

            return profile;
        }

        /// <summary>
        /// The gaps in one channel, as pairs of (usable run, skipped run). The trailing usable
        /// stretch is implied and not emitted.
        /// </summary>
        /// <param name="compactValues">
        /// How many values the compact range ends up holding, which sets the channel's width.
        /// This can exceed the values actually used, because combining runs pulls skipped
        /// values back in.
        /// </param>
        private uint[] SkipRuns(int channel, out int compactValues)
        {
            var pairs = new List<(uint Usable, uint Gap)>();
            uint usable = 0;
            uint gap = 0;

            for (int value = _min[channel]; value <= _max[channel]; value++)
            {
                if (IsUsed(channel, value))
                {
                    if (gap > 0)
                    {
                        // A gap has just closed, so the run before it is now final.
                        pairs.Add((usable, gap));
                        usable = 0;
                        gap = 0;
                    }

                    usable++;
                }
                else
                {
                    gap++;
                }
            }

            // The scan starts and ends on a used value, so what is left is the trailing run.
            uint trailing = usable;

            Combine(pairs, ref trailing);

            compactValues = (int)trailing;
            var runs = new List<uint>(pairs.Count * 2);
            foreach ((uint run, uint skipped) in pairs)
            {
                compactValues += (int)run;
                runs.Add(run);
                runs.Add(skipped);
            }

            return [.. runs];
        }

        /// <summary>
        /// Folds runs together until there are few enough to store, always sacrificing the
        /// narrowest gap first.
        /// </summary>
        /// <remarks>
        /// A channel that alternates used and unused values can describe far more gaps than the
        /// format allows. Absorbing a gap makes its values representable but unused, which costs
        /// compact range and possibly a bit of width - much cheaper than the alternative, which
        /// is a profile the format cannot store at all.
        /// </remarks>
        private static void Combine(List<(uint Usable, uint Gap)> pairs, ref uint trailing)
        {
            while (pairs.Count > ColorProfileBlock.MaxSkipRuns)
            {
                int narrowest = 0;
                for (int i = 1; i < pairs.Count; i++)
                {
                    if (pairs[i].Gap < pairs[narrowest].Gap)
                    {
                        narrowest = i;
                    }
                }

                if (narrowest < pairs.Count - 1)
                {
                    // Swallow the gap, joining this usable run to the one after it.
                    pairs[narrowest] = (
                        pairs[narrowest].Usable + pairs[narrowest].Gap + pairs[narrowest + 1].Usable,
                        pairs[narrowest + 1].Gap);
                    pairs.RemoveAt(narrowest + 1);
                }
                else
                {
                    // The last gap joins its run to the trailing stretch instead.
                    trailing += pairs[narrowest].Usable + pairs[narrowest].Gap;
                    pairs.RemoveAt(narrowest);
                }
            }
        }

        private bool IsUsed(int channel, int value) =>
            (_used[(channel * WordsPerChannel) + (value >> 6)] & (1UL << (value & 63))) != 0;

        /// <summary>
        /// Whether a channel's skip runs save more than they cost: the bits they shave off every
        /// covered pixel, against the bytes the runs themselves occupy in the profile.
        /// </summary>
        private static bool SkipsPay(uint[] runs, int bitsSaved, long coveredPixels)
        {
            long cost = VarSize((ulong)(runs.Length / 2));
            foreach (uint value in runs)
            {
                cost += VarSize(value);
            }

            return (bitsSaved * coveredPixels / 8) > cost;
        }

    }
}

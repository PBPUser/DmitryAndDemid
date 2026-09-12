using NEngineFormat.Core;
using NEngineFormat.StaticIllustration.Blocks;
using NEngineFormat.StaticIllustration.Data;

namespace NEngineFormat.StaticIllustration;

/// <summary>
/// Turns a header and its blocks into an RGBA8 pixel buffer that can be handed straight to a
/// texture upload.
/// </summary>
/// <remarks>
/// <para>
/// The format stores colours compressed against a <see cref="ColorProfileBlock"/>: a channel
/// is either static, in which case every pixel shares one value, or non-static, in which case
/// the profile's StaticColor is the channel's <b>minimum</b> and each pixel stores an offset
/// above it. This walks that back out to plain 8-bit RGBA.
/// </para>
/// <para><b>Assumptions.</b> The format does not pin these down yet, so they are choices:</para>
/// <list type="bullet">
/// <item>A channel the profile marks as copying another takes that channel's value and
/// consumes no entry of its own.</item>
/// <item>A profile marked as using the picture's colour skips counts in the numbering that
/// block leaves behind, and the value it arrives at is turned back into a channel value by
/// it.</item>
/// <item><see cref="IllustrationBlock.Colors"/> holds one entry per varying <b>channel</b>,
/// not per pixel: a covered pixel consumes as many consecutive entries as the profile has
/// non-static channels, in R, G, B, A order. Static channels consume none.</item>
/// <item>Channels are 8 bits on output; a minimum plus its offset is clamped to 255.</item>
/// <item><see cref="IllustrationBlock.OutOfMaskColor"/> is packed RGBA with red in the lowest byte.</item>
/// <item>Illustration blocks tile the image in row-major order, left to right then top to
/// bottom, sized by <see cref="Header.BlockWidth"/> and <see cref="Header.BlockHeight"/>.</item>
/// <item>A per-pixel mask reads least significant bit first within each byte; a linear mask is
/// run lengths alternating covered, uncovered, starting covered.</item>
/// <item>A palette mask packs one index per pixel, most significant bit first, at the width the
/// palette needs. An index naming a colour takes it; the escape index falls through to the
/// colour entries.</item>
/// <item>A <see cref="MaskedBlock"/> starts from the pixels of the tile it names, which is
/// always an earlier one, and its mask marks the pixels it replaces rather than the ones it
/// covers. It may read that tile turned or mirrored, which its flags say and which needs
/// square tiles when rows are swapped for columns.</item>
/// <item>A <see cref="Brush"/> paints its shape into the tile, and the positions it covers are
/// settled: the tile's mask and entries advance over what is left, so a shape costs nothing per
/// pixel. Where two brushes overlap, the later one is on top.</item>
/// <item>An <see cref="ImageMaskBlock"/> is consulted first and its pixels are finished with:
/// the tile covering them stores nothing at all for them, so tile masks and entries advance
/// only over the pixels it left behind.</item>
/// </list>
/// </remarks>
public static class IllustrationDecoder
{
    /// <summary>Bytes per pixel in the returned buffer.</summary>
    public const int BytesPerPixel = 4;

    /// <summary>
    /// Decodes every illustration block into one RGBA8 buffer of
    /// <c>ImageWidth * ImageHeight * 4</c> bytes.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A block names a colour profile that is not present, or runs out of colours.
    /// </exception>
    public static byte[] DecodeRgba(Header header, IReadOnlyList<BlockBase> blocks) =>
        Flatten(header, blocks, DecodeLayers(header, blocks, null));

    /// <summary>
    /// Every layer of the picture, each its own RGBA8 buffer, bottom layer first.
    /// </summary>
    /// <remarks>
    /// A picture that never mentions layers comes back as one, which is what every file written
    /// before them is. This is what a host with layers of its own wants; <see cref="DecodeRgba"/>
    /// is the same picture laid down into one buffer.
    /// </remarks>
    public static byte[][] DecodeLayers(Header header, IReadOnlyList<BlockBase> blocks) =>
        DecodeLayers(header, blocks, null);

    /// <summary>
    /// Lays every visible layer over the one below it, at the opacity each one asks for.
    /// </summary>
    /// <remarks>
    /// Source-over, which is what a blend mode of nothing in particular means. A layer whose
    /// mode the host understands is the host's business: the file carries that mode untouched
    /// and this is only what the picture looks like when nobody is there to apply it.
    /// </remarks>
    public static byte[] Flatten(Header header, IReadOnlyList<BlockBase> blocks, byte[][] layers)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(layers);

        if (layers.Length == 1)
        {
            return layers[0];
        }

        byte[] flat = new byte[(long)header.ImageWidth * header.ImageHeight * BytesPerPixel];
        if (layers.Length == 0)
        {
            return flat;
        }

        IReadOnlyList<IllustrationTiles.LayerSlice> slices = IllustrationTiles.Layers(blocks);

        for (int i = 0; i < layers.Length; i++)
        {
            LayerBlock? layer = i < slices.Count ? slices[i].Block : null;
            if (layer is { Hidden: true })
            {
                continue;
            }

            uint opacity = layer?.Opacity ?? LayerBlock.FullOpacity;
            if (opacity == 0)
            {
                continue;
            }

            Over(flat, layers[i], opacity);
        }

        return flat;
    }

    /// <summary>Lays one layer over what is already there.</summary>
    private static void Over(byte[] under, byte[] layer, uint opacity)
    {
        for (long at = 0; at + 3 < under.LongLength && at + 3 < layer.LongLength; at += BytesPerPixel)
        {
            // Rounded rather than truncated, so a layer at full opacity is left exactly as it is.
            uint alpha = (uint)((layer[at + 3] * opacity) + 127) / 255;
            if (alpha == 0)
            {
                continue;
            }

            if (alpha == 255)
            {
                under[at + 0] = layer[at + 0];
                under[at + 1] = layer[at + 1];
                under[at + 2] = layer[at + 2];
                under[at + 3] = 255;
                continue;
            }

            uint below = under[at + 3];
            uint kept = below * (255 - alpha) / 255;
            uint total = alpha + kept;

            if (total == 0)
            {
                continue;
            }

            for (int channel = 0; channel < 3; channel++)
            {
                uint mixed = ((layer[at + channel] * alpha) + (under[at + channel] * kept) + (total / 2)) / total;
                under[at + channel] = (byte)(mixed > 255 ? 255 : mixed);
            }

            under[at + 3] = (byte)total;
        }
    }

    /// <summary>
    /// Where every pixel of the picture got its colour, one <see cref="PixelOrigin"/> per pixel.
    /// </summary>
    /// <remarks>
    /// The same walk as <see cref="DecodeRgba"/>, recording what it decided rather than what it
    /// drew, so the two can never disagree about which pixels a file actually stores.
    /// </remarks>
    public static PixelOrigin[] DecodeOrigins(Header header, IReadOnlyList<BlockBase> blocks)
    {
        ArgumentNullException.ThrowIfNull(header);

        var origins = new PixelOrigin[(long)header.ImageWidth * header.ImageHeight];
        DecodeLayers(header, blocks, origins);
        return origins;
    }

    /// <summary>
    /// Decodes every layer, optionally recording where the first layer's pixels came from.
    /// </summary>
    /// <remarks>
    /// Origins are the bottom layer's: they answer where a file spent its bytes, and the one
    /// tool that asks works a layer at a time.
    /// </remarks>
    private static byte[][] DecodeLayers(Header header, IReadOnlyList<BlockBase> blocks, PixelOrigin[]? origins)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(blocks);
        header.Validate();

        IReadOnlyList<IllustrationTiles.LayerSlice> slices = IllustrationTiles.Layers(blocks);
        var layers = new byte[Math.Max(1, slices.Count)][];

        for (int i = 0; i < layers.Length; i++)
        {
            layers[i] = new byte[(long)header.ImageWidth * header.ImageHeight * BytesPerPixel];
        }

        if (header.IsEmpty)
        {
            return layers;
        }

        // Profiles first, links applied: a tile names an id and does not care whether the
        // profile behind it was written out or borrowed from another.
        IReadOnlyDictionary<uint, ColorProfileBlock> profiles = ProfileResolver.Resolve(blocks);

        Dictionary<uint, MaskPalleteBlock> palettes = [];
        foreach (BlockBase block in blocks)
        {
            if (block is MaskPalleteBlock palette)
            {
                palettes[palette.PaletteId] = palette;
            }
        }

        // The values no pixel of the picture uses, described once for every profile that
        // counts in them.
        ColorSkipsBlock? skips = null;
        foreach (BlockBase block in blocks)
        {
            if (block is ColorSkipsBlock found)
            {
                skips = found;
                break;
            }
        }

        // An image mask belongs to the layer it was written in, so each layer has its own, and
        // the run starts of each are worked out once rather than per pixel.
        var masks = new ImageMaskLookup?[layers.Length];
        var maskPalettes = new MaskPalleteBlock?[layers.Length];

        for (int i = 0; i < slices.Count && i < layers.Length; i++)
        {
            if (slices[i].Mask is ImageMaskBlock mask && slices[i].MaskPalette is MaskPalleteBlock palette)
            {
                masks[i] = new ImageMaskLookup(mask, palette, header.ImageWidth, header.ImageHeight);
                maskPalettes[i] = palette;
            }
        }

        // Links stand in for illustrations, so the tile run is resolved before anything is drawn.
        IReadOnlyList<IllustrationBlock?> tiles = IllustrationTiles.Resolve(header, blocks);
        int perLayer = Math.Max(1, (int)header.TileCount);

        for (int slot = 0; slot < tiles.Count; slot++)
        {
            // Slots go on counting across layers, so which layer a tile belongs to and where it
            // sits in that layer both follow from its number.
            int layerIndex = Math.Min(slot / perLayer, layers.Length - 1);
            int tileIndex = slot % perLayer;

            byte[] pixels = layers[layerIndex];
            ImageMaskLookup? imageMask = masks[layerIndex];
            MaskPalleteBlock? imagePalette = maskPalettes[layerIndex];

            // Only the bottom layer's origins are recorded; they answer where the file spent its
            // bytes, and a layer at a time is how that is read.
            PixelOrigin[]? layerOrigins = layerIndex == 0 ? origins : null;

            // A tile the image mask covers outright has no block at all; its pixels were
            // painted from the mask below, so there is nothing left to do for it.
            if (tiles[slot] is not IllustrationBlock illustration)
            {
                PaintCoveredTile(header, imageMask, imagePalette, tileIndex, pixels, layerOrigins);
                continue;
            }

            if (!profiles.TryGetValue(illustration.ColorProfileId, out ColorProfileBlock? profile))
            {
                throw new InvalidOperationException(
                    $"Illustration block {slot} names colour profile {illustration.ColorProfileId}, which is not in the file.");
            }

            MaskPalleteBlock? palette = null;
            if (illustration.MaskMode == MaskMode.Palette
                && !palettes.TryGetValue(illustration.MaskPaletteId, out palette))
            {
                throw new InvalidOperationException(
                    $"Illustration block {slot} names mask palette {illustration.MaskPaletteId}, which is not in the file.");
            }

            // A masked tile is a difference from one already drawn, so what it inherits is
            // copied in before its own pixels are laid over the top. The tile it points at may
            // belong to an earlier layer, which is often where the likeness is.
            if (illustration is MaskedBlock masked)
            {
                CopyTile(header, masked, slot, layers, perLayer);
            }

            DecodeTile(
                header, illustration, profile, palette, imageMask, imagePalette, tileIndex, pixels, layerOrigins, skips);
        }

        return layers;
    }

    /// <summary>Paints a tile whose every pixel the image mask claims.</summary>
    private static void PaintCoveredTile(
        Header header,
        ImageMaskLookup? imageMask,
        MaskPalleteBlock? imagePalette,
        int tileIndex,
        byte[] pixels,
        PixelOrigin[]? origins)
    {
        if (imageMask is null || imagePalette is null || header.TilesX == 0)
        {
            return;
        }

        uint originX = (uint)(tileIndex % header.TilesX) * header.BlockWidth;
        uint originY = (uint)(tileIndex / header.TilesX) * header.BlockHeight;

        for (uint y = 0; y < header.BlockHeight; y++)
        {
            for (uint x = 0; x < header.BlockWidth; x++)
            {
                uint imageX = originX + x;
                uint imageY = originY + y;

                if (imageX >= header.ImageWidth || imageY >= header.ImageHeight)
                {
                    continue;
                }

                long pixel = ((long)imageY * header.ImageWidth) + imageX;
                if (imageMask.ClaimedIndex(pixel) is not uint claimed)
                {
                    continue;
                }

                Rgba color = Rgba.FromPacked(imagePalette.Colors[claimed]);
                Mark(origins, pixel, PixelOrigin.ImageMask);
                long at = pixel * BytesPerPixel;
                pixels[at + 0] = color.R;
                pixels[at + 1] = color.G;
                pixels[at + 2] = color.B;
                pixels[at + 3] = color.A;
            }
        }
    }

    /// <summary>Copies the pixels a masked tile starts from into its own rectangle.</summary>
    /// <remarks>
    /// The target is always an earlier tile, so it has already been drawn and the buffer holds
    /// its finished pixels. A position the target does not have - it lies off the edge of the
    /// image - is left alone; the encoder marks those pixels as differing, so the block
    /// supplies them itself.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The target is not an earlier tile, or the block reads it turned on its side while the
    /// tiles are not square.
    /// </exception>
    private static void CopyTile(Header header, MaskedBlock masked, int slot, byte[][] layers, int perLayer)
    {
        uint tilesX = header.TilesX;
        if (tilesX == 0)
        {
            return;
        }

        if (masked.TargetTile >= (ulong)perLayer * (ulong)layers.Length)
        {
            throw new InvalidOperationException(
                $"Tile {slot} starts from tile {masked.TargetTile}, but the picture only has "
                + $"{(long)perLayer * layers.Length}.");
        }

        if (masked.TargetTile >= slot)
        {
            // Reading forward would copy pixels nothing has drawn yet, which would decode as
            // black rather than as an error.
            throw new InvalidOperationException(
                $"Tile {slot} starts from tile {masked.TargetTile}, which is not an earlier tile.");
        }

        // The target may be in an earlier layer: its pixels are drawn by then either way, and a
        // layer of one drawing often repeats another far more closely than its own neighbours do.
        byte[] pixels = layers[Math.Min(slot / perLayer, layers.Length - 1)];
        byte[] from = layers[Math.Min((int)masked.TargetTile / perLayer, layers.Length - 1)];
        int tileIndex = slot % perLayer;
        int targetTile = (int)masked.TargetTile % perLayer;

        TileTransform transform = masked.Transform;

        // Swapping rows for columns only lands inside the tile again when the two are the same
        // length; anywhere else it would read pixels of the tiles alongside.
        if (transform.NeedsSquare() && header.BlockWidth != header.BlockHeight)
        {
            throw new InvalidOperationException(
                $"Tile {slot} reads tile {masked.TargetTile} {transform.Describe()}, which needs "
                + $"square tiles, but they are {header.BlockWidth}x{header.BlockHeight}.");
        }

        uint originX = (uint)(tileIndex % tilesX) * header.BlockWidth;
        uint originY = (uint)(tileIndex / tilesX) * header.BlockHeight;
        uint fromX = (uint)(targetTile % tilesX) * header.BlockWidth;
        uint fromY = (uint)(targetTile / tilesX) * header.BlockHeight;

        for (uint y = 0; y < header.BlockHeight; y++)
        {
            for (uint x = 0; x < header.BlockWidth; x++)
            {
                transform.Map(x, y, header.BlockWidth, header.BlockHeight, out uint sourceX, out uint sourceY);

                if (originX + x >= header.ImageWidth || originY + y >= header.ImageHeight
                    || fromX + sourceX >= header.ImageWidth || fromY + sourceY >= header.ImageHeight)
                {
                    continue;
                }

                long to = (((long)(originY + y) * header.ImageWidth) + originX + x) * BytesPerPixel;
                long at = (((long)(fromY + sourceY) * header.ImageWidth) + fromX + sourceX) * BytesPerPixel;

                pixels[to + 0] = from[at + 0];
                pixels[to + 1] = from[at + 1];
                pixels[to + 2] = from[at + 2];
                pixels[to + 3] = from[at + 3];
            }
        }
    }

    private static void DecodeTile(
        Header header,
        IllustrationBlock illustration,
        ColorProfileBlock profile,
        MaskPalleteBlock? palette,
        ImageMaskLookup? imageMask,
        MaskPalleteBlock? imagePalette,
        int tileIndex,
        byte[] pixels,
        PixelOrigin[]? origins,
        ColorSkipsBlock? skips)
    {
        uint tilesX = header.TilesX;
        if (tilesX == 0)
        {
            return;
        }

        uint originX = (uint)(tileIndex % tilesX) * header.BlockWidth;
        uint originY = (uint)(tileIndex / tilesX) * header.BlockHeight;

        // Which of the picture's colour tables this tile counts in follows from where it sits,
        // so nothing is stored per tile to say so.
        int region = skips?.RegionOf(
            (uint)(tileIndex % (int)tilesX), (uint)(tileIndex / (int)tilesX), tilesX, header.TilesY) ?? 0;

        var mask = new MaskCursor(illustration, palette);
        var colors = new ColorCursor(illustration, profile, skips, region);

        // A masked tile has already been filled from the tile it points at, so a pixel its own
        // mask leaves alone is finished: there is nothing to paint over it.
        bool inherits = illustration is MaskedBlock;

        // With deltas the entries say how far the pixel has moved from the one copied in, so
        // what is already in the buffer is half the answer.
        bool deltas = illustration is MaskedBlock { IsDelta: true };

        Rgba outOfMask = illustration.UseOutOfMaskColor
            ? Rgba.FromPacked(illustration.OutOfMaskColor)
            : MinimumColor(profile, skips, region);

        for (uint y = 0; y < header.BlockHeight; y++)
        {
            uint imageY = originY + y;

            for (uint x = 0; x < header.BlockWidth; x++)
            {
                uint imageX = originX + x;
                bool inside = imageX < header.ImageWidth && imageY < header.ImageHeight;

                // The image mask is settled first, and what it claims never reaches the tile.
                // The tile's own mask and entries therefore advance only over what is left,
                // which is exactly how the encoder wrote them.
                if (inside && imageMask is not null && imagePalette is not null
                    && imageMask.ClaimedIndex(((long)imageY * header.ImageWidth) + imageX) is uint claimed)
                {
                    Rgba fromImage = Rgba.FromPacked(imagePalette.Colors[claimed]);
                    Mark(origins, ((long)imageY * header.ImageWidth) + imageX, PixelOrigin.ImageMask);
                    long at = ((long)imageY * header.ImageWidth + imageX) * BytesPerPixel;
                    pixels[at + 0] = fromImage.R;
                    pixels[at + 1] = fromImage.G;
                    pixels[at + 2] = fromImage.B;
                    pixels[at + 3] = fromImage.A;
                    continue;
                }

                // A shape painted into the tile settles what it covers, the image mask apart.
                // The mask and the entries never saw those positions, so they are not advanced
                // for them either - which is what makes a brush cost nothing per pixel.
                if (illustration.HasBrushes && illustration.BrushAt(x, y) is Brush brush)
                {
                    if (inside)
                    {
                        long painted = ((long)imageY * header.ImageWidth) + imageX;
                        Rgba shape = Rgba.FromPacked(brush.ColorAt(x, y));
                        Mark(origins, painted, PixelOrigin.Brush);

                        long at = painted * BytesPerPixel;
                        pixels[at + 0] = shape.R;
                        pixels[at + 1] = shape.G;
                        pixels[at + 2] = shape.B;
                        pixels[at + 3] = shape.A;
                    }

                    continue;
                }

                // The mask covers the rest of the tile even where it hangs off the edge of the
                // image, so it is advanced for those pixels too, not just the visible ones.
                PixelSource source = mask.Next(out uint paletteIndex);

                if (inherits && source == PixelSource.OutOfMask)
                {
                    if (inside)
                    {
                        Mark(origins, ((long)imageY * header.ImageWidth) + imageX, PixelOrigin.Inherited);
                    }

                    continue;
                }

                long offset = ((long)imageY * header.ImageWidth + imageX) * BytesPerPixel;

                Rgba color;
                if (deltas && source == PixelSource.Entries)
                {
                    // The base is whatever was copied in from the target tile, which is already
                    // sitting in the buffer at this very pixel.
                    (uint dr, uint dg, uint db, uint da) = colors.NextRaw();
                    color = inside
                        ? new Rgba(
                            Shift(pixels[offset + 0], dr),
                            Shift(pixels[offset + 1], dg),
                            Shift(pixels[offset + 2], db),
                            Shift(pixels[offset + 3], da))
                        : default;
                }
                else
                {
                    color = source switch
                    {
                        PixelSource.Palette => Rgba.FromPacked(palette!.Colors[paletteIndex]),
                        PixelSource.Entries => colors.Next(),
                        _ => outOfMask,
                    };
                }

                if (!inside)
                {
                    continue;
                }

                Mark(origins, ((long)imageY * header.ImageWidth) + imageX, source switch
                {
                    PixelSource.Palette => PixelOrigin.Palette,
                    PixelSource.Entries => PixelOrigin.Entries,
                    _ => PixelOrigin.OutOfMask,
                });

                pixels[offset + 0] = color.R;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.B;
                pixels[offset + 3] = color.A;
            }
        }
    }

    /// <summary>Records where one pixel came from, when anyone asked.</summary>
    private static void Mark(PixelOrigin[]? origins, long pixel, PixelOrigin origin)
    {
        if (origins is not null && pixel >= 0 && pixel < origins.Length)
        {
            origins[pixel] = origin;
        }
    }

    /// <summary>Moves one channel by a stored difference, keeping it inside 0 to 255.</summary>
    private static byte Shift(byte from, uint stored)
    {
        int moved = from + MaskedBlock.FromZigzag(stored);
        return moved < 0 ? (byte)0 : moved > 255 ? (byte)255 : (byte)moved;
    }

    /// <summary>The colour every channel takes at its minimum, used outside the mask.</summary>
    /// <remarks>
    /// A profile counting in the picture's numbering holds its minimum there too, so it goes
    /// back through the skips before it is a colour.
    /// </remarks>
    private static Rgba MinimumColor(ColorProfileBlock profile, ColorSkipsBlock? skips, int region)
    {
        return new Rgba(Channel(0, profile.StaticColorR), Channel(1, profile.StaticColorG),
            Channel(2, profile.StaticColorB), Channel(3, profile.StaticColorA));

        byte Channel(int channel, uint minimum) => Clamp(
            profile.UsesGlobalSkips && skips is not null ? skips.Expand(region, channel, minimum) : minimum);
    }

    private static byte Clamp(uint value) => value > 255 ? (byte)255 : (byte)value;

    /// <summary>An 8-bit RGBA colour.</summary>
    private readonly record struct Rgba(byte R, byte G, byte B, byte A)
    {
        /// <summary>Unpacks a colour with red in the lowest byte.</summary>
        public static Rgba FromPacked(ulong packed) => new(
            (byte)(packed & 0xFF),
            (byte)((packed >> 8) & 0xFF),
            (byte)((packed >> 16) & 0xFF),
            (byte)((packed >> 24) & 0xFF));
    }

    /// <summary>Where one pixel takes its colour from.</summary>
    private enum PixelSource
    {
        /// <summary>Outside the mask: the out-of-mask colour, or the profile minimum.</summary>
        OutOfMask,

        /// <summary>The next colour entries.</summary>
        Entries,

        /// <summary>A colour named directly by the tile's palette.</summary>
        Palette,
    }

    /// <summary>Walks a block's mask one pixel at a time, whatever mode it uses.</summary>
    private struct MaskCursor(IllustrationBlock illustration, MaskPalleteBlock? palette)
    {
        private readonly IllustrationBlock _block = illustration;
        private readonly MaskPalleteBlock? _palette = palette;
        private int _pixel = 0;
        private int _runIndex = 0;
        private uint _runRemaining = 0;
        private bool _runCovered = true;
        private bool _runStarted = false;

        /// <summary>Where the next pixel takes its colour from.</summary>
        public PixelSource Next(out uint paletteIndex)
        {
            paletteIndex = 0;

            switch (_block.MaskMode)
            {
                case MaskMode.Disabled:
                    _pixel++;
                    return PixelSource.Entries;

                case MaskMode.PerPixel:
                    int index = _pixel++;
                    int byteIndex = index >> 3;

                    // Past the end of a short mask, treat pixels as uncovered rather than throwing.
                    return byteIndex < _block.Mask.Length
                        && (_block.Mask[byteIndex] & (1 << (index & 7))) != 0
                        ? PixelSource.Entries
                        : PixelSource.OutOfMask;

                case MaskMode.Palette:
                    return NextPalette(out paletteIndex);

                case MaskMode.Linear:
                    while (_runRemaining == 0)
                    {
                        if (_runIndex >= _block.Mask.Length)
                        {
                            // Runs exhausted: the rest of the tile is uncovered.
                            return PixelSource.OutOfMask;
                        }

                        _runCovered = !_runStarted || !_runCovered;
                        _runStarted = true;
                        _runRemaining = _block.Mask[_runIndex++];
                    }

                    _runRemaining--;
                    return _runCovered ? PixelSource.Entries : PixelSource.OutOfMask;

                default:
                    return PixelSource.OutOfMask;
            }
        }

        /// <summary>Reads the next palette index out of the packed mask.</summary>
        private PixelSource NextPalette(out uint paletteIndex)
        {
            paletteIndex = 0;

            if (_palette is null)
            {
                return PixelSource.OutOfMask;
            }

            int bits = _palette.IndexBits;
            long start = (long)_pixel * bits;
            _pixel++;

            // A short mask leaves the rest of the tile outside it, rather than throwing.
            if ((start + bits) > (long)_block.Mask.Length * 8)
            {
                return PixelSource.OutOfMask;
            }

            uint value = 0;
            for (int i = 0; i < bits; i++)
            {
                long bit = start + i;
                value <<= 1;
                if ((_block.Mask[bit >> 3] & (0x80 >> (int)(bit & 7))) != 0)
                {
                    value |= 1;
                }
            }

            if (value >= _palette.EscapeIndex)
            {
                return PixelSource.Entries;
            }

            paletteIndex = value;
            return PixelSource.Palette;
        }
    }

    /// <summary>Unpacks successive colour entries against a profile.</summary>
    private struct ColorCursor(
        IllustrationBlock illustration,
        ColorProfileBlock profile,
        ColorSkipsBlock? skips,
        int region)
    {
        private readonly IllustrationBlock _block = illustration;
        private readonly ColorProfileBlock _profile = profile;
        private readonly ColorSkipsBlock? _skips = skips;
        private readonly int _region = region;
        private int _index = 0;

        /// <summary>
        /// The next entry's four channel values as stored, without being read as a colour.
        /// </summary>
        /// <remarks>
        /// A delta block's entries are differences, not colours: they must not be clamped to a
        /// byte on the way out, because it is the pixel they are applied to that has to land in
        /// range, not the difference itself.
        /// </remarks>
        public (uint R, uint G, uint B, uint A) NextRaw()
        {
            Span<uint> values = stackalloc uint[4];
            Read(values, raw: true);
            return (values[0], values[1], values[2], values[3]);
        }

        /// <summary>The next pixel colour, expanded to 8-bit RGBA.</summary>
        public Rgba Next()
        {
            Span<uint> values = stackalloc uint[4];
            Read(values, raw: false);
            return new Rgba((byte)values[0], (byte)values[1], (byte)values[2], (byte)values[3]);
        }

        /// <summary>
        /// One entry's four channels, with the channels that copy another filled in from it.
        /// </summary>
        /// <remarks>
        /// Copies are resolved after the rest, and a copied channel never consumes an entry, so
        /// the stream is read in the same R,G,B,A order whether or not any channel copies. A
        /// copy's source never copies anything itself, so one pass over them settles it.
        /// </remarks>
        private void Read(Span<uint> values, bool raw)
        {
            Span<uint> minimums =
            [
                _profile.StaticColorR,
                _profile.StaticColorG,
                _profile.StaticColorB,
                _profile.StaticColorA,
            ];

            for (int c = 0; c < 4; c++)
            {
                uint flag = ColorProfileBlock.Channels[c];
                if (_profile.IsMirrored(flag))
                {
                    continue;
                }

                values[c] = raw ? Raw(flag, minimums[c]) : Channel(flag, minimums[c], c);
            }

            for (int c = 0; c < 4; c++)
            {
                uint flag = ColorProfileBlock.Channels[c];
                if (_profile.IsMirrored(flag))
                {
                    values[c] = values[ColorProfileBlock.IndexOf(_profile.MirrorSource(flag))];
                }
            }
        }

        /// <summary>
        /// One channel: a static channel is its stored value and consumes no entry; a varying
        /// one takes the next entry as an offset above its minimum.
        /// </summary>
        private byte Channel(uint flag, uint minimum, int channel)
        {
            if (!_profile.IsNonStatic(flag))
            {
                return Clamp(Widen(minimum, channel));
            }

            if (_index >= _block.Colors.Length)
            {
                throw new InvalidOperationException(
                    $"The illustration block ran out of colour entries at {_index}; the mask covers more channels than the block stores.");
            }

            return Clamp(Widen(minimum + _profile.ExpandOffset(flag, _block.Colors[_index++]), channel));
        }

        /// <summary>
        /// Turns a number in the picture's own numbering into the channel value it stands for.
        /// </summary>
        private readonly uint Widen(uint value, int channel) =>
            _profile.UsesGlobalSkips && _skips is not null ? _skips.Expand(_region, channel, value) : value;

        /// <summary>One channel as stored, before anything reads it as a colour.</summary>
        private uint Raw(uint flag, uint minimum)
        {
            if (!_profile.IsNonStatic(flag))
            {
                return minimum;
            }

            if (_index >= _block.Colors.Length)
            {
                throw new InvalidOperationException(
                    $"The illustration block ran out of colour entries at {_index}; the mask covers more channels than the block stores.");
            }

            return minimum + _profile.ExpandOffset(flag, _block.Colors[_index++]);
        }
    }
}

using NEngineFormat.Core;
using NEngineFormat.StaticIllustration.Blocks;

namespace NEngineFormat.StaticIllustration;

/// <summary>
/// Turns the tile-bearing blocks of a file into the flat run of tiles a decoder lays out,
/// resolving links and repeats to the tiles they stand for.
/// </summary>
/// <remarks>
/// A <see cref="CombinedBlock"/> is expanded into the tiles it holds before any of this, so a
/// tile written alongside its neighbours occupies the same slot it would have alone.
/// <para>Layers simply go on counting: a picture of six tiles has slots 0 to 5 in its first
/// layer, 6 to 11 in its second. A <see cref="LayerBlock"/> moves the fill to the start of the
/// layer it names, so a picture of one layer - which is any file that never mentions them -
/// lays out exactly as it always did.</para>
/// <para>Slots are filled in three steps. A tile every one of whose pixels the image mask claims is
/// not stored at all, so it is skipped first. An <see cref="IllustrationRepeatBlock"/> then
/// claims the tiles it names outright, and everything left over is filled by the illustration
/// and link blocks in file order. A block can therefore be pulled out of the middle of the run
/// without disturbing the positions of the ones around it.</para>
/// </remarks>
public static class IllustrationTiles
{
    /// <summary>One layer of a picture, and the image-wide mask that paints under it.</summary>
    /// <param name="Index">Which layer this is, counting from zero.</param>
    /// <param name="Block">
    /// What the file says about the layer, or <see langword="null"/> for the first layer of a
    /// picture that never mentions layers at all.
    /// </param>
    /// <param name="Mask">The image-wide mask painting under this layer, if it has one.</param>
    /// <param name="MaskPalette">The colours that mask names.</param>
    public sealed record LayerSlice(
        uint Index,
        LayerBlock? Block,
        ImageMaskBlock? Mask,
        MaskPalleteBlock? MaskPalette);

    /// <summary>The layers of a picture, in order, with what paints under each.</summary>
    /// <remarks>
    /// A file with no layer block is one layer, which is how every file written before layers
    /// existed goes on reading correctly. An image mask belongs to the layer it appears in, so
    /// each layer may have a background of its own.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Layers are out of order, repeated, or name a palette that is not in the file.
    /// </exception>
    public static IReadOnlyList<LayerSlice> Layers(IReadOnlyList<BlockBase> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var palettes = new Dictionary<uint, MaskPalleteBlock>();
        foreach (BlockBase block in blocks)
        {
            if (block is MaskPalleteBlock palette)
            {
                palettes[palette.PaletteId] = palette;
            }
        }

        // The first layer is always there, named or not: a file written before layers existed
        // is a file of one layer that never mentions it.
        var found = new List<(uint Index, LayerBlock? Block, ImageMaskBlock? Mask)> { (0, null, null) };

        foreach (BlockBase block in blocks)
        {
            if (block is LayerBlock layer)
            {
                if (layer.LayerIndex == 0 && found.Count == 1 && found[0].Block is null)
                {
                    // The file names its first layer rather than leaving it implied.
                    found[0] = (0, layer, found[0].Mask);
                    continue;
                }

                // Layers are written in order and none is skipped: their tiles are addressed by
                // number, so a gap would name slots no layer owns and a jump back would ask for
                // tiles already laid down to be laid down again.
                if (layer.LayerIndex != found.Count)
                {
                    throw new InvalidOperationException(
                        $"Layer {layer.LayerIndex} follows layer {found[^1].Index}; layers are written in order, "
                        + "one after another, from zero.");
                }

                found.Add((layer.LayerIndex, layer, null));
            }
            else if (block is ImageMaskBlock mask)
            {
                (uint index, LayerBlock? owner, ImageMaskBlock? already) = found[^1];
                if (already is not null)
                {
                    throw new InvalidOperationException($"Layer {index} has two image masks.");
                }

                found[^1] = (index, owner, mask);
            }
        }

        var layers = new LayerSlice[found.Count];
        for (int i = 0; i < layers.Length; i++)
        {
            (uint index, LayerBlock? owner, ImageMaskBlock? mask) = found[i];
            MaskPalleteBlock? palette = null;

            if (mask is not null && !palettes.TryGetValue(mask.MaskPaletteId, out palette))
            {
                throw new InvalidOperationException(
                    $"The image mask of layer {index} names palette {mask.MaskPaletteId}, which is not in the file.");
            }

            layers[i] = new LayerSlice(index, owner, mask, palette);
        }

        return layers;
    }

    /// <summary>How many layers a picture has, which is one unless it says otherwise.</summary>
    public static int LayerCount(IReadOnlyList<BlockBase> blocks) => Layers(blocks).Count;

    /// <summary>
    /// The blocks that take a tile slot by position, in file order. Repeat blocks are absent:
    /// they claim slots by number rather than by where they sit.
    /// </summary>
    public static IReadOnlyList<BlockBase> Sequence(IReadOnlyList<BlockBase> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var sequence = new List<BlockBase>();
        foreach (BlockBase block in blocks)
        {
            // A combined block is several tiles written together, and each still fills a slot
            // of its own. Expanding it here means everything downstream - slots, links,
            // repeats - goes on counting tiles rather than blocks.
            if (block is CombinedBlock combined)
            {
                foreach (IllustrationBlock tile in combined.Tiles)
                {
                    sequence.Add(tile);
                }
            }
            else if (block is IllustrationBlock or IllustrationLinkBlock)
            {
                sequence.Add(block);
            }
        }

        return sequence;
    }

    /// <summary>
    /// The same run as <see cref="Sequence"/>, but naming the block each entry is really stored
    /// in: a combined block appears once for every tile it holds.
    /// </summary>
    /// <remarks>
    /// <see cref="Sequence"/> answers "what fills this slot", which is what a decoder needs.
    /// This answers "where does it live in the file", which is what a tool showing how a
    /// picture was written needs.
    /// </remarks>
    public static IReadOnlyList<BlockBase> StoredSequence(IReadOnlyList<BlockBase> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var stored = new List<BlockBase>();
        foreach (BlockBase block in blocks)
        {
            if (block is CombinedBlock combined)
            {
                for (int i = 0; i < combined.TileCount; i++)
                {
                    stored.Add(combined);
                }
            }
            else if (block is IllustrationBlock or IllustrationLinkBlock)
            {
                stored.Add(block);
            }
        }

        return stored;
    }

    /// <summary>
    /// Which tiles the image mask covers so completely that nothing is stored for them.
    /// </summary>
    /// <remarks>
    /// A tile qualifies when the mask claims every pixel of it that lies inside the image. The
    /// pixels beyond the edge draw nothing either way, so they do not keep a tile alive.
    /// </remarks>
    public static bool[] CoveredTiles(Header header, IReadOnlyList<BlockBase> blocks)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(blocks);

        IReadOnlyList<LayerSlice> layers = Layers(blocks);
        int perLayer = (int)header.TileCount;
        var covered = new bool[perLayer * layers.Count];

        foreach (LayerSlice layer in layers)
        {
            if (layer.Mask is null || layer.MaskPalette is null)
            {
                continue;
            }

            var lookup = new ImageMaskLookup(layer.Mask, layer.MaskPalette, header.ImageWidth, header.ImageHeight);
            int start = (int)layer.Index * perLayer;

            for (int tile = 0; tile < perLayer; tile++)
            {
                uint originX = (uint)(tile % header.TilesX) * header.BlockWidth;
                uint originY = (uint)(tile / header.TilesX) * header.BlockHeight;
                bool all = true;

                for (uint y = 0; y < header.BlockHeight && all; y++)
                {
                    for (uint x = 0; x < header.BlockWidth && all; x++)
                    {
                        uint imageX = originX + x;
                        uint imageY = originY + y;

                        if (imageX >= header.ImageWidth || imageY >= header.ImageHeight)
                        {
                            continue;
                        }

                        if (lookup.ClaimedIndex(((long)imageY * header.ImageWidth) + imageX) is null)
                        {
                            all = false;
                        }
                    }
                }

                covered[start + tile] = all;
            }
        }

        return covered;
    }

    /// <summary>
    /// The effective tile at every slot: illustrations as they are, links merged onto what they
    /// point at, repeats taking their target's content whole.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A block points outside the file, two blocks claim one tile, or a chain loops.
    /// </exception>
    public static IReadOnlyList<IllustrationBlock?> Resolve(Header header, IReadOnlyList<BlockBase> blocks)
    {
        bool[] covered = CoveredTiles(header, blocks);
        BlockBase?[] slots = BuildSlots(header, blocks, covered);

        var tiles = new IllustrationBlock?[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            // A tile the image mask covers outright has no block, and needs none: every pixel
            // of it is painted from the mask.
            tiles[i] = covered[i] ? null : ResolveSlot(slots, i);
        }

        return tiles;
    }

    /// <summary>
    /// The block owning each tile slot, in slot order. Positional blocks appear at the slot
    /// they ended up at rather than where they sit in the file, which is what a caller needs to
    /// line a block up with its tile.
    /// </summary>
    public static IReadOnlyList<BlockBase?> Slots(Header header, IReadOnlyList<BlockBase> blocks) =>
        BuildSlots(header, blocks, CoveredTiles(header, blocks));

    /// <summary>
    /// The block each tile slot is stored in, which for a tile written alongside its neighbours
    /// is the <see cref="CombinedBlock"/> holding it rather than the tile itself.
    /// </summary>
    public static IReadOnlyList<BlockBase?> StoredBlocks(Header header, IReadOnlyList<BlockBase> blocks) =>
        BuildSlots(header, blocks, CoveredTiles(header, blocks), stored: true);

    /// <summary>Works out which block owns each tile slot.</summary>
    private static BlockBase?[] BuildSlots(
        Header header,
        IReadOnlyList<BlockBase> blocks,
        bool[] covered,
        bool stored = false)
    {
        var slots = new BlockBase?[covered.Length];

        foreach (BlockBase block in blocks)
        {
            if (block is not IllustrationRepeatBlock repeat)
            {
                continue;
            }

            foreach (uint tile in repeat.Tiles)
            {
                if (tile >= slots.Length)
                {
                    throw new InvalidOperationException(
                        $"A repeat block claims tile {tile}, but the file only describes {slots.Length}.");
                }

                if (slots[tile] is not null)
                {
                    throw new InvalidOperationException($"Two blocks both claim tile {tile}.");
                }

                slots[tile] = repeat;
            }
        }

        // What is left over, minus the tiles the mask already covers, is filled by the
        // positional blocks in file order - restarting at the first slot of each layer, so a
        // layer that stored fewer tiles than another cannot push the next one along.
        int next = 0;
        int perLayer = Math.Max(1, (int)header.TileCount);

        foreach (BlockBase block in blocks)
        {
            if (block is LayerBlock layer)
            {
                next = (int)layer.LayerIndex * perLayer;
                continue;
            }

            // A combined block holds several tiles, and each takes a slot of its own; which
            // block is named for them is the only difference between the two runs.
            foreach (BlockBase entry in Entries(block, stored))
            {
                while (next < slots.Length && (slots[next] is not null || covered[next]))
                {
                    next++;
                }

                if (next >= slots.Length)
                {
                    break;
                }

                slots[next++] = entry;
            }
        }

        return slots;
    }

    /// <summary>The tile entries one block contributes, naming either the tile or its block.</summary>
    private static IEnumerable<BlockBase> Entries(BlockBase block, bool stored)
    {
        if (block is CombinedBlock combined)
        {
            if (stored)
            {
                for (int i = 0; i < combined.TileCount; i++)
                {
                    yield return combined;
                }
            }
            else
            {
                foreach (IllustrationBlock tile in combined.Tiles)
                {
                    yield return tile;
                }
            }
        }
        else if (block is IllustrationBlock or IllustrationLinkBlock)
        {
            yield return block;
        }
    }

    /// <summary>Follows one slot down to a real illustration.</summary>
    private static IllustrationBlock ResolveSlot(BlockBase?[] slots, int index)
    {
        var chain = new List<IllustrationLinkBlock>();
        var seen = new HashSet<int>();
        int at = index;

        while (true)
        {
            // Without this a file could describe a loop and hang the reader.
            if (!seen.Add(at))
            {
                throw new InvalidOperationException(
                    $"Tile {index} follows a loop back to tile {at}.");
            }

            switch (slots[at])
            {
                case IllustrationBlock:
                    goto resolved;

                case IllustrationLinkBlock link:
                    Step(link.TargetIndex, at);
                    chain.Add(link);
                    at = (int)link.TargetIndex;
                    break;

                case IllustrationRepeatBlock repeat:
                    Step(repeat.TargetTile, at);
                    at = (int)repeat.TargetTile;
                    break;

                default:
                    throw new InvalidOperationException($"Tile {at} has no block describing it.");
            }
        }

    resolved:
        var block = (IllustrationBlock)slots[at]!;

        // Nearest the target first, so the link the caller asked about has the last word.
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            block = chain[i].Apply(block);
        }

        return block;

        void Step(uint target, int from)
        {
            if (target >= slots.Length)
            {
                throw new InvalidOperationException(
                    $"Tile {from} points at tile {target}, but the file only describes {slots.Length}.");
            }
        }
    }
}

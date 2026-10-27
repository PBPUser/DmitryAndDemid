using NEngineFormat.Core;
using NEngineFormat.Core.Utils;
using NEngineFormat.StaticIllustration.Blocks;
using NEngineFormat.StaticIllustration.Data;

namespace NEngineFormat.StaticIllustration;

/// <summary>
/// A whole static illustration file: a <see cref="Header"/> and the blocks that follow it.
/// </summary>
/// <remarks>
/// <para>File layout:</para>
/// <code>
/// Header                        magic, flags, block count, image size, tile size
/// [PlainLength]  VarULong       when the header's compressed flag is set
/// [PackedLength] VarULong
/// blocks, either plain or as one LZMA2 stream:
/// per block, BlockCount times:
///     Type   VarULong           the block id
///     Size   VarULong           payload length in bytes
///     Data   Size bytes         the payload
/// </code>
/// <para>
/// The blocks are compressed together rather than one at a time, so the compressor sees the
/// repetition between them, and only when the result is actually smaller. The header itself
/// stays plain: a reader has to know the block count and the image size before it can decide
/// what to do with the rest.
/// </para>
/// <para>
/// Each block is framed by its own type and length, so a reader can step over a block it does
/// not recognise. <see cref="BlockRegistry"/> turns a type id into the class that implements
/// it; an unknown id comes back as a plain <see cref="BlockBase"/> holding the raw bytes, and
/// is written back out unchanged.
/// </para>
/// </remarks>
public sealed class StaticIllustrationAsset
{
    /// <summary>Largest single block payload accepted, in bytes.</summary>
    public const int MaxBlockSize = 256 * 1024 * 1024;

    /// <summary>
    /// Makes this assembly's block types resolvable before any file is read. Without it a
    /// colour profile would come back as an anonymous <see cref="BlockBase"/>.
    /// </summary>
    static StaticIllustrationAsset() =>
        BlockRegistry.RegisterAssembly(typeof(StaticIllustrationAsset).Assembly);

    /// <summary>The file header.</summary>
    public Header Header { get; set; } = new();

    /// <summary>The blocks, in file order.</summary>
    public List<BlockBase> Blocks { get; } = [];

    /// <summary>
    /// Whether writing may compress the block region with LZMA2.
    /// </summary>
    /// <remarks>
    /// Turning it off writes the blocks plainly, which is what you want when the file is going
    /// straight into an archive that will compress it again, or when you are reading the bytes
    /// to see what the encoder actually produced.
    /// </remarks>
    /// <remarks>
    /// A file that is read back carries its own answer forward, so loading and saving leaves
    /// the bytes as they were rather than quietly recompressing them.
    /// </remarks>
    public bool CompressBlocks { get; set; } = true;

    /// <summary>
    /// What the block region came to before compression, in bytes, as of the last write.
    /// </summary>
    /// <remarks>
    /// The header is not counted: this is the part LZMA2 is given. Zero until the asset has
    /// been written once.
    /// </remarks>
    public long PlainBlockLength { get; private set; }

    /// <summary>
    /// Every colour profile in the file, links resolved to the profiles they stand for.
    /// </summary>
    /// <remarks>
    /// A profile written as a link is still a profile, so it is listed here like any other.
    /// What comes back for one is built on the spot from what it borrows; use
    /// <see cref="StoredProfiles"/> to reach the blocks that are really there.
    /// </remarks>
    public IEnumerable<ColorProfileBlock> Profiles => ResolveProfiles().Values;

    /// <summary>The profile blocks the file actually holds, which is what has to be written.</summary>
    private IEnumerable<ColorProfileBlock> StoredProfiles => Blocks.OfType<ColorProfileBlock>();

    /// <summary>Every profile written as a link to another.</summary>
    public IEnumerable<ColorProfileLinkBlock> ProfileLinks => Blocks.OfType<ColorProfileLinkBlock>();

    /// <summary>
    /// Every profile by id, with each link applied to what it points at.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A link points at a profile that is not in the file, or a chain of them loops.
    /// </exception>
    public IReadOnlyDictionary<uint, ColorProfileBlock> ResolveProfiles() =>
        ProfileResolver.Resolve(Blocks);

    /// <summary>
    /// Every illustration tile in the file, in file order, including the tiles held inside a
    /// <see cref="CombinedBlock"/>.
    /// </summary>
    /// <remarks>
    /// A tile written alongside its neighbours is still a tile, so it is listed here like any
    /// other. What comes back for one of those is a view built on the spot: reading it is the
    /// same as reading the block it would have been, but changing it changes nothing, because
    /// the tile itself lives inside the combined block. Use <see cref="StoredIllustrations"/>
    /// to reach the blocks that are really there.
    /// </remarks>
    public IEnumerable<IllustrationBlock> Illustrations
    {
        get
        {
            foreach (BlockBase block in Blocks)
            {
                if (block is CombinedBlock combined)
                {
                    foreach (IllustrationBlock tile in combined.Tiles)
                    {
                        yield return tile;
                    }
                }
                else if (block is IllustrationBlock illustration)
                {
                    yield return illustration;
                }
            }
        }
    }

    /// <summary>
    /// The illustration blocks the file actually holds, which is what has to be written back.
    /// </summary>
    private IEnumerable<IllustrationBlock> StoredIllustrations => Blocks.OfType<IllustrationBlock>();

    /// <summary>Every mask palette in the file.</summary>
    public IEnumerable<MaskPalleteBlock> Palettes => Blocks.OfType<MaskPalleteBlock>();

    /// <summary>Finds a mask palette by its <see cref="MaskPalleteBlock.PaletteId"/>.</summary>
    public MaskPalleteBlock? FindPalette(uint paletteId) =>
        Palettes.FirstOrDefault(p => p.PaletteId == paletteId);

    /// <summary>Every link standing in for another tile.</summary>
    public IEnumerable<IllustrationLinkBlock> Links => Blocks.OfType<IllustrationLinkBlock>();

    /// <summary>Every block holding several tiles at once.</summary>
    public IEnumerable<CombinedBlock> Combined => Blocks.OfType<CombinedBlock>();

    /// <summary>Every block repeating one tile across many others.</summary>
    public IEnumerable<IllustrationRepeatBlock> Repeats => Blocks.OfType<IllustrationRepeatBlock>();

    /// <summary>The values no pixel of the picture uses, when the file describes them.</summary>
    public ColorSkipsBlock? ColorSkips => Blocks.OfType<ColorSkipsBlock>().FirstOrDefault();

    /// <summary>The image-wide palette mask, when the file has one.</summary>
    public ImageMaskBlock? ImageMask => Blocks.OfType<ImageMaskBlock>().FirstOrDefault();

    /// <summary>Every image-wide mask in the file, one per layer that has one.</summary>
    public IEnumerable<ImageMaskBlock> ImageMasks => Blocks.OfType<ImageMaskBlock>();

    /// <summary>What the file says about each of its layers, in order.</summary>
    /// <remarks>
    /// A picture that never mentions layers is one layer, so this is never empty; the entry for
    /// a layer the file did not describe carries no block of its own.
    /// </remarks>
    public IReadOnlyList<IllustrationTiles.LayerSlice> Layers => IllustrationTiles.Layers(Blocks);

    /// <summary>How many layers the picture has, which is one unless it says otherwise.</summary>
    public int LayerCount => Layers.Count;

    /// <summary>The blocks that start a layer, in order.</summary>
    public IEnumerable<LayerBlock> LayerBlocks => Blocks.OfType<LayerBlock>();

    /// <summary>
    /// The tile at every slot with links already resolved, which is what a decoder draws.
    /// </summary>
    public IReadOnlyList<IllustrationBlock?> ResolveTiles() => IllustrationTiles.Resolve(Header, Blocks);

    /// <summary>Finds a colour profile by its <see cref="ColorProfileBlock.ProfileId"/>.</summary>
    public ColorProfileBlock? FindProfile(uint profileId) =>
        ResolveProfiles().TryGetValue(profileId, out ColorProfileBlock? profile) ? profile : null;

    /// <summary>Reads an asset from a package.</summary>
    /// <exception cref="InvalidDataException">The data does not start with the AKOB signature.</exception>
    /// <exception cref="InvalidOperationException">The file is structurally inconsistent.</exception>
    public static StaticIllustrationAsset Load(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var asset = new StaticIllustrationAsset();
        asset.Header.Read(package);

        // How the file was stored is how it is written back, unless the caller says otherwise.
        asset.CompressBlocks = asset.Header.IsCompressed;

        BitPackage blocks = asset.Header.IsCompressed ? ReadCompressed(package) : package;

        for (uint i = 0; i < asset.Header.BlockCount; i++)
        {
            asset.Blocks.Add(ReadBlock(blocks, i));
        }

        // The mask has to come out first: what it covers decides which tiles have blocks at
        // all, so validating or resolving before that would read the slots wrongly.
        asset.UnpackImageMask();
        asset.Validate();
        asset.UnpackColors();
        return asset;
    }

    /// <summary>Reads an asset from a byte array.</summary>
    public static StaticIllustrationAsset Load(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Load(new BitPackage(bytes));
    }

    /// <summary>Reads an asset from a stream.</summary>
    public static StaticIllustrationAsset Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Load(BitPackage.GetStreamReadPackage(stream));
    }

    /// <summary>Reads an asset from a file on disk.</summary>
    public static StaticIllustrationAsset LoadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using BitPackage package = BitPackage.OpenStreamReadPackage(path);
        return Load(package);
    }

    /// <summary>
    /// Writes the asset, stamping <see cref="Header.BlockCount"/> from the actual block list
    /// so the count can never drift from what follows it.
    /// </summary>
    public void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        Header.BlockCount = (uint)Blocks.Count;
        Validate();
        PackColors();
        PackImageMask();

        // The blocks are laid out first, because whether they compress decides a header flag.
        var body = new BitPackage();
        foreach (BlockBase block in Blocks)
        {
            // The payload is produced first because its length has to precede it, and a
            // block's own Write is the only thing that knows how long it will be.
            var payload = new BitPackage();
            block.Write(payload);
            byte[] data = payload.Export();

            if (data.Length > MaxBlockSize)
            {
                throw new InvalidOperationException(
                    $"Block type {block.Type} produced {data.Length} bytes, over the {MaxBlockSize}-byte limit.");
            }

            body.WriteVarULong(block.Type);
            body.WriteVarULong((ulong)data.Length);
            body.Write(data);
        }

        byte[] plain = body.Export();
        PlainBlockLength = plain.Length;

        byte[] packed = !CompressBlocks || plain.Length == 0 ? [] : Lzma2.Compress(plain);

        // Compression is kept only when it wins. It usually does, but a tiny file is mostly
        // container overhead and an already dense one can come out larger.
        bool compress = packed.Length > 0
            && packed.Length + VarSize((ulong)plain.Length) + VarSize((ulong)packed.Length) < plain.Length;

        Header.IsCompressed = compress;
        Header.Write(package);

        if (!compress)
        {
            package.Write(plain);
            return;
        }

        package.WriteVarULong((ulong)plain.Length);
        package.WriteVarULong((ulong)packed.Length);
        package.Write(packed);
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

    /// <summary>Reads and expands the compressed block region.</summary>
    private static BitPackage ReadCompressed(BitPackage package)
    {
        ulong plainLength = package.ReadVarULong();
        ulong packedLength = package.ReadVarULong();

        if (plainLength > int.MaxValue || packedLength > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"Compressed block region claims {packedLength} bytes expanding to {plainLength}.");
        }

        byte[] packed = new byte[packedLength];
        for (int i = 0; i < packed.Length; i++)
        {
            packed[i] = package.ReadByte();
        }

        return new BitPackage(Lzma2.Decompress(packed, (int)plainLength));
    }

    /// <summary>Serialises the asset to a byte array.</summary>
    public byte[] ToBytes()
    {
        var package = new BitPackage();
        Write(package);
        return package.Export();
    }

    /// <summary>Writes the asset to a file, replacing it if it exists.</summary>
    public void SaveFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        File.WriteAllBytes(path, ToBytes());
    }

    /// <summary>Decodes the illustration to an RGBA8 pixel buffer.</summary>
    public byte[] DecodeRgba() => IllustrationDecoder.DecodeRgba(Header, Blocks);

    /// <summary>Decodes every layer of the illustration, bottom layer first.</summary>
    public byte[][] DecodeLayers() => IllustrationDecoder.DecodeLayers(Header, Blocks);

    /// <summary>Where every pixel of the decoded picture got its colour.</summary>
    public Data.PixelOrigin[] DecodeOrigins() => IllustrationDecoder.DecodeOrigins(Header, Blocks);

    /// <summary>
    /// Bit-packs every illustration's <see cref="IllustrationBlock.Colors"/> into its
    /// <see cref="IllustrationBlock.PackedColors"/>, ready to write.
    /// </summary>
    /// <remarks>
    /// This lives here rather than on the block because it needs the block's colour profile,
    /// and only the whole file knows which profile an id refers to.
    /// </remarks>
    public void PackColors()
    {
        // How a palette writes its colours depends on the colours it ended up with, so it is
        // settled here, where everything else that only writing needs is settled.
        foreach (MaskPalleteBlock palette in Palettes)
        {
            palette.ChooseStorage();
        }

        foreach (CombinedBlock combined in Combined)
        {
            combined.PackedColors = ColorBitPacker.Pack(
                ProfileFor(combined.ColorProfileId), combined.Colors);
        }

        foreach (IllustrationBlock illustration in StoredIllustrations)
        {
            ColorProfileBlock profile = ProfileFor(illustration.ColorProfileId);
            illustration.ColorCount = (uint)illustration.Colors.Length;
            illustration.PackedColors = ColorBitPacker.Pack(profile, illustration.Colors);
        }

        // A link only carries entries when it overrides them, and then against whichever
        // profile it ends up resolving to.
        foreach ((IllustrationLinkBlock link, uint profileId) in LinksOverridingColors())
        {
            link.ColorCount = (uint)link.Colors.Length;
            link.PackedColors = ColorBitPacker.Pack(ProfileFor(profileId), link.Colors);
        }
    }

    /// <summary>
    /// Bit-packs the image mask's runs at the narrowest width that makes them smallest.
    /// </summary>
    /// <remarks>
    /// Like a block's colour entries, the runs are packed against the palette's index width,
    /// which only the whole file can resolve from an id.
    /// </remarks>
    public void PackImageMask()
    {
        foreach (ImageMaskBlock mask in ImageMasks)
        {
            // Rectangles carry no runs to pack, and packing them would rewrite the block as a
            // run form holding nothing at all.
            if (mask.IsRectangles)
            {
                continue;
            }

            MaskPalleteBlock palette = FindPalette(mask.MaskPaletteId)
                ?? throw new InvalidOperationException(
                    $"An image mask names palette {mask.MaskPaletteId}, which is not in the file.");

            mask.RunLengthBits = ImageMaskBlock.OptimalRunBits(mask.RunLengths, palette.IndexBits);
            (mask.PackedRuns, mask.RunCount) = ImageMaskBlock.PackRuns(
                mask.RunLengths,
                mask.RunIndices,
                mask.RunLengthBits,
                palette.IndexBits);
        }
    }

    /// <summary>Expands the image mask's packed runs back out.</summary>
    public void UnpackImageMask()
    {
        foreach (ImageMaskBlock mask in ImageMasks)
        {
            // A mask written as rectangles arrives whole; there are no runs to expand.
            if (mask.IsRectangles)
            {
                continue;
            }

            MaskPalleteBlock palette = FindPalette(mask.MaskPaletteId)
                ?? throw new InvalidOperationException(
                    $"An image mask names palette {mask.MaskPaletteId}, which is not in the file.");

            (mask.RunLengths, mask.RunIndices) = ImageMaskBlock.UnpackRuns(
                mask.PackedRuns,
                mask.RunCount,
                mask.RunLengthBits,
                palette.IndexBits);
        }
    }

    /// <summary>Expands every illustration's packed bit stream back into entries.</summary>
    public void UnpackColors()
    {
        // Combined blocks come first: the tiles they stand for are sliced out of these entries,
        // and a link resolving against one of those tiles needs them already in place.
        foreach (CombinedBlock combined in Combined)
        {
            combined.Colors = ColorBitPacker.Unpack(
                ProfileFor(combined.ColorProfileId),
                combined.PackedColors,
                (int)combined.TotalColorCount);
        }

        foreach (IllustrationBlock illustration in StoredIllustrations)
        {
            illustration.Colors = ColorBitPacker.Unpack(
                ProfileFor(illustration.ColorProfileId),
                illustration.PackedColors,
                (int)illustration.ColorCount);
        }

        foreach ((IllustrationLinkBlock link, uint profileId) in LinksOverridingColors())
        {
            link.Colors = ColorBitPacker.Unpack(
                ProfileFor(profileId),
                link.PackedColors,
                (int)link.ColorCount);
        }
    }

    /// <summary>
    /// The links that carry their own entries, paired with the profile those entries are
    /// written against - the link's own when it overrides one, otherwise its target's.
    /// </summary>
    private IEnumerable<(IllustrationLinkBlock Link, uint ProfileId)> LinksOverridingColors()
    {
        // Slot order, not file order: a repeat block claims tiles out of sequence, so a link's
        // position among the blocks no longer says which tile it fills.
        IReadOnlyList<BlockBase?> slots = IllustrationTiles.Slots(Header, Blocks);
        IReadOnlyList<IllustrationBlock?> resolved = IllustrationTiles.Resolve(Header, Blocks);

        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] is IllustrationLinkBlock link
                && link.HasOverride(IllustrationLinkBlock.OverrideColorsFlag)
                && resolved[i] is IllustrationBlock tile)
            {
                yield return (link, tile.ColorProfileId);
            }
        }
    }

    private ColorProfileBlock ProfileFor(uint profileId) =>
        FindProfile(profileId)
        ?? throw new InvalidOperationException(
            $"A block names colour profile {profileId}, which is not in the file.");

    /// <summary>
    /// Checks the file hangs together: the header is sane, profile ids are unique, and every
    /// illustration names a profile that exists.
    /// </summary>
    /// <exception cref="InvalidOperationException">Something does not line up.</exception>
    public void Validate()
    {
        Header.Validate();

        var seen = new HashSet<uint>();
        foreach (ColorProfileBlock profile in StoredProfiles)
        {
            if (!seen.Add(profile.ProfileId))
            {
                throw new InvalidOperationException(
                    $"Two colour profiles share id {profile.ProfileId}; an illustration referring to it would be ambiguous.");
            }
        }

        foreach (ColorProfileLinkBlock link in ProfileLinks)
        {
            if (!seen.Add(link.ProfileId))
            {
                throw new InvalidOperationException(
                    $"Two colour profiles share id {link.ProfileId}; an illustration referring to it would be ambiguous.");
            }
        }

        // Resolving proves every link points somewhere real and that no chain of them loops.
        seen = [.. ResolveProfiles().Keys];

        // Resolving also proves every link points somewhere real and that no chain loops.
        IReadOnlyList<IllustrationBlock?> tiles = ResolveTiles();

        var palettes = new HashSet<uint>();
        foreach (MaskPalleteBlock palette in Palettes)
        {
            if (!palettes.Add(palette.PaletteId))
            {
                throw new InvalidOperationException(
                    $"Two mask palettes share id {palette.PaletteId}; a tile referring to it would be ambiguous.");
            }
        }

        for (int slot = 0; slot < tiles.Count; slot++)
        {
            // A tile the image mask covers outright has no block to check.
            if (tiles[slot] is not IllustrationBlock tile)
            {
                continue;
            }

            // A masked tile copies pixels that have already been drawn, so its target has to
            // come before it. A file naming a later tile would decode against a blank one.
            if (tile is MaskedBlock masked)
            {
                if (masked.TargetTile >= slot)
                {
                    throw new InvalidOperationException(
                        $"Tile {slot} starts from tile {masked.TargetTile}, which is not an earlier tile.");
                }

                // Rows swapped for columns only land inside the tile again when the two are the
                // same length, so on any other shape the file could not be decoded at all.
                if (masked.Transform.NeedsSquare() && Header.BlockWidth != Header.BlockHeight)
                {
                    throw new InvalidOperationException(
                        $"Tile {slot} reads tile {masked.TargetTile} {masked.Transform.Describe()}, which "
                        + $"needs square tiles, but they are {Header.BlockWidth}x{Header.BlockHeight}.");
                }
            }

            if (!seen.Contains(tile.ColorProfileId))
            {
                throw new InvalidOperationException(
                    $"A tile names colour profile {tile.ColorProfileId}, which is not in the file.");
            }

            if (tile.MaskMode == MaskMode.Palette && !palettes.Contains(tile.MaskPaletteId))
            {
                throw new InvalidOperationException(
                    $"A tile names mask palette {tile.MaskPaletteId}, which is not in the file.");
            }
        }

        // One image mask per layer, and each naming colours the file really holds. Resolving
        // the layers proves the first of those, since a second mask in one layer has nowhere to
        // belong.
        foreach (ImageMaskBlock imageMask in ImageMasks)
        {
            if (!palettes.Contains(imageMask.MaskPaletteId))
            {
                throw new InvalidOperationException(
                    $"An image mask names palette {imageMask.MaskPaletteId}, which is not in the file.");
            }
        }

        IllustrationTiles.Layers(Blocks);
    }

    public override string ToString()
    {
        int layers = LayerCount;
        string said = layers > 1 ? $", {layers} layer(s)" : string.Empty;
        return $"{Header} - {Profiles.Count()} profile(s), {Illustrations.Count()} tile(s){said}";
    }

    /// <summary>Reads one length-framed block and materialises it through the registry.</summary>
    private static BlockBase ReadBlock(BitPackage package, uint index)
    {
        ulong rawType = package.ReadVarULong();
        if (rawType > uint.MaxValue)
        {
            throw new InvalidOperationException($"Block {index} has type id {rawType}, which does not fit in 32 bits.");
        }

        ulong size = package.ReadVarULong();

        // Checked before allocating, so a corrupt length cannot turn a few bytes of input
        // into a huge array.
        if (size > MaxBlockSize)
        {
            throw new InvalidOperationException(
                $"Block {index} declares {size} bytes, over the {MaxBlockSize}-byte limit.");
        }

        byte[] data = size == 0 ? [] : package.Read((int)size);
        return BlockRegistry.Create((uint)rawType, data);
    }
}

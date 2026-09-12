using NEngineFormat.Core;
using NEngineFormat.StaticIllustration.Blocks;
using NEngineFormat.StaticIllustration.Data;

namespace NEngineFormat.StaticIllustration;

/// <summary>One layer handed to the encoder: its pixels and what the host calls it.</summary>
/// <param name="Rgba">The layer's own pixels, RGBA8, the size of the whole picture.</param>
/// <param name="Name">What the layer is called, empty for an unnamed one.</param>
/// <param name="Opacity">How much of it shows, 0 to 255.</param>
/// <param name="Hidden">Whether it is drawn at all.</param>
/// <param name="BlendMode">
/// How it combines with what is under it, as the host numbers its own modes. This format carries
/// the number and does not interpret it.
/// </param>
public sealed record LayerSource(
    byte[] Rgba,
    string Name = "",
    uint Opacity = LayerBlock.FullOpacity,
    bool Hidden = false,
    uint BlendMode = 0);

/// <summary>
/// Writes a picture of several layers as one file.
/// </summary>
/// <remarks>
/// <para>
/// Each layer is encoded the way a whole picture is - its own profiles, palettes, masks and
/// differences - and the results are laid end to end with a <see cref="LayerBlock"/> in front of
/// each. What has to be put right afterwards is only the numbering: tiles count on across
/// layers, so a layer's references to its own tiles, profiles and palettes are shifted to where
/// they now sit.
/// </para>
/// <para>
/// A picture of one layer with nothing to say about it is written exactly as it always was, with
/// no layer block at all, so nothing about this changes the files that existed before it.
/// </para>
/// </remarks>
public static class LayerEncoder
{
    /// <summary>Encodes several layers into one illustration.</summary>
    /// <exception cref="ArgumentException">
    /// There are no layers, or one of them is not the size of the picture.
    /// </exception>
    public static StaticIllustrationAsset Encode(
        IReadOnlyList<LayerSource> layers,
        uint width,
        uint height,
        uint tileWidth = 64,
        uint tileHeight = 64,
        EncoderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(layers);

        if (layers.Count == 0)
        {
            throw new ArgumentException("A picture needs at least one layer.", nameof(layers));
        }

        if (layers.Count > LayerBlock.MaxLayers)
        {
            throw new ArgumentException(
                $"There are {layers.Count} layers, over the {LayerBlock.MaxLayers} limit.", nameof(layers));
        }

        EncoderOptions settings = (options ?? new EncoderOptions()).Clone();

        // The table of unused values belongs to the file rather than to a layer, and each layer
        // is fitted on its own, so a layered picture does without it.
        if (layers.Count > 1)
        {
            settings.ColorSkips = false;
        }

        var asset = new StaticIllustrationAsset();
        uint tiles = 0;
        uint profiles = 0;
        uint palettes = 0;

        for (int i = 0; i < layers.Count; i++)
        {
            LayerSource layer = layers[i];
            ArgumentNullException.ThrowIfNull(layer.Rgba);

            StaticIllustrationAsset part = IllustrationEncoder.Encode(
                layer.Rgba, width, height, tileWidth, tileHeight, settings);

            if (i == 0)
            {
                asset.Header.ImageWidth = part.Header.ImageWidth;
                asset.Header.ImageHeight = part.Header.ImageHeight;
                asset.Header.BlockWidth = part.Header.BlockWidth;
                asset.Header.BlockHeight = part.Header.BlockHeight;
                tiles = (uint)part.Header.TileCount;
            }

            // A single layer with nothing to say about itself is left unsaid, so a picture that
            // has no layers to speak of is written exactly as it was before layers existed.
            if (layers.Count > 1 || Describes(layer))
            {
                asset.Blocks.Add(new LayerBlock
                {
                    LayerIndex = (uint)i,
                    Opacity = layer.Opacity,
                    Hidden = layer.Hidden,
                    BlendMode = layer.BlendMode,
                    Name = layer.Name,
                });
            }

            Relocate(part.Blocks, (uint)i * tiles, profiles, palettes);
            asset.Blocks.AddRange(part.Blocks);

            // The next layer's own numbering starts past everything this one used.
            profiles += Highest(part.Blocks, ProfileIds);
            palettes += Highest(part.Blocks, PaletteIds);
        }

        asset.Header.BlockCount = (uint)asset.Blocks.Count;
        return asset;
    }

    /// <summary>Whether a layer says anything a file would have to carry.</summary>
    private static bool Describes(LayerSource layer) =>
        layer.Name.Length > 0
        || layer.Opacity != LayerBlock.FullOpacity
        || layer.Hidden
        || layer.BlendMode != 0;

    /// <summary>
    /// Shifts one layer's blocks to where they sit in the whole file: its tiles, its profiles
    /// and its palettes all counted on from what the layers before it used.
    /// </summary>
    public static void Relocate(
        IReadOnlyList<BlockBase> blocks,
        uint tileOffset,
        uint profileOffset,
        uint paletteOffset)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        if (tileOffset == 0 && profileOffset == 0 && paletteOffset == 0)
        {
            return;
        }

        foreach (BlockBase block in blocks)
        {
            switch (block)
            {
                case ColorProfileBlock profile:
                    profile.ProfileId += profileOffset;
                    break;

                case ColorProfileLinkBlock link:
                    link.ProfileId += profileOffset;
                    link.TargetProfileId += profileOffset;
                    break;

                case MaskPalleteBlock palette:
                    palette.PaletteId += paletteOffset;
                    break;

                case ImageMaskBlock mask:
                    mask.MaskPaletteId += paletteOffset;
                    break;

                case CombinedBlock combined:
                    combined.ColorProfileId += profileOffset;
                    if ((combined.Flags & IllustrationBlock.MaskModeMask) == (uint)MaskMode.Palette)
                    {
                        combined.MaskPaletteId += paletteOffset;
                    }

                    break;

                case IllustrationRepeatBlock repeat:
                    repeat.TargetTile += tileOffset;
                    for (int i = 0; i < repeat.Tiles.Length; i++)
                    {
                        repeat.Tiles[i] += tileOffset;
                    }

                    break;

                case IllustrationLinkBlock tileLink:
                    tileLink.TargetIndex += tileOffset;

                    // A link only carries a field it replaces, so only those are renumbered.
                    if (tileLink.HasOverride(IllustrationLinkBlock.OverrideColorProfileFlag))
                    {
                        tileLink.ColorProfileId += profileOffset;
                    }

                    if (tileLink.HasOverride(IllustrationLinkBlock.OverrideMaskPaletteFlag))
                    {
                        tileLink.MaskPaletteId += paletteOffset;
                    }

                    break;

                case IllustrationBlock illustration:
                    illustration.ColorProfileId += profileOffset;

                    if (illustration.MaskMode == MaskMode.Palette)
                    {
                        illustration.MaskPaletteId += paletteOffset;
                    }

                    if (illustration is MaskedBlock masked)
                    {
                        masked.TargetTile += tileOffset;
                    }

                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>The largest id of a kind in one layer's blocks, which is what the next starts past.</summary>
    private static uint Highest(IReadOnlyList<BlockBase> blocks, Func<BlockBase, uint?> id)
    {
        uint highest = 0;
        foreach (BlockBase block in blocks)
        {
            if (id(block) is uint found && found > highest)
            {
                highest = found;
            }
        }

        return highest;
    }

    /// <summary>The profile id a block declares, if it declares one.</summary>
    private static uint? ProfileIds(BlockBase block) => block switch
    {
        ColorProfileBlock profile => profile.ProfileId,
        ColorProfileLinkBlock link => link.ProfileId,
        _ => null,
    };

    /// <summary>The palette id a block declares, if it declares one.</summary>
    private static uint? PaletteIds(BlockBase block) => block switch
    {
        MaskPalleteBlock palette => palette.PaletteId,
        _ => null,
    };
}

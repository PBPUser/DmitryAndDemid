using NEngineFormat.Core;
using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;

namespace NEngineFormat.StaticIllustration.Blocks;

/// <summary>
/// Stands in for an <see cref="IllustrationBlock"/> by pointing at another one, optionally
/// replacing some of its fields.
/// </summary>
/// <remarks>
/// <para>Payload layout:</para>
/// <code>
/// TargetIndex      VarULong  position of the tile being linked to
/// Overrides        VarULong  bitmask of which fields this block replaces
/// [Flags]          VarULong  when the flags bit is set
/// [ColorProfileId] VarULong  when the profile bit is set
/// [OutOfMaskColor] VarULong  when the out-of-mask bit is set
/// [MaskLength]     VarULong  when the mask bit is set
/// [Mask]           bytes
/// [ColorCount]     VarULong  when the colours bit is set
/// [PackedLength]   VarULong
/// [PackedColors]   bytes
/// </code>
/// <para>
/// A link occupies a tile slot exactly as an illustration does, so tiles stay positional. Two
/// identical tiles cost one full block and one link; a tile that repeats another but with a
/// different background costs a link plus the one field it replaces.
/// </para>
/// <para>
/// <see cref="TargetIndex"/> counts tiles, not blocks: it is the position among the
/// illustration and link blocks in file order, which is the same ordering the decoder lays
/// tiles out in.
/// </para>
/// </remarks>
[BlockId(Id)]
public class IllustrationLinkBlock : BlockBase
{
    /// <summary>The id this block is stored under.</summary>
    public const uint Id = 5;

    /// <summary>Replaces the target's <see cref="IllustrationBlock.Flags"/>.</summary>
    public const uint OverrideFlagsFlag = 1u << 0;

    /// <summary>Replaces the target's <see cref="IllustrationBlock.Mask"/>.</summary>
    public const uint OverrideMaskFlag = 1u << 1;

    /// <summary>Replaces the target's colour entries.</summary>
    public const uint OverrideColorsFlag = 1u << 2;

    /// <summary>Replaces the target's <see cref="IllustrationBlock.ColorProfileId"/>.</summary>
    public const uint OverrideColorProfileFlag = 1u << 3;

    /// <summary>Replaces the target's <see cref="IllustrationBlock.OutOfMaskColor"/>.</summary>
    public const uint OverrideOutOfMaskColorFlag = 1u << 4;

    /// <summary>Replaces the target's <see cref="IllustrationBlock.MaskPaletteId"/>.</summary>
    public const uint OverrideMaskPaletteFlag = 1u << 5;

    /// <summary>Every override bit, for validating that no unknown ones are set.</summary>
    public const uint AllOverrideFlags =
        OverrideFlagsFlag | OverrideMaskFlag | OverrideColorsFlag
        | OverrideColorProfileFlag | OverrideOutOfMaskColorFlag | OverrideMaskPaletteFlag;

    public IllustrationLinkBlock() => Type = Id;

    /// <summary>Which tile this link stands in for.</summary>
    public uint TargetIndex { get; set; }

    /// <summary>Bitmask of the fields this link replaces rather than inherits.</summary>
    public uint Overrides { get; set; }

    /// <summary>Replacement for <see cref="IllustrationBlock.Flags"/>.</summary>
    public uint Flags { get; set; }

    /// <summary>Replacement for <see cref="IllustrationBlock.ColorProfileId"/>.</summary>
    public uint ColorProfileId { get; set; }

    /// <summary>Replacement for <see cref="IllustrationBlock.OutOfMaskColor"/>.</summary>
    public ulong OutOfMaskColor { get; set; }

    /// <summary>Replacement for <see cref="IllustrationBlock.MaskPaletteId"/>.</summary>
    public uint MaskPaletteId { get; set; }

    /// <summary>Replacement for <see cref="IllustrationBlock.Mask"/>.</summary>
    public byte[] Mask { get; set; } = [];

    /// <summary>Replacement colour entries, unpacked.</summary>
    public uint[] Colors { get; set; } = [];

    /// <summary>Replacement colour entries as stored.</summary>
    public byte[] PackedColors { get; set; } = [];

    /// <summary>How many entries <see cref="PackedColors"/> holds.</summary>
    public uint ColorCount { get; set; }

    /// <summary>Whether this link replaces a given field.</summary>
    public bool HasOverride(uint overrideFlag) => (Overrides & overrideFlag) != 0;

    /// <summary>Turns an override on or off.</summary>
    public void SetOverride(uint overrideFlag, bool enabled) =>
        Overrides = enabled ? Overrides | overrideFlag : Overrides & ~overrideFlag;

    /// <summary>Builds the tile this link stands for, taking what it does not replace.</summary>
    public IllustrationBlock Apply(IllustrationBlock target)
    {
        ArgumentNullException.ThrowIfNull(target);

        bool colors = HasOverride(OverrideColorsFlag);

        // A link replaces fields, not the kind of block it points at: pointing at a tile that
        // draws itself as a difference from another still means "that tile, with these fields
        // changed", so the target it starts from is inherited like anything else.
        IllustrationBlock result = target is MaskedBlock masked
            ? new MaskedBlock { TargetTile = masked.TargetTile }
            : new IllustrationBlock();

        return Fill(result);

        IllustrationBlock Fill(IllustrationBlock block)
        {
            block.Flags = HasOverride(OverrideFlagsFlag) ? Flags : target.Flags;
            block.ColorProfileId = HasOverride(OverrideColorProfileFlag) ? ColorProfileId : target.ColorProfileId;
            block.OutOfMaskColor = HasOverride(OverrideOutOfMaskColorFlag) ? OutOfMaskColor : target.OutOfMaskColor;
            block.MaskPaletteId = HasOverride(OverrideMaskPaletteFlag) ? MaskPaletteId : target.MaskPaletteId;
            block.Mask = HasOverride(OverrideMaskFlag) ? Mask : target.Mask;
            block.Colors = colors ? Colors : target.Colors;
            block.PackedColors = colors ? PackedColors : target.PackedColors;
            block.ColorCount = colors ? ColorCount : target.ColorCount;

            // Shapes travel with the tile: there is no override for them, so a link paints what
            // its target painted. Overriding the flags to clear the brush bit drops them, which
            // is the same power a link has over the mask.
            block.Brushes = block.HasBrushes ? target.Brushes : [];
            return block;
        }
    }


    /// <summary>Checks the link can be written.</summary>
    /// <exception cref="InvalidOperationException">The overrides do not describe a writable block.</exception>
    public void Validate()
    {
        if ((Overrides & ~AllOverrideFlags) != 0)
        {
            throw new InvalidOperationException(
                $"Overrides 0x{Overrides:X} sets bits this format does not define.");
        }

        if (!HasOverride(OverrideMaskFlag) && Mask.Length > 0)
        {
            throw new InvalidOperationException(
                $"{Mask.Length} mask byte(s) are set but the mask override is not; they would not be written.");
        }

        if (!HasOverride(OverrideColorsFlag) && (PackedColors.Length > 0 || ColorCount > 0))
        {
            throw new InvalidOperationException(
                "Colour entries are set but the colours override is not; they would not be written.");
        }

        if (Mask.Length > IllustrationBlock.MaxMaskLength)
        {
            throw new InvalidOperationException(
                $"The mask is {Mask.Length} bytes, over the {IllustrationBlock.MaxMaskLength}-byte limit.");
        }

        if (PackedColors.Length > IllustrationBlock.MaxColorCount)
        {
            throw new InvalidOperationException(
                $"Packed colour data is {PackedColors.Length} bytes, over the {IllustrationBlock.MaxColorCount} limit.");
        }
    }

    public override void Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        TargetIndex = (uint)package.ReadVarULong();
        Overrides = (uint)package.ReadVarULong();

        if ((Overrides & ~AllOverrideFlags) != 0)
        {
            throw new InvalidOperationException(
                $"Overrides 0x{Overrides:X} sets bits this format does not define.");
        }

        Flags = HasOverride(OverrideFlagsFlag) ? (uint)package.ReadVarULong() : 0;
        ColorProfileId = HasOverride(OverrideColorProfileFlag) ? (uint)package.ReadVarULong() : 0;
        OutOfMaskColor = HasOverride(OverrideOutOfMaskColorFlag) ? package.ReadVarULong() : 0;
        MaskPaletteId = HasOverride(OverrideMaskPaletteFlag) ? (uint)package.ReadVarULong() : 0;

        if (HasOverride(OverrideMaskFlag))
        {
            ulong maskLength = package.ReadVarULong();
            if (maskLength > IllustrationBlock.MaxMaskLength)
            {
                throw new InvalidOperationException(
                    $"The mask claims {maskLength} bytes, over the {IllustrationBlock.MaxMaskLength}-byte limit.");
            }

            Mask = new byte[maskLength];
            for (int i = 0; i < Mask.Length; i++)
            {
                Mask[i] = package.ReadByte();
            }
        }
        else
        {
            Mask = [];
        }

        if (HasOverride(OverrideColorsFlag))
        {
            ulong colorCount = package.ReadVarULong();
            if (colorCount > IllustrationBlock.MaxColorCount)
            {
                throw new InvalidOperationException(
                    $"The link claims {colorCount} colours, over the {IllustrationBlock.MaxColorCount} limit.");
            }

            ulong packedLength = package.ReadVarULong();
            if (packedLength > IllustrationBlock.MaxColorCount)
            {
                throw new InvalidOperationException(
                    $"Packed colour data claims {packedLength} bytes, over the {IllustrationBlock.MaxColorCount} limit.");
            }

            ColorCount = (uint)colorCount;
            PackedColors = new byte[packedLength];
            for (int i = 0; i < PackedColors.Length; i++)
            {
                PackedColors[i] = package.ReadByte();
            }
        }
        else
        {
            ColorCount = 0;
            PackedColors = [];
        }

        // Unpacking needs channel widths from a profile this block cannot resolve on its own.
        Colors = [];
    }

    public override void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteVarULong(TargetIndex);
        package.WriteVarULong(Overrides);

        if (HasOverride(OverrideFlagsFlag))
        {
            package.WriteVarULong(Flags);
        }

        if (HasOverride(OverrideColorProfileFlag))
        {
            package.WriteVarULong(ColorProfileId);
        }

        if (HasOverride(OverrideOutOfMaskColorFlag))
        {
            package.WriteVarULong(OutOfMaskColor);
        }

        if (HasOverride(OverrideMaskPaletteFlag))
        {
            package.WriteVarULong(MaskPaletteId);
        }

        if (HasOverride(OverrideMaskFlag))
        {
            package.WriteVarULong((ulong)Mask.Length);
            foreach (byte value in Mask)
            {
                package.WriteByte(value);
            }
        }

        if (HasOverride(OverrideColorsFlag))
        {
            package.WriteVarULong(ColorCount);
            package.WriteVarULong((ulong)PackedColors.Length);
            foreach (byte value in PackedColors)
            {
                package.WriteByte(value);
            }
        }
    }

    public override string ToString()
    {
        var replaced = new List<string>();
        if (HasOverride(OverrideFlagsFlag))
        {
            replaced.Add("flags");
        }

        if (HasOverride(OverrideMaskFlag))
        {
            replaced.Add("mask");
        }

        if (HasOverride(OverrideColorsFlag))
        {
            replaced.Add("colours");
        }

        if (HasOverride(OverrideColorProfileFlag))
        {
            replaced.Add("profile");
        }

        if (HasOverride(OverrideOutOfMaskColorFlag))
        {
            replaced.Add("out-of-mask");
        }

        if (HasOverride(OverrideMaskPaletteFlag))
        {
            replaced.Add("palette");
        }

        string detail = replaced.Count == 0 ? "no overrides" : string.Join(", ", replaced);
        return $"IllustrationLink -> tile {TargetIndex} ({detail})";
    }
}

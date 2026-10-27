using NEngineFormat.Core;
using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;
using NEngineFormat.StaticIllustration.Data;

namespace NEngineFormat.StaticIllustration.Blocks;

/// <summary>
/// The pixels of an illustration: which ones are covered, what colours they take, and which
/// <see cref="ColorProfileBlock"/> says how to decode those colours.
/// </summary>
/// <remarks>
/// <para>Payload layout:</para>
/// <code>
/// Flags            VarULong
/// ColorProfileId   VarULong
/// [MaskPaletteId]  VarULong  only when the mask mode is Palette
/// [OutOfMaskColor] VarULong  only when the out-of-mask colour flag is set
/// [BrushCount]     VarULong  only when the brush flag is set
/// [Brushes]        per brush, see Brush
/// [MaskLength]     VarULong  only when the mask mode is not Disabled
/// [Mask]           bytes
/// ColorCount       VarULong  number of channel entries
/// PackedLength     VarULong  bytes of packed colour data
/// PackedColors     bytes     entries bit-packed at the profile's channel widths
/// </code>
/// <para>
/// <see cref="Mask"/> stays raw bytes in every mode; only its meaning changes. Under
/// <see cref="MaskMode.PerPixel"/> it is a bit per pixel; under <see cref="MaskMode.Linear"/>
/// it is the run lengths. Keeping it opaque here means a new mask mode needs no change to the
/// framing.
/// </para>
/// <para>
/// A <see cref="Brush"/> paints a shape into the tile before the mask is read, and the
/// positions it covers are settled: the mask and the entries advance over what is left, exactly
/// as they do past the pixels the image-wide mask claimed. So a box, a stroke or a wedge costs
/// its handful of numbers and nothing per pixel.
/// </para>
/// <para>
/// <see cref="Colors"/> is the unpacked, in-memory view; <see cref="PackedColors"/> is what
/// actually goes to disk. A block cannot convert between them on its own, because the channel
/// widths live in its colour profile and a block is parsed before any profile has been
/// resolved - so <see cref="StaticIllustrationAsset"/> does it, once every block is loaded.
/// </para>
/// </remarks>
[BlockId(Id)]
public class IllustrationBlock : BlockBase
{
    /// <summary>The id this block is stored under.</summary>
    public const uint Id = 4;

    /// <summary>Bits 0-1 of <see cref="Flags"/>, holding the <see cref="Data.MaskMode"/>.</summary>
    public const uint MaskModeMask = 0b11;

    /// <summary>Bit 2 of <see cref="Flags"/>: an explicit colour for pixels outside the mask.</summary>
    public const uint UseOutOfMaskColorFlag = 1u << 2;

    /// <summary>Bit 7 of <see cref="Flags"/>: shapes are painted into the tile before its mask.</summary>
    /// <remarks>
    /// Bit 7 rather than the first one going spare because bits 3 to 6 belong to
    /// <see cref="MaskedBlock"/>, which is an illustration block of its own and reads its flags
    /// through this one. A brush therefore costs a second flags byte, which is a fair price for
    /// a field only the tiles that carry shapes pay.
    /// </remarks>
    public const uint BrushFlag = 1u << 7;

    /// <summary>Most brushes accepted from a single block.</summary>
    public const int MaxBrushCount = 4096;

    /// <summary>
    /// Most colours accepted from a single block, guarding against a corrupt count turning
    /// into a huge allocation.
    /// </summary>
    public const int MaxColorCount = 64 * 1024 * 1024;

    /// <summary>Largest mask accepted, in bytes.</summary>
    public const int MaxMaskLength = 64 * 1024 * 1024;

    /// <summary>
    /// 0/1 bit: Mask Mode (0=Disabled, 1=Per pixel mask, 2=Linear (Sizes of line, where we have/haven't colors), 3=Throw, not implemented
    /// 2 bit: Use Out of Mask Color (0=Disabled, 1=Enabled) (Disabled means that out of mask pixels is MinimumColor which sets by ColorProfile
    ///
    /// </summary>
    public uint Flags { get; set; }
    public byte[] Mask { get; set; } = [];
    public uint[] Colors { get; set; } = [];
    public uint ColorProfileId { get; set; }
    public ulong OutOfMaskColor { get; set; }

    /// <summary>
    /// Which <see cref="MaskPalleteBlock"/> this tile's mask indexes into. Only meaningful
    /// under <see cref="Data.MaskMode.Palette"/>.
    /// </summary>
    public uint MaskPaletteId { get; set; }

    /// <summary>
    /// The colour entries exactly as stored: a continuous bit stream at the widths the block's
    /// profile declares. See <see cref="ColorBitPacker"/>.
    /// </summary>
    public byte[] PackedColors { get; set; } = [];

    /// <summary>
    /// How many entries <see cref="PackedColors"/> holds. Kept explicitly so a block stays
    /// skippable without resolving its profile first.
    /// </summary>
    public uint ColorCount { get; set; }

    /// <summary>
    /// Shapes painted into the tile before its mask is read, in the order they are painted.
    /// </summary>
    public Brush[] Brushes { get; set; } = [];

    public IllustrationBlock()
    {
        Type = Id;
    }

    /// <summary>The mask mode held in bits 0-1 of <see cref="Flags"/>.</summary>
    public MaskMode MaskMode
    {
        get => (MaskMode)(Flags & MaskModeMask);
        set => Flags = (Flags & ~MaskModeMask) | ((uint)value & MaskModeMask);
    }

    /// <summary>
    /// Whether <see cref="OutOfMaskColor"/> is in use. When it is not, pixels outside the mask
    /// take the minimum colour set by the colour profile.
    /// </summary>
    public bool UseOutOfMaskColor
    {
        get => (Flags & UseOutOfMaskColorFlag) != 0;
        set => Flags = value ? Flags | UseOutOfMaskColorFlag : Flags & ~UseOutOfMaskColorFlag;
    }

    /// <summary>Whether a mask is stored at all.</summary>
    public bool HasMask => MaskMode != MaskMode.Disabled;

    /// <summary>Whether shapes are painted into this tile.</summary>
    public bool HasBrushes
    {
        get => (Flags & BrushFlag) != 0;
        set => Flags = value ? Flags | BrushFlag : Flags & ~BrushFlag;
    }

    /// <summary>
    /// Which brush paints a position of the tile, or <see langword="null"/> where none does.
    /// </summary>
    /// <remarks>
    /// The last one wins: brushes paint in the order they are stored, so where two overlap the
    /// later one is what is on top. Both the decoder and the encoder ask this same question, so
    /// what a tile stores and what it draws can never drift apart.
    /// </remarks>
    public Brush? BrushAt(uint x, uint y)
    {
        for (int i = Brushes.Length - 1; i >= 0; i--)
        {
            if (Brushes[i].Covers(x, y))
            {
                return Brushes[i];
            }
        }

        return null;
    }

    /// <summary>Checks the block can be written.</summary>
    /// <exception cref="NotSupportedException"><see cref="MaskMode.Throw"/> is selected.</exception>
    /// <exception cref="InvalidOperationException">The properties do not describe a writable block.</exception>
    public virtual void Validate()
    {
        if (!HasMask && Mask.Length > 0)
        {
            throw new InvalidOperationException(
                $"Mask mode is {MaskMode} but {Mask.Length} mask byte(s) are set; they would not be written.");
        }

        if (Mask.Length > MaxMaskLength)
        {
            throw new InvalidOperationException(
                $"The mask is {Mask.Length} bytes, over the {MaxMaskLength}-byte limit.");
        }

        if (Colors.Length > MaxColorCount)
        {
            throw new InvalidOperationException(
                $"There are {Colors.Length} colours, over the {MaxColorCount} limit.");
        }

        if (PackedColors.Length > MaxColorCount)
        {
            throw new InvalidOperationException(
                $"Packed colour data is {PackedColors.Length} bytes, over the {MaxColorCount} limit.");
        }

        if (!UseOutOfMaskColor && OutOfMaskColor != 0)
        {
            throw new InvalidOperationException(
                "OutOfMaskColor is set but the out-of-mask colour flag is not; it would not be written.");
        }

        if (!HasBrushes && Brushes.Length > 0)
        {
            throw new InvalidOperationException(
                $"{Brushes.Length} brush(es) are set but the brush flag is not; they would not be written.");
        }

        // A flag with nothing behind it costs a byte and paints nothing.
        if (HasBrushes && Brushes.Length == 0)
        {
            throw new InvalidOperationException("The brush flag is set but no brush is stored.");
        }

        if (Brushes.Length > MaxBrushCount)
        {
            throw new InvalidOperationException(
                $"There are {Brushes.Length} brushes, over the {MaxBrushCount} limit.");
        }

        foreach (Brush brush in Brushes)
        {
            brush.Validate();
        }
    }

    public override void Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        Flags = (uint)package.ReadVarULong();
        ColorProfileId = (uint)package.ReadVarULong();
        MaskPaletteId = MaskMode == MaskMode.Palette ? (uint)package.ReadVarULong() : 0;
        OutOfMaskColor = UseOutOfMaskColor ? package.ReadVarULong() : 0;

        if (HasBrushes)
        {
            ulong brushCount = package.ReadVarULong();
            if (brushCount > MaxBrushCount)
            {
                throw new InvalidOperationException(
                    $"The block claims {brushCount} brushes, over the {MaxBrushCount} limit.");
            }

            Brushes = new Brush[brushCount];
            for (int i = 0; i < Brushes.Length; i++)
            {
                Brushes[i] = Brush.Read(package);
            }
        }
        else
        {
            Brushes = [];
        }

        if (HasMask)
        {
            ulong maskLength = package.ReadVarULong();
            if (maskLength > MaxMaskLength)
            {
                throw new InvalidOperationException(
                    $"The mask claims {maskLength} bytes, over the {MaxMaskLength}-byte limit.");
            }

            Mask = maskLength == 0 ? [] : package.Read((int)maskLength);
        }
        else
        {
            Mask = [];
        }

        ulong colorCount = package.ReadVarULong();
        if (colorCount > MaxColorCount)
        {
            throw new InvalidOperationException(
                $"The block claims {colorCount} colours, over the {MaxColorCount} limit.");
        }

        ulong packedLength = package.ReadVarULong();
        if (packedLength > MaxColorCount)
        {
            throw new InvalidOperationException(
                $"Packed colour data claims {packedLength} bytes, over the {MaxColorCount} limit.");
        }

        ColorCount = (uint)colorCount;

        PackedColors = new byte[packedLength];
        for (int i = 0; i < PackedColors.Length; i++)
        {
            PackedColors[i] = package.ReadByte();
        }

        // Unpacking needs the channel widths, which live in a profile this block cannot see.
        // StaticIllustrationAsset fills Colors in once every block has been read.
        Colors = [];
    }

    public override void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteVarULong(Flags);
        package.WriteVarULong(ColorProfileId);

        if (MaskMode == MaskMode.Palette)
        {
            package.WriteVarULong(MaskPaletteId);
        }

        if (UseOutOfMaskColor)
        {
            package.WriteVarULong(OutOfMaskColor);
        }

        if (HasBrushes)
        {
            package.WriteVarULong((ulong)Brushes.Length);
            foreach (Brush brush in Brushes)
            {
                brush.Write(package);
            }
        }

        if (HasMask)
        {
            package.WriteVarULong((ulong)Mask.Length);
            if (Mask.Length > 0)
            {
                package.Write(Mask);
            }
        }

        package.WriteVarULong(ColorCount);
        package.WriteVarULong((ulong)PackedColors.Length);
        foreach (byte value in PackedColors)
        {
            package.WriteByte(value);
        }
    }

    public override string ToString()
    {
        string mask = HasMask ? $"{MaskMode} mask {Mask.Length} bytes" : "no mask";
        string outOfMask = UseOutOfMaskColor ? $", out-of-mask {OutOfMaskColor}" : string.Empty;
        string brushes = HasBrushes ? $", {Brushes.Length} brush(es)" : string.Empty;
        return $"Illustration profile={ColorProfileId}, {mask}, {ColorCount} entr(ies){outOfMask}{brushes}";
    }
}

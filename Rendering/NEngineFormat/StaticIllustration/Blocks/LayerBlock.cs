using NEngineFormat.Core;
using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;

namespace NEngineFormat.StaticIllustration.Blocks;

/// <summary>
/// Starts a layer: every tile-bearing block after it belongs to that layer rather than to the
/// one before.
/// </summary>
/// <remarks>
/// <para>Payload layout:</para>
/// <code>
/// LayerIndex  VarULong  which layer this starts, counting from zero
/// Flags       VarULong  bit 0: hidden
/// Opacity     VarULong  0 to 255, how much of the layer shows
/// BlendMode   VarULong  how the layer combines with what is under it
/// Name        string    length-prefixed UTF-8, empty for an unnamed layer
/// </code>
/// <para>
/// A picture is one layer unless it says otherwise, so a file with no layer block at all is
/// exactly the file it always was. Where there are several, the tiles simply go on counting: a
/// picture of six tiles has slots 0 to 5 in its first layer, 6 to 11 in its second, and so on.
/// That costs nothing to say and buys something real - a difference or a link may point at a
/// tile of an earlier layer, which is where the matches often are, because layers of one
/// drawing tend to resemble one another far more than neighbouring tiles do.
/// </para>
/// <para>
/// An <see cref="ImageMaskBlock"/> belongs to the layer it appears in, so each layer may paint
/// its own background image-wide.
/// </para>
/// <para>
/// <see cref="BlendMode"/> is carried rather than understood: this format stores what the
/// program that made the picture called it, and <see cref="StaticIllustrationAsset.DecodeRgba"/>
/// lays every visible layer over the one below at its opacity. A host that knows its own blend
/// modes gets them back exactly as it gave them.
/// </para>
/// </remarks>
[BlockId(Id)]
public class LayerBlock : BlockBase
{
    /// <summary>The id this block is stored under.</summary>
    public const uint Id = 13;

    /// <summary>Bit 0 of <see cref="Flags"/>: the layer is not drawn.</summary>
    public const uint HiddenFlag = 1u << 0;

    /// <summary>Every flag this format defines, for rejecting the ones it does not.</summary>
    public const uint AllFlags = HiddenFlag;

    /// <summary>Fully opaque, which is what a layer is unless it says otherwise.</summary>
    public const uint FullOpacity = 255;

    /// <summary>Most layers a file may describe.</summary>
    public const uint MaxLayers = 4096;

    /// <summary>Longest layer name accepted, in characters.</summary>
    public const int MaxNameLength = 1024;

    public LayerBlock() => Type = Id;

    /// <summary>Which layer this block starts, counting from zero and upwards.</summary>
    public uint LayerIndex { get; set; }

    /// <summary>Bitmask of the things this format says about a layer.</summary>
    public uint Flags { get; set; }

    /// <summary>How much of the layer shows, 0 to 255.</summary>
    public uint Opacity { get; set; } = FullOpacity;

    /// <summary>How the layer combines with what is under it, as the host that made it says.</summary>
    public uint BlendMode { get; set; }

    /// <summary>What the layer is called, empty when it has no name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether the layer is drawn at all.</summary>
    public bool Hidden
    {
        get => (Flags & HiddenFlag) != 0;
        set => Flags = value ? Flags | HiddenFlag : Flags & ~HiddenFlag;
    }

    /// <summary>Checks the block can be written.</summary>
    /// <exception cref="InvalidOperationException">The properties do not describe a writable layer.</exception>
    public void Validate()
    {
        if ((Flags & ~AllFlags) != 0)
        {
            throw new InvalidOperationException(
                $"Flags 0x{Flags:X} sets bits this format does not define for a layer.");
        }

        if (LayerIndex >= MaxLayers)
        {
            throw new InvalidOperationException(
                $"Layer {LayerIndex} is past the {MaxLayers} limit.");
        }

        if (Opacity > FullOpacity)
        {
            throw new InvalidOperationException(
                $"A layer shows at {Opacity}, but opacity runs from 0 to {FullOpacity}.");
        }

        if (Name.Length > MaxNameLength)
        {
            throw new InvalidOperationException(
                $"The layer name is {Name.Length} characters, over the {MaxNameLength} limit.");
        }
    }

    public override void Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        LayerIndex = (uint)package.ReadVarULong();
        Flags = (uint)package.ReadVarULong();
        Opacity = (uint)package.ReadVarULong();
        BlendMode = (uint)package.ReadVarULong();
        Name = package.ReadString();

        Validate();
    }

    public override void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteVarULong(LayerIndex);
        package.WriteVarULong(Flags);
        package.WriteVarULong(Opacity);
        package.WriteVarULong(BlendMode);
        package.WriteString(Name);
    }

    public override string ToString() =>
        $"Layer {LayerIndex}{(Name.Length > 0 ? $" \"{Name}\"" : string.Empty)}: "
        + $"{(Hidden ? "hidden" : "shown")}, opacity {Opacity}, blend {BlendMode}";
}

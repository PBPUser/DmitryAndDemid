using NEngineFormat.Core.Utils;

namespace NEngineFormat.Core;

/// <summary>
/// The base storage unit of an NEngine format file: an identified, sized bunch of bytes.
/// </summary>
public class BlockBase
{
    public BlockBase()
    {
    }

    public BlockBase(uint type, byte[] data)
    {
        Type = type;
        Size = (uint)data.Length;
        Data = data;
    }

    /// <summary>What kind of block this is.</summary>
    public uint Type { get; set; }

    /// <summary>The declared payload size.</summary>
    public uint Size { get; set; }

    /// <summary>The block's contents.</summary>
    public byte[] Data { get; set; } = [];

    public override string ToString() => $"Block type={Type} size={Size} data={Data.Length} bytes";

    public virtual void Read(BitPackage package) { }

    /// <summary>
    /// Writes the block's payload. The default emits <see cref="Data"/> unchanged, which is
    /// what lets a block of an unrecognised type survive a load-then-save round-trip: a reader
    /// that cannot interpret it still hands back the exact bytes it was given.
    /// </summary>
    public virtual void Write(BitPackage package) => package.Write(Data);
}

using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;

namespace NEngineFormat.Core.Blocks;

/// <summary>
/// Used for information, such as the author of picture.
/// </summary>
[BlockId(Id)]
public class MetaStringBlock : BlockBase
{
    /// <summary>The id this block is stored under.</summary>
    public const uint Id = 1;

    public MetaStringBlock() : this(string.Empty, string.Empty)
    {
    }

    public MetaStringBlock(string name, string value)
    {
        Type = Id;
        Name = name;
        Value = value;
    }

    public string Name { get; set; }
    public string Value { get; set; }

    public override void Read(BitPackage package)
    {
        Name = package.ReadString();
        Value = package.ReadString();
    }

    public override void Write(BitPackage package)
    {
        package.WriteString(Name);
        package.WriteString(Value);
    }

    public override string ToString() => $"{Name}={Value}";
}

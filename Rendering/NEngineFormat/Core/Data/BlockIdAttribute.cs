namespace NEngineFormat.Core.Data;

/// <summary>
/// Assigns the on-disk id a block type is stored under.
/// </summary>
/// <remarks>
/// The id is what a file carries; the class is what the id resolves to when reading. Declare
/// it once as a constant on the block and point the attribute at that constant, so the two
/// can never disagree.
/// </remarks>
/// <example>
/// <code>
/// [BlockId(Id)]
/// public class MetaStringBlock : BlockBase
/// {
///     public const uint Id = 1;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BlockIdAttribute(uint id) : Attribute
{
    /// <summary>The id this block type is stored under.</summary>
    public uint Id { get; } = id;
}

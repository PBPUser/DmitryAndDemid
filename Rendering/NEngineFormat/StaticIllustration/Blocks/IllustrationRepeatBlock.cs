using NEngineFormat.Core;
using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;

namespace NEngineFormat.StaticIllustration.Blocks;

/// <summary>
/// Says that one tile's content is repeated verbatim at a list of other tiles, replacing a run
/// of plain links that would each have cost a block of their own.
/// </summary>
/// <remarks>
/// <para>Payload layout:</para>
/// <code>
/// TargetTile  VarULong  the tile being repeated
/// Count       VarULong  how many tiles repeat it
/// First       VarULong  the lowest tile number
/// Gaps        VarULong x(Count-1), each the step from the previous tile minus one
/// </code>
/// <para>
/// A link with no overrides carries a target and an empty override mask, and pays block framing
/// on top. Where many tiles repeat the same one - a flat background is the usual case - naming
/// them in a single list costs a byte or two each instead.
/// </para>
/// <para>
/// Tile numbers are stored sorted and as gaps, because a run of repeats is usually clustered
/// and a gap of one costs a single byte where an absolute index would cost two or three.
/// </para>
/// <para>
/// Unlike an illustration or a link, this block does <b>not</b> occupy a tile slot. It claims
/// the slots it names, and the positional blocks fill whatever is left, in order.
/// </para>
/// </remarks>
[BlockId(Id)]
public class IllustrationRepeatBlock : BlockBase
{
    /// <summary>The id this block is stored under.</summary>
    public const uint Id = 7;

    /// <summary>Most tiles one repeat block may name.</summary>
    public const int MaxTiles = 1 << 24;

    public IllustrationRepeatBlock() => Type = Id;

    /// <summary>The tile whose content every listed tile takes.</summary>
    public uint TargetTile { get; set; }

    /// <summary>The tiles that repeat it, ascending and without duplicates.</summary>
    public uint[] Tiles { get; set; } = [];

    /// <summary>Checks the block can be written.</summary>
    /// <exception cref="InvalidOperationException">The tile list is empty, unsorted or repeats itself.</exception>
    public void Validate()
    {
        if (Tiles.Length is 0 or > MaxTiles)
        {
            throw new InvalidOperationException(
                $"A repeat block must name 1..{MaxTiles} tiles, got {Tiles.Length}.");
        }

        for (int i = 1; i < Tiles.Length; i++)
        {
            // Gaps are stored, so the list has to climb; equal neighbours would also mean two
            // blocks claiming one slot.
            if (Tiles[i] <= Tiles[i - 1])
            {
                throw new InvalidOperationException(
                    $"Repeat tiles must ascend without repeating; {Tiles[i]} follows {Tiles[i - 1]}.");
            }
        }

        if (Array.IndexOf(Tiles, TargetTile) >= 0)
        {
            throw new InvalidOperationException(
                $"Tile {TargetTile} cannot repeat itself.");
        }
    }

    public override void Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        TargetTile = (uint)package.ReadVarULong();

        ulong count = package.ReadVarULong();
        if (count is 0 or > (ulong)MaxTiles)
        {
            throw new InvalidOperationException(
                $"A repeat block must name 1..{MaxTiles} tiles, got {count}.");
        }

        Tiles = new uint[count];
        ulong tile = package.ReadVarULong();
        Tiles[0] = (uint)tile;

        for (int i = 1; i < Tiles.Length; i++)
        {
            // Each gap is the step from the previous tile, less the one that is always there.
            tile += package.ReadVarULong() + 1;
            if (tile > uint.MaxValue)
            {
                throw new InvalidOperationException($"Repeat tile {tile} does not fit in 32 bits.");
            }

            Tiles[i] = (uint)tile;
        }
    }

    public override void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteVarULong(TargetTile);
        package.WriteVarULong((ulong)Tiles.Length);
        package.WriteVarULong(Tiles[0]);

        for (int i = 1; i < Tiles.Length; i++)
        {
            package.WriteVarULong(Tiles[i] - Tiles[i - 1] - 1);
        }
    }

    public override string ToString() => $"IllustrationRepeat: tile {TargetTile} at {Tiles.Length} other tile(s)";
}

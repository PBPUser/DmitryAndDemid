namespace NEngineFormat.StaticIllustration.Data;

/// <summary>How a tile reads the pixels of the tile it copies from.</summary>
/// <remarks>
/// The three bits combine into the eight ways a square can be laid over itself - four turns,
/// each of them optionally mirrored. A drawing repeats itself turned or reflected far more often
/// than it repeats itself exactly: the corners of a frame, the two halves of a symmetrical
/// shape, a tiled texture laid down in alternating directions.
/// </remarks>
[Flags]
public enum TileTransform : uint
{
    /// <summary>Read straight through, position for position.</summary>
    None = 0,

    /// <summary>Columns are read right to left.</summary>
    MirrorX = 1,

    /// <summary>Rows are read bottom to top.</summary>
    MirrorY = 2,

    /// <summary>Rows and columns are swapped, which only a square tile can do.</summary>
    Transpose = 4,
}

/// <summary>Working with the eight orientations a tile can be read in.</summary>
public static class TileTransforms
{
    /// <summary>Every orientation, plainest first.</summary>
    public static readonly TileTransform[] All =
    [
        TileTransform.None,
        TileTransform.MirrorX,
        TileTransform.MirrorY,
        TileTransform.MirrorX | TileTransform.MirrorY,
        TileTransform.Transpose,
        TileTransform.Transpose | TileTransform.MirrorX,
        TileTransform.Transpose | TileTransform.MirrorY,
        TileTransform.Transpose | TileTransform.MirrorX | TileTransform.MirrorY,
    ];

    /// <summary>Whether an orientation can only be read out of a square tile.</summary>
    /// <remarks>
    /// Swapping rows for columns turns a tile on its side, so a tile wider than it is tall would
    /// be read outside itself. Mirroring does not move a position out of the rectangle it is in,
    /// and so needs nothing of the shape.
    /// </remarks>
    public static bool NeedsSquare(this TileTransform transform) =>
        (transform & TileTransform.Transpose) != 0;

    /// <summary>Which position of the source tile a position of this tile is drawn from.</summary>
    /// <remarks>
    /// Transposing first and then mirroring is what makes the eight distinct: mirroring first
    /// would give the same eight in a different order, but the decoder and the encoder have to
    /// agree on which bits mean which orientation, and this is that order.
    /// </remarks>
    public static void Map(
        this TileTransform transform,
        uint x,
        uint y,
        uint width,
        uint height,
        out uint sourceX,
        out uint sourceY)
    {
        if ((transform & TileTransform.Transpose) != 0)
        {
            (x, y) = (y, x);
        }

        sourceX = (transform & TileTransform.MirrorX) != 0 && width > 0 ? width - 1 - x : x;
        sourceY = (transform & TileTransform.MirrorY) != 0 && height > 0 ? height - 1 - y : y;
    }

    /// <summary>What to call an orientation when it is being reported.</summary>
    public static string Describe(this TileTransform transform) => transform switch
    {
        TileTransform.None => "straight",
        TileTransform.MirrorX => "flipped across",
        TileTransform.MirrorY => "flipped down",
        TileTransform.MirrorX | TileTransform.MirrorY => "turned 180",
        TileTransform.Transpose => "transposed",
        TileTransform.Transpose | TileTransform.MirrorX => "turned left",
        TileTransform.Transpose | TileTransform.MirrorY => "turned right",
        _ => "transposed and turned 180",
    };
}

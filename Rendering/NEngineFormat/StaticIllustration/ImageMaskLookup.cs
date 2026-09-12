using NEngineFormat.StaticIllustration.Blocks;

namespace NEngineFormat.StaticIllustration;

/// <summary>
/// Random access into an <see cref="ImageMaskBlock"/>'s runs.
/// </summary>
/// <remarks>
/// Runs are written in row-major order but read tile by tile, so lookups jump about. Building
/// the run start offsets once and binary searching them keeps that cheap without expanding the
/// mask back into an index a pixel, which is the cost the runs existed to avoid.
/// </remarks>
public sealed class ImageMaskLookup
{
    private readonly long[] _starts;
    private readonly uint[] _indices;
    private readonly uint _escape;

    /// <summary>
    /// One byte a pixel for a mask written as rectangles: zero where nothing claims it, and
    /// the palette index plus one where something does.
    /// </summary>
    /// <remarks>
    /// Rectangles cannot be searched the way runs can - a pixel may fall in any of them - so
    /// they are painted out once instead. A byte a pixel is a great deal of memory for a large
    /// picture, but it is memory rather than file, and it is the only form that answers a
    /// lookup in a step.
    /// </remarks>
    private readonly byte[]? _painted;

    /// <summary>Prepares lookups for one mask against the palette it names.</summary>
    public ImageMaskLookup(ImageMaskBlock mask, MaskPalleteBlock palette)
        : this(mask, palette, 0, 0)
    {
    }

    /// <summary>
    /// Prepares lookups, told the picture's size so a mask written as rectangles can be
    /// painted out.
    /// </summary>
    public ImageMaskLookup(ImageMaskBlock mask, MaskPalleteBlock palette, uint width, uint height)
    {
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(palette);

        _indices = mask.RunIndices;
        _escape = palette.EscapeIndex;

        if (mask.IsRectangles)
        {
            _starts = [];
            Pixels = (long)width * height;
            _painted = new byte[Pixels];

            foreach ((uint index, uint x, uint y, uint rectWidth, uint rectHeight) in mask.Rectangles)
            {
                if (index >= _escape)
                {
                    continue;
                }

                for (uint row = y; row < y + rectHeight && row < height; row++)
                {
                    for (uint column = x; column < x + rectWidth && column < width; column++)
                    {
                        _painted[(row * width) + column] = (byte)(index + 1);
                    }
                }
            }

            return;
        }

        _starts = new long[mask.RunLengths.Length];

        long at = 0;
        for (int i = 0; i < mask.RunLengths.Length; i++)
        {
            _starts[i] = at;
            at += mask.RunLengths[i];
        }

        Pixels = at;
    }

    /// <summary>How many pixels the runs cover.</summary>
    public long Pixels { get; }

    /// <summary>
    /// The palette entry claiming a pixel, or <see langword="null"/> when the pixel is left to
    /// its tile.
    /// </summary>
    public uint? ClaimedIndex(long pixel)
    {
        if (pixel < 0 || pixel >= Pixels)
        {
            return null;
        }

        if (_painted is not null)
        {
            byte claimed = _painted[pixel];
            return claimed == 0 ? null : (uint)(claimed - 1);
        }

        if (_starts.Length == 0)
        {
            return null;
        }

        // The run holding this pixel is the last one starting at or before it.
        int low = 0;
        int high = _starts.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (_starts[middle] <= pixel)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        uint index = _indices[low];
        return index >= _escape ? null : index;
    }
}

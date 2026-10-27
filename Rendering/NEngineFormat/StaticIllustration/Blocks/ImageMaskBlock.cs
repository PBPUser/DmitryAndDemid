using NEngineFormat.Core;
using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;

namespace NEngineFormat.StaticIllustration.Blocks;

/// <summary>
/// A palette mask covering the whole picture rather than one tile, naming the colours common
/// enough to be worth lifting out of every block at once.
/// </summary>
/// <remarks>
/// <para>Payload layout, as runs:</para>
/// <code>
/// MaskPaletteId  VarULong  the palette these runs name
/// RunLengthBits  byte      width of one run length, 1 to 32; zero means rectangles
/// RunCount       VarULong
/// PackedLength   VarULong  bytes of packed runs
/// PackedRuns     bytes     per run: two or more colours, the id then the length;
///                          one colour, the length then the id
/// </code>
/// <para>And as rectangles:</para>
/// <code>
/// MaskPaletteId  VarULong
/// RunLengthBits  byte      zero, marking what follows as rectangles
/// RectCount      VarULong
/// Rectangles     RectCount x (Index, X, Y, Width, Height) VarULong each
/// </code>
/// <para>
/// The same claim written two ways. Runs suit a picture that changes constantly along each
/// row; rectangles suit one with broad flat areas, where a single rectangle stands in for as
/// many runs as it covers rows. The encoder builds both and keeps whichever is smaller, and a
/// reader tells them apart by the run width, which no run form can leave at zero.
/// </para>
/// <para>
/// The runs are bit-packed exactly as a block's colour entries are: written most significant
/// bit first and running across byte boundaries, so with three-bit fields they lay out as
/// <c>AAABBBCC CDDDEEEF FF...</c>.
/// </para>
/// <para>
/// Which field of a run comes first depends on how many colours the mask paints. Naming two or
/// more, a run leads with the colour it applies and then says how far it applies - the reader
/// knows what it is looking at before it knows how much of it there is. Naming one, there is
/// nothing to lead with: the index only ever says claimed or not, so the length keeps its
/// place in front. <see cref="IdFirst"/> decides, from the index width alone.
/// </para>
/// <para>
/// The width is chosen per image rather than fixed. A picture of long flat stretches wants wide
/// lengths and few runs; a busy one wants narrow lengths, splitting the occasional long run
/// rather than paying for a wide field on every short one.
/// </para>
/// <para>
/// A length is stored one less than it is, since a run of zero pixels would mean nothing. A
/// pixel this mask names is finished with: the tile covering it stores nothing for it at all.
/// </para>
/// </remarks>
[BlockId(Id)]
public class ImageMaskBlock : BlockBase
{
    /// <summary>The id this block is stored under.</summary>
    public const uint Id = 8;

    /// <summary>Most runs one image mask may hold.</summary>
    public const int MaxRuns = 1 << 24;

    /// <summary>Most rectangles one image mask may hold.</summary>
    public const int MaxRectangles = 1 << 22;

    public ImageMaskBlock() => Type = Id;

    /// <summary>Which <see cref="MaskPalleteBlock"/> these runs name.</summary>
    public uint MaskPaletteId { get; set; }

    /// <summary>Bits one run length occupies, 1 to 32.</summary>
    public int RunLengthBits { get; set; } = 8;

    /// <summary>How many runs <see cref="PackedRuns"/> holds.</summary>
    public uint RunCount { get; set; }

    /// <summary>The runs exactly as stored.</summary>
    public byte[] PackedRuns { get; set; } = [];

    /// <summary>How many pixels each run covers, in row-major order. Unpacked view.</summary>
    public uint[] RunLengths { get; set; } = [];

    /// <summary>The palette index each run names, escape included. Unpacked view.</summary>
    public uint[] RunIndices { get; set; } = [];

    /// <summary>
    /// The rectangles this mask claims, when it is written as rectangles rather than runs.
    /// </summary>
    /// <remarks>
    /// Each names one palette colour and the area it covers. They do not overlap, and between
    /// them they claim exactly what the runs would have claimed.
    /// </remarks>
    public (uint Index, uint X, uint Y, uint Width, uint Height)[] Rectangles { get; set; } = [];

    /// <summary>
    /// Whether the claim is written as rectangles. A run width of zero means no run can be
    /// read, which is what marks the other form.
    /// </summary>
    public bool IsRectangles => RunLengthBits == 0;

    /// <summary>The largest run one length field can express at a given width.</summary>
    public static long MaxRunAt(int lengthBits) => 1L << lengthBits;

    /// <summary>
    /// Whether a run leads with the colour it applies rather than with its length.
    /// </summary>
    /// <remarks>
    /// A palette of one colour needs a single bit to say claimed or not, and a palette of two
    /// or more needs at least two - so the index width says how many colours the mask paints
    /// without the block having to hold the palette itself.
    /// </remarks>
    public static bool IdFirst(int indexBits) => indexBits >= 2;

    /// <summary>How many pixels the runs cover between them.</summary>
    public long PixelCount
    {
        get
        {
            long total = 0;
            foreach (uint length in RunLengths)
            {
                total += length;
            }

            return total;
        }
    }

    /// <summary>
    /// The run-length width that makes the packed runs smallest, counting the extra runs a
    /// narrow width forces by splitting the long ones.
    /// </summary>
    public static int OptimalRunBits(IReadOnlyList<uint> lengths, int indexBits)
    {
        ArgumentNullException.ThrowIfNull(lengths);

        int best = 8;
        long bestCost = long.MaxValue;

        for (int bits = 1; bits <= 32; bits++)
        {
            long max = MaxRunAt(bits);
            long runs = 0;

            foreach (uint length in lengths)
            {
                // A run longer than the field allows is written as several, so a narrow width
                // is not free just because the field is small.
                runs += ((length - 1) / max) + 1;
            }

            long cost = runs * (bits + indexBits);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = bits;
            }
        }

        return best;
    }

    /// <summary>Packs runs into a bit stream, splitting any that overflow the length field.</summary>
    public static (byte[] Packed, uint Count) PackRuns(
        IReadOnlyList<uint> lengths,
        IReadOnlyList<uint> indices,
        int lengthBits,
        int indexBits)
    {
        ArgumentNullException.ThrowIfNull(lengths);
        ArgumentNullException.ThrowIfNull(indices);

        var splitLengths = new List<uint>(lengths.Count);
        var splitIndices = new List<uint>(lengths.Count);
        long max = MaxRunAt(lengthBits);

        for (int i = 0; i < lengths.Count; i++)
        {
            long remaining = lengths[i];
            while (remaining > 0)
            {
                long take = Math.Min(remaining, max);
                splitLengths.Add((uint)take);
                splitIndices.Add(indices[i]);
                remaining -= take;
            }
        }

        int perRun = lengthBits + indexBits;
        var packed = new byte[(((long)splitLengths.Count * perRun) + 7) / 8];
        long bit = 0;

        bool idFirst = IdFirst(indexBits);

        for (int i = 0; i < splitLengths.Count; i++)
        {
            if (idFirst)
            {
                WriteBits(packed, ref bit, splitIndices[i], indexBits);
            }

            // Stored one less than it is, so a full field means the largest run rather than one
            // that covers nothing.
            WriteBits(packed, ref bit, splitLengths[i] - 1, lengthBits);

            if (!idFirst)
            {
                WriteBits(packed, ref bit, splitIndices[i], indexBits);
            }
        }

        return (packed, (uint)splitLengths.Count);
    }

    /// <summary>Reads runs back out of a bit stream.</summary>
    public static (uint[] Lengths, uint[] Indices) UnpackRuns(
        byte[] packed,
        uint count,
        int lengthBits,
        int indexBits)
    {
        ArgumentNullException.ThrowIfNull(packed);

        long needed = (long)count * (lengthBits + indexBits);
        if (needed > (long)packed.Length * 8)
        {
            throw new InvalidOperationException(
                $"{count} run(s) need {needed} bits but only {packed.Length * 8} were stored.");
        }

        var lengths = new uint[count];
        var indices = new uint[count];
        bool idFirst = IdFirst(indexBits);
        long bit = 0;

        for (int i = 0; i < count; i++)
        {
            if (idFirst)
            {
                indices[i] = ReadBits(packed, ref bit, indexBits);
            }

            lengths[i] = ReadBits(packed, ref bit, lengthBits) + 1;

            if (!idFirst)
            {
                indices[i] = ReadBits(packed, ref bit, indexBits);
            }
        }

        return (lengths, indices);
    }

    /// <summary>Checks the block can be written.</summary>
    /// <exception cref="InvalidOperationException">The runs are inconsistent or empty.</exception>
    public void Validate()
    {
        if (IsRectangles)
        {
            // Zero is what marks the rectangle form, so a block holding runs as well is saying
            // two different things and only one of them would be written.
            if (RunLengths.Length > 0 || RunIndices.Length > 0 || PackedRuns.Length > 0)
            {
                throw new InvalidOperationException(
                    "An image mask written as rectangles cannot hold runs as well.");
            }

            if (Rectangles.Length > MaxRectangles)
            {
                throw new InvalidOperationException(
                    $"An image mask holds {Rectangles.Length} rectangles, over the {MaxRectangles} limit.");
            }

            foreach ((uint _, uint _, uint _, uint rectWidth, uint rectHeight) in Rectangles)
            {
                // A rectangle covering nothing would claim nothing and only cost bytes.
                if (rectWidth == 0 || rectHeight == 0)
                {
                    throw new InvalidOperationException(
                        "An image mask rectangle must cover at least one pixel.");
                }
            }

            return;
        }

        if (RunLengthBits is < 1 or > 32)
        {
            throw new InvalidOperationException($"RunLengthBits must be 1..32, got {RunLengthBits}.");
        }

        if (RunCount > MaxRuns)
        {
            throw new InvalidOperationException(
                $"An image mask holds {RunCount} runs, over the {MaxRuns} limit.");
        }

        if (RunLengths.Length != RunIndices.Length)
        {
            throw new InvalidOperationException(
                $"An image mask holds {RunLengths.Length} run length(s) against {RunIndices.Length} index(es).");
        }

        foreach (uint length in RunLengths)
        {
            // A zero-length run would cover nothing and leave everything after it misaligned,
            // which is far harder to notice than a rejected file.
            if (length == 0)
            {
                throw new InvalidOperationException("An image mask run must cover at least one pixel.");
            }
        }
    }

    public override void Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        MaskPaletteId = (uint)package.ReadVarULong();

        RunLengthBits = package.ReadByte();

        if (IsRectangles)
        {
            ReadRectangles(package);
            return;
        }

        if (RunLengthBits > 32)
        {
            throw new InvalidOperationException($"RunLengthBits must be 1..32, got {RunLengthBits}.");
        }

        ulong runs = package.ReadVarULong();
        if (runs > MaxRuns)
        {
            throw new InvalidOperationException(
                $"An image mask claims {runs} runs, over the {MaxRuns} limit.");
        }

        RunCount = (uint)runs;

        ulong packedLength = package.ReadVarULong();
        if (packedLength > IllustrationBlock.MaxMaskLength)
        {
            throw new InvalidOperationException(
                $"An image mask claims {packedLength} packed bytes, over the {IllustrationBlock.MaxMaskLength}-byte limit.");
        }

        PackedRuns = new byte[packedLength];
        for (int i = 0; i < PackedRuns.Length; i++)
        {
            PackedRuns[i] = package.ReadByte();
        }

        // Unpacking needs the palette's index width, which this block cannot resolve on its
        // own; StaticIllustrationAsset fills the runs in once every block has been read.
        RunLengths = [];
        RunIndices = [];
    }

    public override void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteVarULong(MaskPaletteId);
        package.WriteByte((byte)RunLengthBits);

        if (IsRectangles)
        {
            WriteRectangles(package);
            return;
        }

        package.WriteVarULong(RunCount);
        package.WriteVarULong((ulong)PackedRuns.Length);

        foreach (byte value in PackedRuns)
        {
            package.WriteByte(value);
        }
    }

    public override string ToString() => IsRectangles
        ? $"ImageMask: palette #{MaskPaletteId}, {Rectangles.Length} rectangle(s)"
        : $"ImageMask: palette #{MaskPaletteId}, {RunCount} run(s) at {RunLengthBits} bit(s), {PackedRuns.Length} bytes";

    /// <summary>Reads the rectangle form.</summary>
    private void ReadRectangles(BitPackage package)
    {
        ulong count = package.ReadVarULong();
        if (count > MaxRectangles)
        {
            throw new InvalidOperationException(
                $"An image mask claims {count} rectangles, over the {MaxRectangles} limit.");
        }

        Rectangles = new (uint, uint, uint, uint, uint)[count];
        for (int i = 0; i < Rectangles.Length; i++)
        {
            Rectangles[i] = (
                (uint)package.ReadVarULong(),
                (uint)package.ReadVarULong(),
                (uint)package.ReadVarULong(),
                (uint)package.ReadVarULong(),
                (uint)package.ReadVarULong());
        }

        RunCount = 0;
        RunLengths = [];
        RunIndices = [];
        PackedRuns = [];

        Validate();
    }

    /// <summary>Writes the rectangle form.</summary>
    private void WriteRectangles(BitPackage package)
    {
        package.WriteVarULong((ulong)Rectangles.Length);

        foreach ((uint index, uint x, uint y, uint rectWidth, uint rectHeight) in Rectangles)
        {
            package.WriteVarULong(index);
            package.WriteVarULong(x);
            package.WriteVarULong(y);
            package.WriteVarULong(rectWidth);
            package.WriteVarULong(rectHeight);
        }
    }

    private static void WriteBits(byte[] target, ref long bit, uint value, int bits)
    {
        for (int i = bits - 1; i >= 0; i--)
        {
            if (((value >> i) & 1) != 0)
            {
                target[bit >> 3] |= (byte)(0x80 >> (int)(bit & 7));
            }

            bit++;
        }
    }

    private static uint ReadBits(byte[] source, ref long bit, int bits)
    {
        uint value = 0;
        for (int i = 0; i < bits; i++)
        {
            value <<= 1;
            if ((source[bit >> 3] & (0x80 >> (int)(bit & 7))) != 0)
            {
                value |= 1;
            }

            bit++;
        }

        return value;
    }
}

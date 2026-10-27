using NEngineFormat.Core;
using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;
using NEngineFormat.StaticIllustration.Data;

namespace NEngineFormat.StaticIllustration.Blocks;

/// <summary>
/// The values no pixel of the picture uses, per channel, so no profile has to describe them
/// again.
/// </summary>
/// <remarks>
/// <para>Payload layout:</para>
/// <code>
/// RegionsX  VarULong                  columns the picture is divided into, 1 to 6
/// RegionsY  VarULong                  rows, 1 to 6
/// per region, row-major:
///   per channel, in R,G,B,A order:
///     Count  VarULong                 how many runs follow
///     Runs   Count x (usable, skipped) VarULong pairs
/// </code>
/// <para>
/// A <see cref="ColorProfileBlock"/> already drops the values its own tiles never reach, but it
/// pays for every run it describes, so a gap has to earn its bytes before that profile will
/// take it. Here the gaps are described once for the whole picture and every profile that opts
/// in gets them for nothing - so a gap too small to pay for itself sixty times over still
/// narrows all sixty channels.
/// </para>
/// <para>
/// A profile that sets <see cref="ColorProfileBlock.GlobalSkipsFlag"/> works in the numbering
/// this block leaves behind: its minimum, its own runs and its entries are all counted in
/// values that the picture actually uses, and this block turns the result back into a colour.
/// </para>
/// <para>
/// One table for the whole picture only suits a picture that is much the same throughout. A
/// dark left and a bright right use the whole range between them and so skip nothing, while
/// each half on its own skips most of it. Dividing the picture into a few columns and rows and
/// describing each separately costs one table per region and is worth it exactly when the
/// regions differ; the encoder measures both and takes the cheaper. Which table a tile counts
/// in follows from where it is, so nothing has to be stored per tile to say so.
/// </para>
/// </remarks>
[BlockId(Id)]
public class ColorSkipsBlock : BlockBase
{
    /// <summary>The id this block is stored under.</summary>
    public const uint Id = 11;

    /// <summary>Most runs one channel may describe.</summary>
    public const int MaxRunsPerChannel = 128;

    /// <summary>Values a channel can take before any are skipped.</summary>
    public const int ChannelValues = 256;

    /// <summary>Most columns or rows the picture may be divided into.</summary>
    public const int MaxRegions = 6;

    public ColorSkipsBlock() => Type = Id;

    /// <summary>Columns the picture is divided into.</summary>
    public int RegionsX { get; set; } = 1;

    /// <summary>Rows the picture is divided into.</summary>
    public int RegionsY { get; set; } = 1;

    /// <summary>How many tables this block holds.</summary>
    public int RegionCount => RegionsX * RegionsY;

    /// <summary>
    /// Each region's channel runs, row-major, then in R,G,B,A order within a region: pairs of
    /// usable then skipped values.
    /// </summary>
    public uint[][][] Regions { get; set; } =
        [[[], [], [], []]];

    /// <summary>
    /// The one region's channel runs, for a block that divides the picture no further.
    /// </summary>
    public uint[][] Channels
    {
        get => Regions[0];
        set => Regions = [value];
    }

    /// <summary>Which table a tile counts in, from where it sits.</summary>
    /// <remarks>
    /// A tile belongs to one region whole, so a tile straddling a boundary still counts in a
    /// single table - the one its top-left corner falls in.
    /// </remarks>
    public int RegionOf(uint tileX, uint tileY, uint tilesX, uint tilesY)
    {
        int column = tilesX == 0 ? 0 : (int)Math.Min(tileX * (uint)RegionsX / tilesX, (uint)RegionsX - 1);
        int row = tilesY == 0 ? 0 : (int)Math.Min(tileY * (uint)RegionsY / tilesY, (uint)RegionsY - 1);
        return (row * RegionsX) + column;
    }

    /// <summary>Whether any channel of any region skips anything at all.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (uint[][] region in Regions)
            {
                foreach (uint[] runs in region)
                {
                    if (runs.Length > 0)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>One channel's runs in one region.</summary>
    public ReadOnlySpan<uint> For(int region, int channel) =>
        region >= 0 && region < Regions.Length && channel >= 0 && channel < Regions[region].Length
            ? Regions[region][channel]
            : default;

    /// <summary>One channel's runs, for a block holding a single table.</summary>
    public ReadOnlySpan<uint> For(int channel) => For(0, channel);

    /// <summary>Turns a number in a region's numbering back into a channel value.</summary>
    public uint Expand(int region, int channel, uint compact) =>
        SkipRuns.Expand(For(region, channel), compact);

    /// <summary>Turns a channel value into a region's numbering.</summary>
    /// <exception cref="InvalidOperationException">No pixel of that region uses the value.</exception>
    public uint Compact(int region, int channel, uint value) =>
        SkipRuns.Compact(For(region, channel), value);

    /// <summary>Whether a region uses a value at all.</summary>
    public bool Holds(int region, int channel, uint value) =>
        SkipRuns.Holds(For(region, channel), value);

    /// <summary>How many values one channel is left with in a region.</summary>
    public int Remaining(int region, int channel) =>
        SkipRuns.Remaining(For(region, channel), ChannelValues);

    /// <summary>The single table's forms, for a block that holds only one.</summary>
    public uint Expand(int channel, uint compact) => Expand(0, channel, compact);

    /// <summary>The single table's forms, for a block that holds only one.</summary>
    public uint Compact(int channel, uint value) => Compact(0, channel, value);

    /// <summary>The single table's forms, for a block that holds only one.</summary>
    public bool Holds(int channel, uint value) => Holds(0, channel, value);

    /// <summary>The single table's forms, for a block that holds only one.</summary>
    public int Remaining(int channel) => Remaining(0, channel);

    /// <summary>Checks the block can be written.</summary>
    /// <exception cref="InvalidOperationException">A channel's runs do not describe a channel.</exception>
    public void Validate()
    {
        if (RegionsX is < 1 or > MaxRegions || RegionsY is < 1 or > MaxRegions)
        {
            throw new InvalidOperationException(
                $"Colour skips divide the picture {RegionsX}x{RegionsY}; each way must be 1 to {MaxRegions}.");
        }

        if (Regions.Length != RegionCount)
        {
            throw new InvalidOperationException(
                $"Colour skips hold {Regions.Length} table(s) for a {RegionsX}x{RegionsY} division.");
        }

        foreach (uint[][] region in Regions)
        {
            ValidateRegion(region);
        }
    }

    private static void ValidateRegion(uint[][] channels)
    {
        if (channels.Length != ChannelMirrors.ChannelCount)
        {
            throw new InvalidOperationException(
                $"Colour skips cover {channels.Length} channel(s); a colour has {ChannelMirrors.ChannelCount}.");
        }

        for (int c = 0; c < channels.Length; c++)
        {
            uint[] runs = channels[c];
            if (runs.Length % 2 != 0)
            {
                throw new InvalidOperationException(
                    $"Channel {c} has {runs.Length} skip value(s), which is not a whole number of runs.");
            }

            if (runs.Length / 2 > MaxRunsPerChannel)
            {
                throw new InvalidOperationException(
                    $"Channel {c} describes {runs.Length / 2} runs, over the {MaxRunsPerChannel} limit.");
            }

            long covered = 0;
            for (int i = 0; i < runs.Length; i++)
            {
                covered += runs[i];
            }

            // Past the end of the channel the runs would describe values that cannot exist.
            if (covered > ChannelValues)
            {
                throw new InvalidOperationException(
                    $"Channel {c} describes {covered} values, more than the {ChannelValues} a channel has.");
            }

            for (int i = 1; i < runs.Length; i += 2)
            {
                if (runs[i] == 0)
                {
                    throw new InvalidOperationException(
                        $"Channel {c} has a run that skips nothing, which would say nothing.");
                }
            }
        }
    }

    public override void Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        RegionsX = (int)package.ReadVarULong();
        RegionsY = (int)package.ReadVarULong();

        if (RegionsX is < 1 or > MaxRegions || RegionsY is < 1 or > MaxRegions)
        {
            throw new InvalidOperationException(
                $"Colour skips claim a {RegionsX}x{RegionsY} division; each way must be 1 to {MaxRegions}.");
        }

        Regions = new uint[RegionCount][][];

        for (int region = 0; region < Regions.Length; region++)
        {
            Regions[region] = new uint[ChannelMirrors.ChannelCount][];

            for (int c = 0; c < ChannelMirrors.ChannelCount; c++)
            {
                ulong count = package.ReadVarULong();
                if (count > MaxRunsPerChannel)
                {
                    throw new InvalidOperationException(
                        $"Channel {c} claims {count} runs, over the {MaxRunsPerChannel} limit.");
                }

                Regions[region][c] = new uint[count * 2];
                for (int i = 0; i < Regions[region][c].Length; i++)
                {
                    Regions[region][c][i] = (uint)package.ReadVarULong();
                }
            }
        }

        Validate();
    }

    public override void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteVarULong((ulong)RegionsX);
        package.WriteVarULong((ulong)RegionsY);

        foreach (uint[][] region in Regions)
        {
            foreach (uint[] runs in region)
            {
                package.WriteVarULong((ulong)(runs.Length / 2));
                foreach (uint value in runs)
                {
                    package.WriteVarULong(value);
                }
            }
        }
    }

    public override string ToString()
    {
        char[] names = ['R', 'G', 'B', 'A'];
        var parts = new string[ChannelMirrors.ChannelCount];

        // The first region stands for the rest; the whole grid would not fit on a line.
        for (int c = 0; c < parts.Length; c++)
        {
            parts[c] = $"{names[c]}{Remaining(0, c)}";
        }

        string grid = RegionCount > 1 ? $"{RegionsX}x{RegionsY} regions, first " : string.Empty;
        return $"ColorSkips {grid}{string.Join(' ', parts)} of {ChannelValues}";
    }
}

namespace NEngineFormat.StaticIllustration.Data;

/// <summary>
/// The descriptor that says which colour channels copy another channel rather than holding a
/// value of their own.
/// </summary>
/// <remarks>
/// <para>Layout, in one integer:</para>
/// <code>
/// bits 0-3    one per channel in R,G,B,A order: this channel copies another
/// bits 4-5    which channel red copies
/// bits 6-7    which channel green copies
/// bits 8-9    which channel blue copies
/// bits 10-11  which channel alpha copies
/// </code>
/// <para>
/// Grey is the case this exists for: three channels holding one value, said once instead of
/// three times. A copied channel's source never copies anything itself, so one pass settles
/// every copy however they are arranged.
/// </para>
/// </remarks>
public static class ChannelMirrors
{
    /// <summary>How many channels a colour has.</summary>
    public const int ChannelCount = 4;

    /// <summary>Whether any channel copies another.</summary>
    public static bool Any(uint mirrors) => (mirrors & 0b1111) != 0;

    /// <summary>Whether one channel takes its value from another.</summary>
    public static bool IsMirrored(uint mirrors, int channel) => (mirrors & (1u << channel)) != 0;

    /// <summary>The channel this one copies, or itself when it copies nothing.</summary>
    public static int Source(uint mirrors, int channel) =>
        IsMirrored(mirrors, channel) ? (int)((mirrors >> (4 + (channel * 2))) & 0b11) : channel;

    /// <summary>The descriptor with one channel made to copy another.</summary>
    public static uint With(uint mirrors, int channel, int source)
    {
        if (channel == source)
        {
            return mirrors & ~(1u << channel);
        }

        return (mirrors & ~(0b11u << (4 + (channel * 2))))
            | (1u << channel)
            | ((uint)source << (4 + (channel * 2)));
    }

    /// <summary>
    /// The copies a set of colours allows: a channel copies the first earlier one it matches in
    /// every colour.
    /// </summary>
    /// <remarks>
    /// Every colour has to agree. One that parts is enough to keep both channels, because the
    /// descriptor speaks for the whole palette rather than for any one entry.
    /// </remarks>
    public static uint Detect(IReadOnlyList<uint> packedColors)
    {
        ArgumentNullException.ThrowIfNull(packedColors);

        if (packedColors.Count == 0)
        {
            return 0;
        }

        var equal = new bool[ChannelCount * ChannelCount];
        Array.Fill(equal, true);

        foreach (uint color in packedColors)
        {
            for (int first = 0; first < ChannelCount; first++)
            {
                for (int second = first + 1; second < ChannelCount; second++)
                {
                    if (ChannelOf(color, first) != ChannelOf(color, second))
                    {
                        equal[(first * ChannelCount) + second] = false;
                    }
                }
            }
        }

        uint mirrors = 0;
        for (int channel = 1; channel < ChannelCount; channel++)
        {
            for (int source = 0; source < channel; source++)
            {
                if (equal[(source * ChannelCount) + channel] && !IsMirrored(mirrors, source))
                {
                    mirrors = With(mirrors, channel, source);
                    break;
                }
            }
        }

        return mirrors;
    }

    /// <summary>One channel of a colour packed with red in the lowest byte.</summary>
    public static byte ChannelOf(uint packed, int channel) => (byte)(packed >> (channel * 8));

    /// <summary>Packs four channels back into a colour with red in the lowest byte.</summary>
    public static uint Pack(ReadOnlySpan<byte> channels) =>
        channels[0]
        | ((uint)channels[1] << 8)
        | ((uint)channels[2] << 16)
        | ((uint)channels[3] << 24);
}

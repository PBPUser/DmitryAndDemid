using NEngineFormat.Core;
using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;

namespace NEngineFormat.StaticIllustration.Blocks;

/// <summary>
/// Stands in for a <see cref="ColorProfileBlock"/> by pointing at another one, optionally
/// replacing some of its fields.
/// </summary>
/// <remarks>
/// <para>Payload layout:</para>
/// <code>
/// ProfileId            VarULong  the id this profile answers to
/// TargetProfileId      VarULong  the profile it borrows from
/// Overrides            VarULong  bitmask of which fields it replaces
/// [Flags]              VarULong  when the flags bit is set
/// [Mirrors]            VarULong  when the mirrors bit is set
/// [NonStaticColorSize] VarULong  when the widths bit is set
/// [StaticColorR]       VarULong  when the red bit is set, and so on for G, B, A
/// [SkipCounts+Skips]   VarULong  when the skips bit is set: a count per varying channel,
///                                then that many (usable, skipped) pairs
/// </code>
/// <para>
/// A picture of any size carries dozens of profiles, and they are rarely unalike: two tiles of
/// the same drawing differ by where their colours start far more often than by how wide they
/// are. A link says the difference instead of saying everything again - a profile that shares
/// its neighbour's widths and skips but starts three shades higher costs a few bytes rather
/// than a dozen.
/// </para>
/// <para>
/// Links may point at links, and the chain is followed until it reaches a profile; a chain that
/// loops is rejected rather than followed.
/// </para>
/// </remarks>
[BlockId(Id)]
public class ColorProfileLinkBlock : BlockBase
{
    /// <summary>The id this block is stored under.</summary>
    public const uint Id = 12;

    /// <summary>Replaces the target's <see cref="ColorProfileBlock.Flags"/>.</summary>
    public const uint OverrideFlagsFlag = 1u << 0;

    /// <summary>Replaces the target's <see cref="ColorProfileBlock.Mirrors"/>.</summary>
    public const uint OverrideMirrorsFlag = 1u << 1;

    /// <summary>Replaces the target's <see cref="ColorProfileBlock.NonStaticColorSize"/>.</summary>
    public const uint OverrideSizesFlag = 1u << 2;

    /// <summary>Replaces the target's red minimum.</summary>
    public const uint OverrideRedFlag = 1u << 3;

    /// <summary>Replaces the target's green minimum.</summary>
    public const uint OverrideGreenFlag = 1u << 4;

    /// <summary>Replaces the target's blue minimum.</summary>
    public const uint OverrideBlueFlag = 1u << 5;

    /// <summary>Replaces the target's alpha minimum.</summary>
    public const uint OverrideAlphaFlag = 1u << 6;

    /// <summary>Replaces the target's colour skips.</summary>
    public const uint OverrideSkipsFlag = 1u << 7;

    /// <summary>Every override bit, for rejecting ones this format does not define.</summary>
    public const uint AllOverrideFlags =
        OverrideFlagsFlag | OverrideMirrorsFlag | OverrideSizesFlag | OverrideRedFlag
        | OverrideGreenFlag | OverrideBlueFlag | OverrideAlphaFlag | OverrideSkipsFlag;

    public ColorProfileLinkBlock() => Type = Id;

    /// <summary>The id tiles name to reach this profile.</summary>
    public uint ProfileId { get; set; }

    /// <summary>The profile this one borrows from.</summary>
    public uint TargetProfileId { get; set; }

    /// <summary>Bitmask of the fields this link replaces rather than inherits.</summary>
    public uint Overrides { get; set; }

    /// <summary>Replacement for <see cref="ColorProfileBlock.Flags"/>.</summary>
    public uint Flags { get; set; }

    /// <summary>Replacement for <see cref="ColorProfileBlock.Mirrors"/>.</summary>
    public uint Mirrors { get; set; }

    /// <summary>Replacement for <see cref="ColorProfileBlock.NonStaticColorSize"/>.</summary>
    public uint NonStaticColorSize { get; set; }

    /// <summary>Replacement red minimum.</summary>
    public uint StaticColorR { get; set; }

    /// <summary>Replacement green minimum.</summary>
    public uint StaticColorG { get; set; }

    /// <summary>Replacement blue minimum.</summary>
    public uint StaticColorB { get; set; }

    /// <summary>Replacement alpha minimum.</summary>
    public uint StaticColorA { get; set; }

    /// <summary>Replacement skip counts, one per varying channel.</summary>
    public uint[] ColorSkipCounts { get; set; } = [];

    /// <summary>Replacement skip runs, in (usable, skipped) pairs.</summary>
    public uint[] ColorSkips { get; set; } = [];

    /// <summary>Whether this link replaces a given field.</summary>
    public bool HasOverride(uint overrideFlag) => (Overrides & overrideFlag) != 0;

    /// <summary>Turns an override on or off.</summary>
    public void SetOverride(uint overrideFlag, bool enabled) =>
        Overrides = enabled ? Overrides | overrideFlag : Overrides & ~overrideFlag;

    /// <summary>Builds the profile this link stands for, taking what it does not replace.</summary>
    public ColorProfileBlock Apply(ColorProfileBlock target)
    {
        ArgumentNullException.ThrowIfNull(target);

        bool skips = HasOverride(OverrideSkipsFlag);

        return new ColorProfileBlock
        {
            ProfileId = ProfileId,
            Flags = HasOverride(OverrideFlagsFlag) ? Flags : target.Flags,
            Mirrors = HasOverride(OverrideMirrorsFlag) ? Mirrors : target.Mirrors,
            NonStaticColorSize = HasOverride(OverrideSizesFlag) ? NonStaticColorSize : target.NonStaticColorSize,
            StaticColorR = HasOverride(OverrideRedFlag) ? StaticColorR : target.StaticColorR,
            StaticColorG = HasOverride(OverrideGreenFlag) ? StaticColorG : target.StaticColorG,
            StaticColorB = HasOverride(OverrideBlueFlag) ? StaticColorB : target.StaticColorB,
            StaticColorA = HasOverride(OverrideAlphaFlag) ? StaticColorA : target.StaticColorA,
            ColorSkipCounts = skips ? ColorSkipCounts : target.ColorSkipCounts,
            ColorSkips = skips ? ColorSkips : target.ColorSkips,
        };
    }

    /// <summary>Checks the link can be written.</summary>
    /// <exception cref="InvalidOperationException">The overrides do not describe a writable link.</exception>
    public void Validate()
    {
        if ((Overrides & ~AllOverrideFlags) != 0)
        {
            throw new InvalidOperationException(
                $"Overrides 0x{Overrides:X} sets bits this format does not define.");
        }

        // A profile standing in for itself would resolve to itself for ever.
        if (ProfileId == TargetProfileId)
        {
            throw new InvalidOperationException($"Profile {ProfileId} cannot borrow from itself.");
        }

        if (!HasOverride(OverrideSkipsFlag) && (ColorSkipCounts.Length > 0 || ColorSkips.Length > 0))
        {
            throw new InvalidOperationException(
                "Skip runs are set but the skips bit is not; they would not be written.");
        }

        long declared = 0;
        foreach (uint count in ColorSkipCounts)
        {
            declared += count;
        }

        if (declared * 2 != ColorSkips.Length)
        {
            throw new InvalidOperationException(
                $"The link declares {declared} skip run(s) but holds {ColorSkips.Length / 2}.");
        }
    }

    public override void Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        ProfileId = (uint)package.ReadVarULong();
        TargetProfileId = (uint)package.ReadVarULong();
        Overrides = (uint)package.ReadVarULong();

        Flags = HasOverride(OverrideFlagsFlag) ? (uint)package.ReadVarULong() : 0;
        Mirrors = HasOverride(OverrideMirrorsFlag) ? (uint)package.ReadVarULong() : 0;
        NonStaticColorSize = HasOverride(OverrideSizesFlag) ? (uint)package.ReadVarULong() : 0;
        StaticColorR = HasOverride(OverrideRedFlag) ? (uint)package.ReadVarULong() : 0;
        StaticColorG = HasOverride(OverrideGreenFlag) ? (uint)package.ReadVarULong() : 0;
        StaticColorB = HasOverride(OverrideBlueFlag) ? (uint)package.ReadVarULong() : 0;
        StaticColorA = HasOverride(OverrideAlphaFlag) ? (uint)package.ReadVarULong() : 0;

        if (HasOverride(OverrideSkipsFlag))
        {
            ulong channels = package.ReadVarULong();
            if (channels > ChannelCount)
            {
                throw new InvalidOperationException(
                    $"A profile link declares skips for {channels} channels; a colour has {ChannelCount}.");
            }

            ColorSkipCounts = new uint[channels];
            var runs = new List<uint>();

            for (int c = 0; c < ColorSkipCounts.Length; c++)
            {
                uint count = (uint)package.ReadVarULong();
                if (count > ColorProfileBlock.MaxSkipRuns)
                {
                    throw new InvalidOperationException(
                        $"Channel {c} declares {count} skip runs, over the {ColorProfileBlock.MaxSkipRuns} limit.");
                }

                ColorSkipCounts[c] = count;
                for (int i = 0; i < count * 2; i++)
                {
                    runs.Add((uint)package.ReadVarULong());
                }
            }

            ColorSkips = [.. runs];
        }
        else
        {
            ColorSkipCounts = [];
            ColorSkips = [];
        }

        Validate();
    }

    public override void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteVarULong(ProfileId);
        package.WriteVarULong(TargetProfileId);
        package.WriteVarULong(Overrides);

        WriteIf(OverrideFlagsFlag, Flags);
        WriteIf(OverrideMirrorsFlag, Mirrors);
        WriteIf(OverrideSizesFlag, NonStaticColorSize);
        WriteIf(OverrideRedFlag, StaticColorR);
        WriteIf(OverrideGreenFlag, StaticColorG);
        WriteIf(OverrideBlueFlag, StaticColorB);
        WriteIf(OverrideAlphaFlag, StaticColorA);

        if (HasOverride(OverrideSkipsFlag))
        {
            package.WriteVarULong((ulong)ColorSkipCounts.Length);

            int at = 0;
            foreach (uint count in ColorSkipCounts)
            {
                package.WriteVarULong(count);
                for (int i = 0; i < count * 2; i++)
                {
                    package.WriteVarULong(ColorSkips[at++]);
                }
            }
        }

        void WriteIf(uint flag, uint value)
        {
            if (HasOverride(flag))
            {
                package.WriteVarULong(value);
            }
        }
    }

    public override string ToString() =>
        $"ColorProfileLink #{ProfileId} -> #{TargetProfileId}, overrides 0x{Overrides:X}";

    /// <summary>Channels a colour has, which bounds the skip counts a link may carry.</summary>
    private const int ChannelCount = 4;
}

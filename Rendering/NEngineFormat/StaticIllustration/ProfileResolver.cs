using NEngineFormat.Core;
using NEngineFormat.StaticIllustration.Blocks;

namespace NEngineFormat.StaticIllustration;

/// <summary>
/// Turns the profile blocks of a file into the profiles a decoder reads by, with every link
/// applied to what it borrows from.
/// </summary>
/// <remarks>
/// A link may point at another link, so the chain is walked to its end and the links are then
/// applied from that end back - the nearest one has the last word, exactly as a tile link
/// behaves. A chain that loops is rejected; it could not be resolved at all.
/// </remarks>
public static class ProfileResolver
{
    /// <summary>Every profile by id, links resolved.</summary>
    /// <exception cref="InvalidOperationException">
    /// A link points at a profile that is not in the file, or a chain loops.
    /// </exception>
    public static IReadOnlyDictionary<uint, ColorProfileBlock> Resolve(IReadOnlyList<BlockBase> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var profiles = new Dictionary<uint, ColorProfileBlock>();
        var links = new Dictionary<uint, ColorProfileLinkBlock>();

        foreach (BlockBase block in blocks)
        {
            if (block is ColorProfileBlock profile)
            {
                profiles[profile.ProfileId] = profile;
            }
            else if (block is ColorProfileLinkBlock link)
            {
                links[link.ProfileId] = link;
            }
        }

        foreach (uint id in links.Keys)
        {
            profiles[id] = Follow(id, profiles, links);
        }

        return profiles;
    }

    /// <summary>Walks one link down to a profile and applies the chain back up.</summary>
    private static ColorProfileBlock Follow(
        uint id,
        Dictionary<uint, ColorProfileBlock> profiles,
        Dictionary<uint, ColorProfileLinkBlock> links)
    {
        var chain = new List<ColorProfileLinkBlock>();
        var seen = new HashSet<uint>();
        uint at = id;

        while (links.TryGetValue(at, out ColorProfileLinkBlock? link))
        {
            if (!seen.Add(at))
            {
                throw new InvalidOperationException($"Profile {id} follows a loop back to profile {at}.");
            }

            chain.Add(link);
            at = link.TargetProfileId;
        }

        if (!profiles.TryGetValue(at, out ColorProfileBlock? target))
        {
            throw new InvalidOperationException(
                $"Profile {id} borrows from profile {at}, which is not in the file.");
        }

        // Nearest the profile first, so the link that was asked about has the last word.
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            target = chain[i].Apply(target);
        }

        return target;
    }
}

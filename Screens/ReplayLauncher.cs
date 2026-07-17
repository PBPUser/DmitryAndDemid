using System.Text.Json;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

/// <summary>
/// Shared machinery for turning a saved .rpy file into a running GameplayScreen driven by a ReplayController.
/// Used by the replay viewer (foreground playback) and the title-screen demo (attract mode).
/// </summary>
public static class ReplayLauncher
{
    /// <summary>Saved replays under the "Replays" folder next to the executable, sorted.</summary>
    public static string[] FindReplays()
    {
        try
        {
            if (!Directory.Exists("Replays"))
                return [];
            string[] files = Directory.GetFiles("Replays", "*.rpy");
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Builds a GameplayScreen that plays back the replay, or null if it can't be loaded.</summary>
    public static GameplayScreen? Build(string path, bool demo)
    {
        Replay replay;
        try { replay = Replay.Load(path); }
        catch { return null; }

        ProtogonistData? data = LoadPerson(replay.Information.Person);
        if (data == null)
            return null;

        // Filtered to .sid via the shared helper — the demo/replay used to enumerate "*" and take [0], loading a
        // stray non-stage file and crashing exactly like the main story did (see CampaignStagePaths).
        string[] spellCards = FileStageInfo.CampaignStagePaths();
        if (spellCards.Length == 0)
            return null;

        var bitPackage = BitPackage.OpenStreamReadPackage(spellCards[0]);
        GameplayScreen screen = new(data, replay.Information.Difficulty,
            [FileStageInfo.Load(ref bitPackage)], 0, false, new ReplayController(replay, 0))
        {
            IsDemo = demo,
        };
        bitPackage.Dispose();
        return screen;
    }

    /// <summary>The replay header stores the character's ID; find the matching PlayablePersons entry.</summary>
    public static ProtogonistData? LoadPerson(string id)
    {
        foreach (string file in Assets.Files("Assets/Data/PlayablePersons/", "*.json"))
        {
            try
            {
                ProtogonistData? data = JsonSerializer.Deserialize<ProtogonistData>(File.ReadAllText(file));
                if (data != null && data.ID == id)
                    return data;
            }
            catch
            {
                // Skip an unreadable or malformed person file rather than fail the whole launch.
            }
        }
        return null;
    }
}

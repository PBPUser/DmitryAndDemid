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

    /// <summary>
    /// Builds a GameplayScreen that plays back the replay, or null if it can't be loaded. <paramref name="startStage"/>
    /// is the campaign stage index to begin playback from (chosen in the replay viewer's level list).
    /// </summary>
    public static GameplayScreen? Build(string path, bool demo, int startStage = 0)
    {
        Replay replay;
        try { replay = Replay.Load(path); }
        catch { return null; }

        ProtogonistData? data = LoadPerson(replay.Information.Person);
        if (data == null)
            return null;

        // The stage list of the mode the replay was recorded in: an Extra replay plays on the Extra stage(s),
        // everything else on the campaign. Filtered to .sid via the shared helpers — the demo/replay used to
        // enumerate "*" and take [0], loading a stray non-stage file and crashing exactly like the main story
        // did (see CampaignStagePaths).
        bool extra = replay.Information.IsExtra;
        string[] spellCards = extra ? FileStageInfo.ExtraStagePaths() : FileStageInfo.CampaignStagePaths();
        if (spellCards.Length == 0)
            return null;
        GameType mode = extra ? GameType.Extra : GameType.Default;

        // A new-format replay records which stages it covers, so load the whole campaign and let playback start
        // at the picked stage and advance normally. Old replays have no per-stage index — their buffer is a
        // single stage's inputs from offset 0 — so fall back to loading just the first stage, as before.
        bool hasStageInfo = replay.Information.ReplayStageInfo is { Length: > 0 };
        FileStageInfo[] stages = hasStageInfo
            ? spellCards.Select(FileStageInfo.LoadFromFile).ToArray()
            : [FileStageInfo.LoadFromFile(spellCards[0])];
        int start = hasStageInfo ? Math.Clamp(startStage, 0, stages.Length - 1) : 0;

        GameplayScreen screen = new(data, replay.Information.Difficulty,
            stages, 0, false, new ReplayController(replay, start), mode, start)
        {
            IsDemo = demo,
        };
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

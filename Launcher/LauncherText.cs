using System.Text.Json;

namespace DmitryAndDemid.Launcher;

/// <summary>
/// The launcher's strings. Two tables, both read once from the game folder the launcher sits in:
/// <c>Assets/Data/translation.json</c>, the game's own wording (shared with the in-game settings, so the two
/// dialogs cannot name a setting two ways), and <c>Assets/Data/translation.launcher.en.json</c>, an English
/// table that belongs to the configurator alone — nothing in the game reads it, and no English leaks into
/// the game's own strings. The game's Helper cannot be linked here (its static state reaches the GPU), so
/// this is the small GPU-free reading of the same format.
///
/// Which table speaks is one coin flip per run, and the <c>;</c> variants inside the chosen entry are rolled
/// per key — so a launch is all English or all Russian, worded a little differently each time, but never two
/// ways within one launch: every roll is cached, or a frame title and the row explaining it could disagree.
/// A key the English table lacks falls back to the game's, and a key in neither comes back as itself so
/// nothing is ever blank. GTK renders the Cyrillic as it is — no transliteration, that is a font trick of
/// the game's.
/// </summary>
public static class LauncherText
{
    private static readonly Dictionary<string, string> Game = Load("translation.json");
    private static readonly Dictionary<string, string> English = Load("translation.launcher.en.json");

    /// <summary>The coin flip, once per run. English only when that table actually loaded.</summary>
    private static readonly bool SpeaksEnglish = English.Count > 0 && Random.Shared.Next(2) == 0;

    /// <summary>What each key settled on this run; one roll per key, however many times it is asked for.</summary>
    private static readonly Dictionary<string, string> Rolled = [];

    private static Dictionary<string, string> Load(string name)
    {
        foreach (string dir in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            string path = Path.Combine(dir, "Assets", "Data", name);
            if (!File.Exists(path))
                continue;
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                break;
            }
        }
        return new Dictionary<string, string>();
    }

    /// <summary>This run's wording for <paramref name="key"/>, or the key when neither table has it.</summary>
    public static string T(string key)
    {
        if (Rolled.TryGetValue(key, out string? settled))
            return settled;

        settled = Roll(key);
        Rolled[key] = settled;
        return settled;
    }

    private static string Roll(string key)
    {
        string? entry = null;
        if (SpeaksEnglish)
            English.TryGetValue(key, out entry);
        if (string.IsNullOrEmpty(entry))
            Game.TryGetValue(key, out entry);
        if (string.IsNullOrEmpty(entry))
            return key;

        string[] variants = entry.Split(';', StringSplitOptions.RemoveEmptyEntries);
        return variants.Length == 0 ? key : variants[Random.Shared.Next(variants.Length)];
    }

    /// <summary>A row label ("Апскейлер: %s") as a frame title: the value placeholder and its separator dropped.</summary>
    public static string Title(string key)
    {
        string s = T(key).Replace("%s", "").TrimEnd();
        return s.TrimEnd(':').TrimEnd();
    }
}

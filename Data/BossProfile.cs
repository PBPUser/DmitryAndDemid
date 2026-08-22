using System.Text.Json;
using System.Text.Json.Serialization;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data;

/// <summary>
/// A boss's profile card, loaded from <c>Assets/Data/Profiles/&lt;boss&gt;.json</c>. Referenced by the dialog's
/// ShowBossName tag: when a line is flagged, the game shows the boss's <see cref="Name"/>, the optional art
/// <c>profile-&lt;boss&gt;.png</c>, and a fork tinted with <see cref="Color"/>. Everything is opt-in by file
/// existence — a boss with no profile json simply shows nothing extra, so no assets are required to ship.
/// </summary>
public class BossProfile
{
    /// <summary>Display name shown beside the profile (the friendly, human name — Cyrillic is fine).</summary>
    [JsonInclude] public string Name { get; set; } = "";

    /// <summary>The boss's accent colour as <c>#RRGGBB</c> (or <c>#RRGGBBAA</c>); tints the rotating fork.</summary>
    [JsonInclude] public string Color { get; set; } = "#FFFFFF";

    private static Dictionary<string, BossProfile>? cache;

    /// <summary>All profiles under Assets/Data/Profiles, keyed by file name without extension. Scanned once.</summary>
    private static Dictionary<string, BossProfile> All()
    {
        if (cache != null)
            return cache;
        cache = new Dictionary<string, BossProfile>();
        try
        {
            if (Assets.DirectoryExists("Assets/Data/Profiles"))
                foreach (string file in Assets.Files("Assets/Data/Profiles", "*.json"))
                {
                    try
                    {
                        BossProfile? p = JsonSerializer.Deserialize<BossProfile>(File.ReadAllText(file));
                        if (p != null)
                            cache[Path.GetFileNameWithoutExtension(file)] = p;
                    }
                    catch { /* skip a malformed profile rather than crash the dialog */ }
                }
        }
        catch { /* no profiles dir -> no profiles, which is fine */ }
        return cache;
    }

    /// <summary>The profile for a boss key (e.g. "nikitab"), or null if none exists.</summary>
    public static BossProfile? Get(string bossKey) =>
        !string.IsNullOrEmpty(bossKey) && All().TryGetValue(bossKey, out BossProfile? p) ? p : null;

    /// <summary>Parses <see cref="Color"/> (#RRGGBB / #RRGGBBAA) into an Rgba; white on any parse failure.</summary>
    public Rgba AccentColor()
    {
        string s = (Color ?? "").Trim().TrimStart('#');
        try
        {
            if (s.Length == 6 || s.Length == 8)
            {
                byte g = Convert.ToByte(s.Substring(2, 2), 16);
                byte r = Convert.ToByte(s.Substring(0, 2), 16);
                byte b = Convert.ToByte(s.Substring(4, 2), 16);
                byte a = s.Length == 8 ? Convert.ToByte(s.Substring(6, 2), 16) : (byte)255;
                return new Rgba(r, g, b, a);
            }
        }
        catch { /* fall through to white */ }
        return Rgba.White;
    }

    /// <summary>
    /// Derives the profile/art key from a dialog line's character-art filename, e.g. "nikitab_dialog_art.png"
    /// -> "nikitab". Strips the extension and the common art suffixes so the key matches profile-&lt;key&gt;.png
    /// and Profiles/&lt;key&gt;.json.
    /// </summary>
    public static string KeyFromCharacterTexture(string characterTexture)
    {
        string s = characterTexture ?? "";
        int dot = s.LastIndexOf('.');
        if (dot >= 0)
            s = s.Substring(0, dot);
        foreach (string suffix in new[] { "_dialog_arts", "_dialog_art", "_boss_art", "_art" })
            if (s.EndsWith(suffix))
            {
                s = s.Substring(0, s.Length - suffix.Length);
                break;
            }
        return s;
    }
}

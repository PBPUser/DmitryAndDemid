using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Text.Json;
using System.Text.Json.Serialization;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data;

public class MusicInfo
{
    public static List<MusicInfo?> MusicInformations = Directory.GetFiles("Assets/Music/Descriptions")
        .Select(x => JsonSerializer.Deserialize<MusicInfo>(System.IO.File.ReadAllText(x))).ToList();
    public static string[] MusicNames = MusicInformations.Select(x => x == null ? "(HoJlb)" : Helper.Transliterate( x.Title)).ToArray();
    
    [JsonInclude] public int Number = -1;
    [JsonInclude] public string Title = "-1. Unknown";
    [JsonInclude] public string Description = "Unknown";
    [JsonInclude] public string File = "";
    [JsonInclude] public string InGameName = "";
}
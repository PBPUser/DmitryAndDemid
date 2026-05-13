using System.Text.Json;
using System.Text.Json.Serialization;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data;

public class MusicInfo
{
    public static List<MusicInfo?> MusicInformations = Directory.GetFiles("Assets/Music/Descriptions")
        .Select(x => JsonSerializer.Deserialize<MusicInfo>(System.IO.File.ReadAllText(x))).ToList();
    public static string[] MusicNames = MusicInformations.Select(x => x == null ? "(HoJlb)" : Helper.Transliterate( x.Title)).ToArray();
    
    [JsonInclude] public int Number = 0;
    [JsonInclude] public string Title = "";
    [JsonInclude] public string Description = "";
    [JsonInclude] public string File = "";
}
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class MusicInfo
{
    [JsonInclude] public int Number = 0;
    [JsonInclude] public string Title = "";
    [JsonInclude] public string Description = "";
    [JsonInclude] public string File = "";
}
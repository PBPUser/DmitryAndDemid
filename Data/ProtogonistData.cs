using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class ProtogonistData
{
    [JsonInclude]
    public string ArtName = "";

    [JsonInclude]
    public string Description = "Sample text";

    [JsonInclude]
    public int Speed = 2;

    [JsonInclude]
    public string WeaponScriptName = "";

    [JsonInclude]
    public string BombScriptName = "";

    [JsonInclude]
    public string Sprite = "";
}

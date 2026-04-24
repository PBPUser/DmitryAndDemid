using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class BossAppearInfo
{
    [JsonInclude] public string ID = "";
    [JsonInclude] public string DialogElementID = "";
}
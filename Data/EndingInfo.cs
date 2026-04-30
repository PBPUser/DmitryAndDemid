using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class EndingInfo
{
    [JsonInclude] public string ID;
    [JsonInclude] public bool IsBad = false;
    [JsonInclude] public List<EndingElement> Elements;
}
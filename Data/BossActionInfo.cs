using System.Numerics;
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class BossActionInfo
{
    [JsonInclude] public string ID = "";
    [JsonInclude] public Vector2? StartingPosition = null;
    [JsonInclude] public string BossAction = "";
}
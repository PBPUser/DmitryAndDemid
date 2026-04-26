using System.Numerics;
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class BossActionInfo
{
    [JsonInclude] public string ID = "";
    [JsonInclude] public Vector2? StartingPosition = null;
    [JsonInclude] public string MoveAction = "";
    [JsonInclude] public string ShootAction = "";
    [JsonInclude] public string StartAction = "";
    [JsonInclude] public float Health = 0f;
}
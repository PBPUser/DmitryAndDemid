using System.Numerics;
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class BossSpawnInfo
{
    [JsonInclude] public string ID = "";
    [JsonInclude] public string DialogElementAppearID = "";
    [JsonInclude] public Vector2 RenderSize = new Vector2(32, 32);
    [JsonInclude] public Vector2 CollisionSize = new Vector2(32, 32);
    [JsonInclude] public string BossSprite = "";
}
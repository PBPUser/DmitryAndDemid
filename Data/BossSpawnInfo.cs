using System.Numerics;
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class BossSpawnInfo
{
    [JsonInclude] public string ID = "";
    [JsonInclude] public string DialogElementAppearID = "";
    [JsonInclude] public Vector2 RenderSize = new Vector2(64, 64);
    [JsonInclude] public Vector2 CollisionSize = new Vector2(32, 32);
    [JsonInclude] public Vector2 Position = new Vector2(192, 64);
    [JsonInclude] public string BossSpriteTexture = "";
}
using System.Numerics;
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class BulletSpawnInfoModifier : ChapterElement
{
    /// <summary>
    /// Bullet Spawn Info to Modify
    /// </summary>
    [JsonInclude] public string ID = "";
    [JsonInclude] public Vector2? PositionOverride = null;
}
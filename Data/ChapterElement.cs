using System.Numerics;
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class ChapterElement
{
    [JsonInclude] public Vector2 Position = Vector2.Zero;
    [JsonInclude] public int SpawnTick = 0;
    [JsonInclude] public bool Stacked = false;
    [JsonInclude] public int StackLength = 0;
    [JsonInclude] public int StackDelay = 2;
    [JsonInclude] public Vector2 StackPositionOffset = new Vector2(0, 0);
    [JsonInclude] public float StackRotationOffset = 0f;
}
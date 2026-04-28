using System.Numerics;
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class ChapterElement
{
    [JsonInclude] public Vector2 Position = Vector2.Zero;
    [JsonInclude] public float Rotation = 0;
    [JsonInclude] public int SpawnTick = 0;
    [JsonInclude] public bool Stacked = false;
    [JsonInclude] public int StackLength = 0;
    [JsonInclude] public int StackDelay = 2;
    [JsonInclude] public Vector2 StackPositionOffset = new Vector2(0, 0);
    [JsonInclude] public float StackRotationOffset = 0f;
    [JsonInclude] public Vector2 PositionTarget = Vector2.Zero;
    [JsonInclude] public int DislocationOnTargetDuration = 0;
    [JsonInclude] public Vector2 FinalPosition = Vector2.Zero;
}
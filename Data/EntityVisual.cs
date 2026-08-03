using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data;

public class EntityVisual
{
    public static Dictionary<string, EntityVisual> Visuals = new();

    static EntityVisual()
    {
        foreach (var file in Assets.Files("Assets/Data/EntityVisuals"))
            Visuals[Path.GetFileNameWithoutExtension(file)] = JsonSerializer.Deserialize<EntityVisual>(File.ReadAllText(file), new JsonSerializerOptions()
            {
                IncludeFields = true
            })!;
    }

    [JsonInclude] public string Texture;
    [JsonInclude] public Vector2 SourcePosition;
    [JsonInclude] public Vector2 RenderSize;
    [JsonInclude] public float Collision;
    [JsonInclude] public int DeathCircleColor;
    [JsonInclude] public int DeathParticleGlowColor;
    /// <summary>Frames laid out left-to-right in the texture starting at <see cref="SourcePosition"/>, each
    /// <see cref="RenderSize"/> wide. 1 (the default) means no animation — the existing single-frame behavior.</summary>
    [JsonInclude] public int FrameCount = 1;
    /// <summary>Ticks each frame is held before advancing. Only meaningful when <see cref="FrameCount"/> &gt; 1.</summary>
    [JsonInclude] public int FrameTicks = 8;
    /// <summary>Frame index (into the same strip as <see cref="FrameCount"/>) to show instead of the idle loop
    /// while the object is actually translating left/right. -1 (the default) disables lean entirely.</summary>
    [JsonInclude] public int LeanLeftFrame = -1;
    /// <summary>Frame index to show while translating right. -1 disables lean entirely.</summary>
    [JsonInclude] public int LeanRightFrame = -1;
    /// <summary>Frame indices (up to 4, into the same strip) stepped through in order while the object is
    /// stationary — a livelier idle than the plain breathing loop. Empty (the default) keeps the plain
    /// sequential 0..<see cref="FrameCount"/>-1 breathing loop instead.</summary>
    [JsonInclude] public int[] DanceFrames = [];
    /// <summary>Ticks each dance step is held before advancing. Only meaningful when <see cref="DanceFrames"/>
    /// is non-empty.</summary>
    [JsonInclude] public int DanceTicks = 10;
}

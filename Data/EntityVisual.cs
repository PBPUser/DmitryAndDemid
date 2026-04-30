using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class EntityVisual
{
    public static Dictionary<string, EntityVisual> Visuals = new();

    static EntityVisual()
    {
        foreach (var file in Directory.GetFiles("Assets/Data/EntityVisuals"))
            Visuals[Path.GetFileNameWithoutExtension(file)] = JsonSerializer.Deserialize<EntityVisual>(File.ReadAllText(file), new JsonSerializerOptions()
            {
                IncludeFields = true
            })!;
    }

    [JsonInclude] public string Texture;
    [JsonInclude] public Vector2 SourcePosition;
    [JsonInclude] public Vector2 RenderSize;
    [JsonInclude] public Vector2 Collision;
}

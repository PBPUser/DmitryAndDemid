using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Data;

public class BulletVisual
{
    public static Dictionary<string, BulletVisual> Constants = new Dictionary<string, BulletVisual>();

    private static RenderTexture2D Rectangle384x448;
    
    
    static BulletVisual()
    {
        Rectangle384x448 = LoadRenderTexture(384, 448);
        foreach (var file in Directory.GetFiles("Assets/Data/BulletVisuals", "*.json"))
            Constants[Path.GetFileNameWithoutExtension(file)] = JsonSerializer.Deserialize<BulletVisual>(File.ReadAllText(file), new JsonSerializerOptions {IncludeFields= true});
    }

    public static void FillRCPrerender()
    {
         BeginTextureMode(Rectangle384x448);
         DrawRectangle(0,0,384,448,Color.White);
         EndTextureMode();
    }

    public BulletVisual()
    {
        Bullets = LoadRenderTexture(8192, 8192);
    }
    
    [JsonInclude] public string Texture = "";
    [JsonInclude] public BulletVisualRenderType RenderType = BulletVisualRenderType.FromSprite;
    [JsonInclude] public Vector2 Collision;
    [JsonInclude] public Vector2 RenderSize;
    [JsonInclude] public Vector2 SourcePosition = new Vector2(0, 0);
    [JsonInclude] public Vector2? SourceSize = null;
    [JsonInclude] public string Effect = "";

    [JsonIgnore] int CurrentX = 0;
    [JsonIgnore] int CurrentY = 0;
    [JsonIgnore] Dictionary<Vector3, Vector2> Positions = new();
    [JsonIgnore] RenderTexture2D Bullets = new RenderTexture2D();
    [JsonIgnore] Vector3 PreviousColor = -Vector3.One;
    
    public Texture2D GetTexture(Vector3 color)
    {
        if (RenderType == BulletVisualRenderType.FromSprite)
            return Runtime.CurrentRuntime.Textures[Texture];
        if (Positions.ContainsKey(color))
            return Bullets.Texture;
        BeginTextureMode(Bullets);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders[Texture]);
        DrawTexturePro(Rectangle384x448.Texture, new Rectangle(0,0,SourceSize.Value.X, SourceSize.Value.Y),
            new Rectangle(CurrentX,CurrentY,SourceSize.Value.X, SourceSize.Value.Y), Vector2.Zero, 0, Color.White);
        EndShaderMode();
        EndTextureMode();
        CurrentX += (int)SourceSize.Value.X;
        CurrentY += (int)SourceSize.Value.Y;
        return Bullets.Texture;
    }

    public Vector2 GetSourcePosition(Vector3 color)
    {
        if(RenderType == BulletVisualRenderType.FromSprite)
            return SourcePosition;
        if (Positions.ContainsKey(color))
            return Positions[color];
        return Vector2.Zero;
    }
    
    
}

public enum BulletVisualRenderType
{
    FromShader = 0,
    FromSprite = 1
}
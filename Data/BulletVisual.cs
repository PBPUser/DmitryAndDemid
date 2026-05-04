using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Data;

public class BulletVisual
{
    public static Dictionary<string, BulletVisual> Constants = new Dictionary<string, BulletVisual>();

    public static RenderTexture2D Rectangle384x448;
    
    
    static BulletVisual()
    {
        Rectangle384x448 = LoadRenderTexture(384, 448);
        foreach (var file in Directory.GetFiles("Assets/Data/BulletVisuals", "*.json"))
            Constants[Path.GetFileNameWithoutExtension(file)] = JsonSerializer.Deserialize<BulletVisual>(File.ReadAllText(file), new JsonSerializerOptions {IncludeFields= true});
    }

    public bool LastShaderInvalid = false;

    public static void FillRCPrerender()
    {
         BeginTextureMode(Rectangle384x448);
         DrawRectangle(0,0,384,448,Color.White);
         EndTextureMode();
    }

    public BulletVisual()
    {
        Bullets = LoadRenderTexture(8192, 8192);
        if (RenderType == BulletVisualRenderType.FromShader)
        {
        }
    }
    
    [JsonInclude] public string Texture = "";
    [JsonInclude] public BulletVisualRenderType RenderType = BulletVisualRenderType.FromSprite;
    [JsonInclude] public Vector2 Collision;
    [JsonInclude] public Vector2 RenderSize;
    [JsonInclude] public Vector2 SourcePosition = new Vector2(0, 0);
    [JsonInclude] public Vector2? SourceSize = null;
    [JsonInclude] public string Effect = "";
    
    [JsonIgnore] public string ShaderText = "";
    [JsonIgnore] int CurrentX = 0;
    [JsonIgnore] int CurrentY = 0;
    [JsonIgnore] Dictionary<Vector3, Vector2> Positions = new();
    [JsonIgnore] RenderTexture2D Bullets = new RenderTexture2D();
    [JsonIgnore] Vector3 PreviousColor = -Vector3.One;
    [JsonIgnore] private bool CustomShaderUsed = false;
    [JsonIgnore] private Shader CustomShader;
    
    public Texture2D GetTexture(Vector3 color)
    {
        if (RenderType == BulletVisualRenderType.FromSprite)
            return Runtime.CurrentRuntime.Textures[Texture];
        if(ShaderText == "")
            ShaderText = File.ReadAllText($"Assets/Shaders/{Texture}.fs");
        if (Positions.ContainsKey(color))
            return Bullets.Texture;
        BeginTextureMode(Bullets);
        var shader = CustomShaderUsed ? CustomShader : Runtime.CurrentRuntime.Shaders[Texture];
        
        SetShaderValue(shader, GetShaderLocation(shader,"color"),
            color, ShaderUniformDataType.Vec3);
        BeginShaderMode(shader);
        DrawTexturePro(Rectangle384x448.Texture, 
            new (0,0,384,448),
            new (CurrentX,CurrentY,SourceSize.Value),
            Vector2.Zero, 0, Color.White);
        //DrawRectangle(0, 0, 512, 512, Color.Green);
        EndShaderMode();
        EndTextureMode();
        Positions.Add(color, new Vector2(CurrentX, 8192-CurrentY));
        CurrentX += (int)SourceSize.Value.X;
        if (CurrentX + SourceSize.Value.X > 8192-SourceSize.Value.X)
        {
            CurrentX = 0;
            CurrentY += (int)SourceSize.Value.Y;
        }

        if (CurrentX > 8192 - SourceSize.Value.Y)
            CurrentY = 0;
        //CurrentY += (int)SourceSize.Value.Y;
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

    public Vector2 GetSourceSize()
    {
        if (RenderType == BulletVisualRenderType.FromSprite)
            return SourceSize.Value;
        return SourceSize.Value * new Vector2(1, -1);
    }
    
    
#if DEBUG
    public static string BaseVS = File.ReadAllText("Assets/Shaders/base.vs");
    
    public void ReloadShader()
    {
        LastShaderInvalid = true;
        var shader = LoadShaderFromMemory(
            BaseVS,
            ShaderText
        );
        if (!IsShaderValid(shader))
        {
            UnloadShader(shader);
            return;
        }
        LastShaderInvalid = false;
        if(CustomShaderUsed)
            UnloadShader(CustomShader);
        CustomShader = shader;
        BeginTextureMode(Bullets);
        ClearBackground(Color.Black with {A = 0});
        foreach (var color in this.Positions)
        {
            SetShaderValue(CustomShader, GetShaderLocation(CustomShader,"color"),
                color.Key, ShaderUniformDataType.Vec3);
            BeginShaderMode(CustomShader);
            DrawTexturePro(Rectangle384x448.Texture, 
                new (0,0,384,448),
                new (color.Value.X, 8192-color.Value.Y,SourceSize.Value),
                Vector2.Zero, 0, Color.White);
            EndShaderMode();
        }
        EndTextureMode();
        CustomShaderUsed = true;
    }    
#endif
}

public enum BulletVisualRenderType
{
    FromShader = 0,
    FromSprite = 1
}
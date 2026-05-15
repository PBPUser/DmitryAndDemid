using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Data;

public class BulletVisual
{
//public static Dictionary<string, BulletVisual> Constants = new Dictionary<string, BulletVisual>();
    public static RenderTexture2D Rectangle384x448;
    
    public bool LastShaderInvalid = false;
    
    [JsonInclude] public string Texture = "";
    [JsonInclude] public BulletVisualRenderType RenderType = BulletVisualRenderType.FromSprite;
    [JsonInclude] public Vector2 Collision;
    [JsonInclude] public Vector2 RenderSize;
    [JsonInclude] public Vector2 SourcePosition = new Vector2(0, 0);
    [JsonInclude] public Vector2? SourceSize = null;

    [JsonInclude]
    public string Effect
    {
        get => _effect;
        set
        {
            _effect = value;
            
        }
    }

    private string _effect;
    
    [JsonIgnore] public string ShaderText = "";
    [JsonIgnore] public int CurrentX = 0;
    [JsonIgnore] public int CurrentY = 0;
    [JsonIgnore] private Dictionary<int, Vector2> Positions = new();
    [JsonIgnore] private RenderTexture2D Bullets;
    [JsonIgnore] private Vector3 PreviousColor = -Vector3.One;
    [JsonIgnore] private bool CustomShaderUsed;
    [JsonIgnore] private Shader CustomShader;
    
    static BulletVisual()
    {
        Rectangle384x448 = LoadRenderTexture(384, 448);
        //foreach (var file in Directory.GetFiles("Assets/Data/BulletVisuals", "*.json"))
            //Constants[Path.GetFileNameWithoutExtension(file)] = JsonSerializer.Deserialize<BulletVisual>(File.ReadAllText(file), new JsonSerializerOptions {IncludeFields= true});
    }

    public BulletVisual()
    {
        Bullets = LoadRenderTexture(8192, 8192);
        if (RenderType == BulletVisualRenderType.FromShader)
        {
        }
    }

    public static void FillRCPrerender()
    {
         BeginTextureMode(Rectangle384x448);
         DrawRectangle(0,0,384,448,Color.White);
         EndTextureMode();
    }
    
    public Texture2D GetTexture(Vector3 color)
    {
        if (RenderType == BulletVisualRenderType.FromSprite)
            return Runtime.CurrentRuntime.Textures[Texture];
        if(ShaderText == "")
            ShaderText = File.ReadAllText($"Assets/Shaders/{Texture}.fs");
        int iColor = Helper.Vector3ColorToInt(color);
        if (Positions.ContainsKey(iColor))
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
        EndShaderMode();
        EndTextureMode();
        Positions.Add(iColor, new Vector2(CurrentX, 8192+CurrentY));
        CurrentX += (int)SourceSize.Value.X;
        if (CurrentX + SourceSize.Value.X > 8192-SourceSize.Value.X)
        {
            CurrentX = 0;
            CurrentY += (int)SourceSize.Value.Y;
        }
        if (CurrentY > 8192 - SourceSize.Value.Y)
            CurrentY = 0;
        return Bullets.Texture;
    }

    public Vector2 GetSourcePosition(Vector3 color)
    {
        if(RenderType == BulletVisualRenderType.FromSprite)
            return SourcePosition;
        int iColor = Helper.Vector3ColorToInt(color);
        if (Positions.ContainsKey(iColor))
            return Positions[iColor];
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
                Helper.ColorIntToVector3(color.Key), ShaderUniformDataType.Vec3);
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
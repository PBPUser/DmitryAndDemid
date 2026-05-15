using System.Collections.Frozen;
using System.Numerics;
using System.Text.Json.Serialization;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Data;

public class BulletRenderingInfo
{
    private static Rectangle FRC384448 = new Rectangle(0, 0, 384, 448); 
    private static Texture2D T384448 = Runtime.CurrentRuntime.Textures["384x448"];
    
    [JsonInclude] public BulletVisualRenderType RenderType = BulletVisualRenderType.FromSprite;
    [JsonInclude] public string Texture = "";
    [JsonInclude] public Vector2 SourceSize = Vector2.Zero;
    [JsonInclude] public Vector2 SpritePosition = Vector2.Zero;
    [JsonInclude] public int Collision = 0;

    [JsonInclude]
    public string Effect
    {
        get => effect;
        set
        {
            effect = value;
            ReloadEffectAttribs();
        }
    }

    public void ReloadEffectAttribs()
    {
        if (!Runtime.CurrentRuntime.Shaders.ContainsKey(effect)) return;
        EffectShader = Runtime.CurrentRuntime.Shaders[effect];
        LocFXCreatedAt = GetShaderLocation(Runtime.CurrentRuntime.Shaders[effect], "created_at");
        LocFXTime = GetShaderLocation(Runtime.CurrentRuntime.Shaders[effect], "time");
        LocFXResolution = GetShaderLocation(EffectShader, "resolution");
        LocFXOutputResolution = GetShaderLocation(EffectShader, "output_resolution");
        LocFXPosition = GetShaderLocation(EffectShader, "position");
    }
    
    private string effect = "";

    [JsonIgnore] public Shader EffectShader;
    [JsonIgnore] public Shader TextureShader;
    [JsonIgnore] public int LocFXCreatedAt;
    [JsonIgnore] public int LocFXTime;
    [JsonIgnore] public int LocFXResolution;
    [JsonIgnore] public int LocFXOutputResolution;
    [JsonIgnore] public int LocFXPosition;
    [JsonIgnore] public int LocTXColor;
    [JsonIgnore] public RenderTexture2D? RuntimeTexture;
    [JsonIgnore] public RenderTexture2D? TempTexture;
    [JsonIgnore] public bool IsInitialized = false;
    [JsonIgnore] private int CurrentX = 0, CurrentY = 0;
    [JsonIgnore] private Dictionary<int, Vector2> Positions = new();
    
    void Init()
    {
        if (IsInitialized)
            return;
        #if DEBUG
        TextureShaderText = File.ReadAllText($"Assets/Shaders/{Texture}.fs");
        #endif
        RuntimeTexture = LoadRenderTexture(8192, 8192);
        TempTexture = LoadRenderTexture(8192, 8192);
        TextureShader = Runtime.CurrentRuntime.Shaders[Texture];
        LocTXColor = GetShaderLocation(TextureShader, "color");
        IsInitialized = true;
    }

    public Texture2D GetTexture(int color)
    {
        if (RenderType == BulletVisualRenderType.FromSprite)
            return Runtime.CurrentRuntime.Textures[Texture];
        if(!IsInitialized)
            Init();
        if (Positions.ContainsKey(color))
            return RuntimeTexture!.Value.Texture;
        BeginTextureMode(TempTexture!.Value);
        SetShaderValue(TextureShader, LocTXColor, Helper.ColorIntToVector3(color), ShaderUniformDataType.Vec3);
        BeginShaderMode(TextureShader);
        DrawTexturePro(T384448, FRC384448, new Rectangle(CurrentX, CurrentY, SourceSize), Vector2.Zero, 0, Color.White);
        EndTextureMode();
        EndShaderMode();
        Positions[color] = new Vector2(CurrentX, CurrentY);
        CurrentX += (int)SourceSize.X;
        if (CurrentX > 8192)
        {
            CurrentX = 0;
            CurrentY += (int)SourceSize.Y;
        }
        BeginTextureMode(RuntimeTexture!.Value);
        ClearBackground(Color.White with {A=0});
        DrawTexture(TempTexture!.Value.Texture, 0, 0, Color.White);
        EndTextureMode();
        return RuntimeTexture!.Value.Texture;
    }

    public Vector2 GetSpritePosition(int color)
    {
        if (RenderType == BulletVisualRenderType.FromSprite)
            return SpritePosition;
        return Positions[color];
    }
    
    #if DEBUG
    public static string BaseVS = File.ReadAllText("Assets/Shaders/base.vs");
    public string TextureShaderText = "";
    private bool TextureShaderOverriden = false;
    
    public void ReloadTextureShader()
    {
        var shader = LoadShaderFromMemory(BaseVS, TextureShaderText);
        if (!IsShaderValid(shader))
        {
            UnloadShader(shader);
            return;
        }
        if(TextureShaderOverriden)
            UnloadShader(TextureShader);
        TextureShader = shader;
        TextureShaderOverriden = true;

        for (int i = 0; i < Positions.Count; i++)
        {
            var j = Positions.Keys.ElementAt(i);
            Positions.Remove(j);
            GetTexture(j);
        }
    }
    #endif
    
    public void Unload()
    {
        if (!IsInitialized)
            return;
        UnloadRenderTexture(TempTexture.Value);
        UnloadRenderTexture(RuntimeTexture.Value);
    }
}
using System.Numerics;
using static Raylib_cs.Raylib;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using static ImGuiNET.ImGui;

namespace DmitryAndDemid.Screens;

public class GameplayEditorScreen : Screen
{
    public GameplayEditorScreen()
    {
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        TexturePreview = LoadRenderTexture(8192, 8192);
        TexturePreview2 = LoadRenderTexture(8192, 8192);
        EffectsFragmentShaderTexts = new string[Effects.Length];
        EffectsOverride = new bool[Effects.Length];
        EffectsShadersOverrides = new Shader[Effects.Length];
        for (int i = 0; i < EffectsFragmentShaderTexts.Length; i++)
        {
            EffectsOverride[i] = false;
            EffectsFragmentShaderTexts[i] = File.ReadAllText($"Assets/Shaders/{Effects[i]}.fs");
        }
    }

    private bool UseEffect = false;
    private int Item = 0; 
    private int Page = 0;
    private float Zoom = 1;
    private float State = 1;
    private string[] Items = ["Bullet Visuals", ""];
    private Vector3 Color = Vector3.One;
    private RenderTexture2D TexturePreview;
    private RenderTexture2D TexturePreview2;
    private bool ShowFull = false;
    public string ShaderText = "";
    public int EffectIndex = 0;
    private string[] Effects = Runtime.CurrentRuntime.Shaders.Keys.ToArray();
    private string[] EffectsFragmentShaderTexts;
    private bool[] EffectsOverride;
    private Shader[] EffectsShadersOverrides;
    private double TimeStart = 0f;
    private float PreviousValue = 0f;

    public override void TopUpdate()
    {
        var s = Raylib.GetTime() - TimeStart;
        if (s <= 3.2)
        {
            if (s < 1)
                State = (float)s;
            else if(s < 2)
                State = 1;
            else if(s <= 3)
                State = (float)(s - 1);
            else
                State = PreviousValue;
        }
        base.TopUpdate();
    }

    public override void Render()
    {
        DrawBackground();
        base.Render();
    }

#if DEBUG
    public override void DrawImgui()
    {   
        BeginMainMenuBar();
        if (MenuItem("Bullet Viewer"))
            Page = 0;
        if (MenuItem("Texture Viewer"))
            Page = 1;
        if (MenuItem("Exit"))
            Runtime.CurrentRuntime.RemoveScreen(this);
        EndMainMenuBar();
        switch (Page)
        {
            case 0:
                string[] j = Data.BulletVisual.Constants.Select(x => x.Key).ToArray();
                Begin("Bullet Selection");
                BeginTable("Bullet Selection Table", 3);
                EndTable();
                ColorEdit3("Color: ", ref Color);
                
                SliderFloat("Zoom", ref Zoom, 0.01f, 10);
                if(Button("Reset Zoom"))
                    Zoom = 1;
                Checkbox("Show Full", ref ShowFull);
                if (ListBox("Visuals", ref Item, j, j.Length))
                {
                    var r = Effects.FirstOrDefault(x => x.Equals(BulletVisual.Constants.ElementAt(Item).Key as string));
                    EffectIndex = Effects.IndexOf(r);
                }
                var item = Data.BulletVisual.Constants.ElementAt(Item).Value;
                Text($"Size: {item.RenderSize}");
                Text($"Type: {item.RenderType}");
                End();
                
                Begin("Effect Setting");
                Checkbox("Render effect", ref UseEffect);
                if (UseEffect )
                {
                    Text($"Effect: {EffectIndex} {item.Effect}");
                    ListBox("Effect List", ref EffectIndex, Runtime.CurrentRuntime.Shaders.Keys.ToArray(),
                        Runtime.CurrentRuntime.Shaders.Count);
                    SliderFloat("State", ref State, 0, 2);
                    if (Button("Play animation"))
                    {
                        TimeStart = Raylib.GetTime();
                        PreviousValue = State;
                    }
                }
                End();
                if (UseEffect && EffectIndex > -1)
                {
                    Begin($"{Effects[EffectIndex]}.fs - Shader Editor (EFFECT)");
                    if (InputTextMultiline("Text of shader", ref EffectsFragmentShaderTexts[EffectIndex], 65536,
                            new Vector2(640, 480), ImGuiInputTextFlags.AllowTabInput))
                    {
                        var sh = LoadShaderFromMemory(BulletVisual.BaseVS, EffectsFragmentShaderTexts[EffectIndex]);
                        if (IsShaderValid(sh))
                        {
                            if(EffectsOverride[EffectIndex])
                                UnloadShader(EffectsShadersOverrides[EffectIndex]);
                            EffectsShadersOverrides[EffectIndex] = sh;
                            EffectsOverride[EffectIndex] = true;
                        }
                        else
                            UnloadShader(sh);
                    }
                    End();
                }
                var texture = item.GetTexture(Color);
                Begin("Bullet View");
                BeginTextureMode(TexturePreview2);
                ClearBackground(Raylib_cs.Color.Black with {A = 0});
                if (UseEffect && EffectIndex != -1)
                {
                    var sPos = item.GetSourcePosition(Color);
                    var effect = EffectsOverride[EffectIndex] ? EffectsShadersOverrides[EffectIndex] : Runtime.CurrentRuntime.Shaders[Effects[EffectIndex]];
                    sPos.Y = texture.Height - sPos.Y;
                    Text($"Effect: {Effects[EffectIndex]}");
                    Text($"resolution: {texture.Width} {texture.Height}");
                    Text($"size: {item.SourceSize.Value}");
                    Text($"source pos: {sPos}");
                    SetShaderValue(effect, GetShaderLocation(effect, "resolution"), [(float)texture.Width, (float)texture.Height], ShaderUniformDataType.Vec2);
                    SetShaderValue(effect, GetShaderLocation(effect, "size"), item.SourceSize.Value, ShaderUniformDataType.Vec2);
                    SetShaderValue(effect, GetShaderLocation(effect, "position"),  sPos, ShaderUniformDataType.Vec2);
                    SetShaderValue(effect, GetShaderLocation(effect, "statement"), State, ShaderUniformDataType.Float);
                    BeginShaderMode(effect);
                }
                DrawTexture(texture, 0, 0, Raylib_cs.Color.White);
                EndTextureMode();
                EndShaderMode();
                BeginTextureMode(TexturePreview);
                ClearBackground(Raylib_cs.Color.Black with {A = 0});
                DrawTexture(TexturePreview2.Texture, 0, 0, Raylib_cs.Color.White);
                EndTextureMode();
                if (!ShowFull)
                {
                    var rc = new Rectangle(item.GetSourcePosition(Color), item.GetSourceSize());
                    Text($"Rectangle: {rc}");
                    BeginShaderMode(Runtime.CurrentRuntime.Shaders["flip"]);
                    EndShaderMode();
                    rlImGui.ImageRect(TexturePreview.Texture,
                        (int)(item.SourceSize.Value.X * Zoom), 
                        (int)(item.SourceSize.Value.Y * Zoom),
                        rc);
                }
                else
                {
                    var size = Helper.GetSize(TexturePreview.Texture);
                    var rc = new Rectangle(Vector2.Zero, size);
                    if (item.RenderType == BulletVisualRenderType.FromShader)
                        rc.Size *= new Vector2(1, -1);
                    Text($"Full Size: {size}");
                    Text($"Rectangle: {rc}");
                    rlImGui.ImageRect(TexturePreview.Texture, (int)(size.X * Zoom), (int)(size.Y * Zoom), rc);
                }
                End();
                if (item.RenderType == BulletVisualRenderType.FromSprite)
                    break;
                Begin($"{item.Texture}.fs - Shader Editor (TEXTURE)");
                if(Button("Reload Shader"))
                    item.ReloadShader();
                if (InputTextMultiline("text", ref item.ShaderText, 65536, new Vector2(640, 480), ImGuiInputTextFlags.AllowTabInput))
                    item.ReloadShader();
                if(item.LastShaderInvalid)
                    Text("Something went wrong");
                End();
                break;
        }
        base.DrawImgui();
    }
#endif

    public override void Unload()
    {
        UnloadRenderTexture(TexturePreview);
        base.Unload();
    }
}
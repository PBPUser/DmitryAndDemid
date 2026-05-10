using System.Net.Mime;
using System.Numerics;
using System.Text.Json;
using static Raylib_cs.Raylib;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using ImGuiNET;
using Microsoft.VisualBasic.CompilerServices;
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
        GameplayPreview = LoadRenderTexture(384, 448);
        LoadingPreview = LoadRenderTexture(640, 480);
        LoadingBuffer = LoadRenderTexture(640, 480);
        EffectsFragmentShaderTexts = new string[Effects.Length];
        EffectsOverride = new bool[Effects.Length];
        EffectsShadersOverrides = new Shader[Effects.Length];
        for (int i = 0; i < EffectsFragmentShaderTexts.Length; i++)
        {
            EffectsOverride[i] = false;
            EffectsFragmentShaderTexts[i] = File.ReadAllText($"Assets/Shaders/{Effects[i]}.fs");
        }
        ForkSize =  new Vector2(ForkTexture.Width, ForkTexture.Height);
    }

    private Vector2 ForkSize;
    private Texture2D ForkTexture = Runtime.CurrentRuntime.Textures["vilkaCut.png"];
    private bool UseEffect = false;
    private int Item = 0; 
    private int Page = 0;
    private float Zoom = 1;
    private float State = 1;
    private float Time = 0;
    private Vector3 Color = Vector3.One;
    private Vector2 Position = Vector2.One;
    private RenderTexture2D TexturePreview, TexturePreview2, GameplayPreview, LoadingPreview, LoadingBuffer;
    private bool ShowFull = false;
    private bool HighlightCurrent = false;
    public string ShaderText = "";
    public int EffectIndex = 0;
    private string[] Effects = Runtime.CurrentRuntime.Shaders.Keys.ToArray();
    private string[] EffectsFragmentShaderTexts;
    private bool[] EffectsOverride;
    private Shader[] EffectsShadersOverrides;
    private double TimeStart = 0f;
    private double TimeStart2 = 0f;
    private float PreviousValue = 0f;
    private float Speed = 1f;
    private float PreviousValue2 = 0f;
    private float LoadingScreenLength = 8f;
    private float LoadingScreenFade = 0.5f;
    private float LoadingScreenFadeState = 1f;
    private float LoadingFifoShowDelay = 2f;
    private string LoadingShaderText = File.ReadAllText("Assets/Shaders/loading.fs");
    private string LoadingSwapShaderText = File.ReadAllText("Assets/Shaders/loading_swap.fs");
    private bool LoadingShaderOverriden = false;
    private bool LoadingSwapShaderOverriden = false;
    private Texture2D LoadingTexture = Runtime.CurrentRuntime.Textures["loading.png"];
    private string[] Endings = Directory.GetFiles("Assets/Data/Endings")
        .Select(x => File.ReadAllText(x)).ToArray();
    private string[] EndingNames = Directory.GetFiles("Assets/Data/Endings")
        .Select(x => Path.GetFileNameWithoutExtension(x)).ToArray();
    private int EndingIndex = 0;
    private bool ShowError = false;
    private string ErrorText = "";
    private string[] SpellCards => Directory.GetFiles("Assets/Data/SpellCards").Select(x => x.Split('/').Last()).ToArray();
    private string SpellcardFilename => SpellCards[SpellcardIndex];
    private string CustomSpellcardFilename = "";
    private int SpellcardIndex = 0;
    
    private Shader
        LoadingTileShader = Runtime.CurrentRuntime.Shaders["loading"],
        LoadingSwapShader = Runtime.CurrentRuntime.Shaders["loading_swap"];

    private static Rectangle
        LoadingBufferSource = new Rectangle(0, -480, 640, 480),
        LoadingTarget = new Rectangle(0, 0, 640, 480);
    private Rectangle
        LoadingSource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["loading.png"]);
    
    
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
        if (EffectIndex == -1)
            return;
    }

#if DEBUG
    public override void DrawImgui()
    {   
        BeginMainMenuBar();
        if (MenuItem("BulletEdit"))
            Page = 0;
        if (MenuItem("GameEffect Editor"))
            Page = 1;
        if (MenuItem("Loading Tester"))
            Page = 2;
        if (MenuItem("Ending/Staff Roll"))
            Page = 3;
        if (MenuItem("SpellCards"))
            Page = 4;
        if (MenuItem("Exit"))
            Runtime.CurrentRuntime.RemoveScreen(this);
        EndMainMenuBar();
        switch (Page)
        {
            case 0:
                string[] j = BulletVisual.Constants.Select(x => x.Key).ToArray();
                Begin("Bullet Selection");
                BeginTable("Bullet Selection Table", 3);
                EndTable();
                ColorEdit3("Color: ", ref Color);
                
                SliderFloat("Zoom", ref Zoom, 0.01f, 10);
                if(Button("Reset Zoom"))
                    Zoom = 1;
                Checkbox("Show Full", ref ShowFull);
                Checkbox("Highlight Current", ref HighlightCurrent);
                if (ListBox("Visuals", ref Item, j, j.Length))
                {
                    var r = Effects.FirstOrDefault(x => x.Equals(BulletVisual.Constants.ElementAt(Item).Key as string));
                    EffectIndex = Effects.IndexOf(r);
                }
                var item = Data.BulletVisual.Constants.ElementAt(Item).Value;
                Text($"Size: {item.RenderSize}");
                Text($"Type: {item.RenderType}");
                Text($"Current Position: {item.CurrentX} {item.CurrentY}");
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
                if (ShowFull && HighlightCurrent)
                {
                    var rc = new Rectangle(item.GetSourcePosition(Color), item.GetSourceSize());
                    DrawRectangle((int)rc.X,
                        (int)(rc.Y+rc.Height),
                        (int)rc.Width,
                        (int)Math.Abs(rc.Height),
                        Raylib_cs.Color.Magenta with
                        {
                            A = 128
                        });
                }
                DrawTexture(TexturePreview2.Texture, 0, 0, Raylib_cs.Color.White);
                EndTextureMode();
                if (ShowFull)
                {
                    var size = Helper.GetSize(TexturePreview.Texture);
                    var rc = new Rectangle(Vector2.Zero, size);
                    if (item.RenderType == BulletVisualRenderType.FromShader)
                        rc.Size *= new Vector2(1, -1);
                    Text($"Full Size: {size}");
                    Text($"Rectangle: {rc}");
                    rlImGui.ImageRect(TexturePreview.Texture, (int)(size.X * Zoom), (int)(size.Y * Zoom), rc);
                }
                else
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
            case 1:
                Begin("Effect Selector");
                ListBox("Select effect for gameplay", ref EffectIndex, Effects, Effects.Length);
                SliderFloat("Time", ref Time, 0, 1);
                if (Button("Play Animation"))
                    TimeStart2 = Raylib.GetTime();
                SliderFloat2("Position", ref Position, 0, 448);
                End();
                if(EffectIndex == -1)
                    break;
                Begin($"{Effects[EffectIndex]}.fs - Shader Editor (GAMESCREEN EFFECT)");
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
                
                var shader = EffectsOverride[EffectIndex] ? EffectsShadersOverrides[EffectIndex] : Runtime.CurrentRuntime.Shaders[Effects[EffectIndex]];
                BeginTextureMode(GameplayPreview);
                SetShaderValue(shader,GetShaderLocation(shader,"position"), Position * new Vector2(1, -1), ShaderUniformDataType.Vec2);
                SetShaderValue(shader,GetShaderLocation(shader,"time"), Time, ShaderUniformDataType.Float);
                BeginShaderMode(shader);
                DrawTexturePro(BulletVisual.Rectangle384x448.Texture, new Rectangle(0,448,384,-448), new Rectangle(0,0,384,448),
                    Vector2.Zero, 0, Raylib_cs.Color.White);
                EndShaderMode();
                EndTextureMode();
                
                Begin("Gameplay");
                Text($"Position: {Position}");
                Text($"Time: {Time}");
                rlImGui.Image(GameplayPreview.Texture);
                End();
                break;
            case 2:
                float time = (float)Raylib.GetTime();
                Begin("Loading Screen Tester");
                SliderFloat("Speed", ref Speed, 0, 20);
                SliderFloat("Fade", ref LoadingScreenFadeState, 0f, 2f);
                End();
                Begin("Loading Screen Animation Tester");
                SliderFloat("Fade Length", ref LoadingScreenFade, 0.1f, 2f);
                SliderFloat("Loading Time", ref LoadingScreenLength, 2, 30);
                SliderFloat("Fifo Delay", ref LoadingFifoShowDelay, 2, 30);
                if (Button("Play"))
                {
                    TiledLoadingScreen? screen = null;
                    screen = new TiledLoadingScreen(LoadingScreenLength, LoadingScreenFade, () =>
                    {
                        Runtime.CurrentRuntime.RemoveScreen(screen);
                    }, true, LoadingFifoShowDelay);
                    screen.LoadingShaderTiles = LoadingTileShader;
                    screen.LoadingShaderSwap = LoadingSwapShader;
                    screen.TimeDisappear = screen.TimeAppear + LoadingScreenLength;
                    Runtime.CurrentRuntime.AddScreen(screen);
                }
                End();
                Begin("loading.fs - Shader Editor (LOADING)");
                if (InputTextMultiline("Text", ref LoadingShaderText, 65536, new Vector2(640, 480),
                        ImGuiInputTextFlags.AllowTabInput))
                {
                    var sh =  LoadShaderFromMemory(BulletVisual.BaseVS, LoadingShaderText);
                    if (IsShaderValid(sh))
                    {
                        if(LoadingShaderOverriden)
                            UnloadShader(LoadingTileShader);
                        LoadingTileShader = sh;
                        LoadingShaderOverriden = true;
                    }
                    else
                        UnloadShader(sh);
                }
                End();
                Begin("loading_swap.fs - Shader Editor (LOADING)");
                if (InputTextMultiline("Text", ref LoadingSwapShaderText, 65536, new Vector2(640, 480),
                        ImGuiInputTextFlags.AllowTabInput))
                {
                    var sh =  LoadShaderFromMemory(BulletVisual.BaseVS, LoadingSwapShaderText);
                    if (IsShaderValid(sh))
                    {
                        if (LoadingSwapShaderOverriden)
                            UnloadShader(LoadingSwapShader);
                        LoadingSwapShader = sh;
                        LoadingSwapShaderOverriden = true;
                    }
                    else
                        UnloadShader(sh);
                }
                End();
                BeginTextureMode(LoadingBuffer);
                ClearBackground(Raylib_cs.Color.Black with {A=0});
                SetShaderValue(LoadingTileShader, GetShaderLocation(LoadingTileShader, "time"), time,
                    ShaderUniformDataType.Float);                
                SetShaderValue(LoadingTileShader, GetShaderLocation(LoadingTileShader, "speed"), Speed,
                    ShaderUniformDataType.Float);
                SetShaderValue(LoadingTileShader, GetShaderLocation(LoadingTileShader, "textureRes"), LoadingSource.Size * 2,
                    ShaderUniformDataType.Vec2);
                SetShaderValue(LoadingTileShader, GetShaderLocation(LoadingTileShader, "outputRes"), LoadingTarget.Size,
                    ShaderUniformDataType.Vec2);
                BeginShaderMode(LoadingTileShader);
                DrawTexturePro(LoadingTexture, LoadingSource, LoadingTarget, Vector2.Zero, 0, Raylib_cs.Color.White);
                EndShaderMode();
                EndTextureMode();
                BeginTextureMode(LoadingPreview);
                ClearBackground(Raylib_cs.Color.Black with {A=0});
                SetShaderValue(LoadingSwapShader, GetShaderLocation(LoadingSwapShader, "time"), LoadingScreenFadeState,
                    ShaderUniformDataType.Float);
                BeginShaderMode(LoadingSwapShader);
                DrawTexturePro(LoadingBuffer.Texture, LoadingBufferSource, LoadingTarget, Vector2.Zero, 0, Raylib_cs.Color.White);
                EndShaderMode();
                EndTextureMode();
                Begin("Loading Preview");
                rlImGui.Image(LoadingPreview.Texture);
                End();
                break;
            case 3:
                Begin("Ending tester");
                ListBox("endings", ref EndingIndex, EndingNames, Endings.Length);
                End();
                Begin($"{EndingNames[EndingIndex]}.json - Ending Editor");
                if (Button("Run"))
                {
                    try
                    {
                        Runtime.CurrentRuntime.AddScreen(new EndingScreen(0,  JsonSerializer.Deserialize<EndingInfo>(Endings[EndingIndex])!,false));
                    }
                    catch (Exception e)
                    {
                        Error(e.ToString());
                    }
                }
                if (InputTextMultiline("text", ref Endings[EndingIndex], 65536, new Vector2(640, 480),
                        ImGuiInputTextFlags.AllowTabInput))
                {
                    
                }
                End();
                break;
            case 4:
                Begin("Load file");
                if (ListBox("select file", ref SpellcardIndex, SpellCards, SpellCards.Length, 32))
                    CustomSpellcardFilename = SpellcardFilename.Remove(SpellcardFilename.Length - 4, 4);
                Text("Load: ");
                InputText("File name", ref CustomSpellcardFilename, 64, ImGuiInputTextFlags.None);
                if (Button(File.Exists($"Assets/Data/SpellCards/{CustomSpellcardFilename}.sid") ? "Load" : "Create"))
                {
                    FileStageInfo info;
                    
                    if (File.Exists($"Assets/Data/SpellCards/{CustomSpellcardFilename}.sid"))
                    {
                        BitPackage package = BitPackage.OpenStreamReadPackage($"Assets/Data/SpellCards/{CustomSpellcardFilename}.sid");
                        info = FileStageInfo.Load(ref package);
                        package.Dispose();
                    }
                    else
                    {
                        info = new FileStageInfo();
                    }
                    Runtime.CurrentRuntime.AddScreen(new StageEditorScreen(info, $"Assets/Data/SpellCards/{CustomSpellcardFilename}.sid"));
                }
                End();
                break;
        }

        if (ShowError)
        {
            Begin("Error");
            Text(ErrorText);
            if(Button("OK"))
                ShowError = false;
            End();
        }
        base.DrawImgui();
    }

    private void Error(string text)
    {
        ErrorText = text;
        ShowError = true;
    }
#endif

    public override void Unload()
    {
        UnloadRenderTexture(TexturePreview);
        base.Unload();
    }
}
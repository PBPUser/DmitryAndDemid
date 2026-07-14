using DmitryAndDemid.Rendering;
using System.Net.Mime;
using System.Numerics;
using System.Text.Json;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;
using ImGuiNET;
using Microsoft.VisualBasic.CompilerServices;
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
        SplashTimeTexture = LoadRenderTexture((int)(128 * Runtime.CurrentRuntime.ScaleF),
            (int)(96 * Runtime.CurrentRuntime.ScaleF));
        EffectsFragmentShaderTexts = new string[Effects.Length];
        EffectsOverride = new bool[Effects.Length];
        EffectsShadersOverrides = new ShaderHandle[Effects.Length];
        for (int i = 0; i < EffectsFragmentShaderTexts.Length; i++)
        {
            EffectsOverride[i] = false;
            EffectsFragmentShaderTexts[i] = File.ReadAllText($"Assets/Shaders/{Effects[i]}.fs");
        }
        ForkSize =  new Vector2(ForkTexture.Width, ForkTexture.Height);
        RedrawTimer();
        RerenderSpellText();
    }

    private Vector2 ForkSize;
    private TextureHandle ForkTexture = Runtime.CurrentRuntime.Textures["vilkaCut.png"];
    private bool UseEffect = false;
    private int Item = 0;
    private double TimeFrom = 0;

    private TargetHandle BackgroundTestTexture =
        LoadRenderTexture(384, 448);

    private bool BackgroundTesterEnabled = false;
    private string[] Textures => Runtime.CurrentRuntime.Textures.Keys.ToArray();
    private int BackgroundTestIndex = -1;

    private int BGTesterX = 192;
    private int BGTesterY = 400;
    private int Page = 0;
    private float Zoom = 1;
    private float State = 1;
    private float Time = 0;
    private Vector3 PickerColor = Vector3.One;
    private Vector2 Position = Vector2.One;
    private TargetHandle TexturePreview, TexturePreview2, GameplayPreview, LoadingPreview, LoadingBuffer;
    private bool ShowFull = false;
    private bool HighlightCurrent = false;
    public int EffectIndex = 0;
    private string[] Effects = Runtime.CurrentRuntime.Shaders.Keys.ToArray();
    private string[] EffectsFragmentShaderTexts;
    private bool[] EffectsOverride;
    private ShaderHandle[] EffectsShadersOverrides;
    private double TimeStart = 0f;
    private double TimeStart2 = 0f;
    private float PreviousValue = 0f;
    private float Speed = 1f;
    private float PreviousValue2 = 0f;
    private float LoadingScreenLength = 8f;
    private float LoadingScreenFade = 0.5f;
    private float LoadingScreenFadeState = 1f;
    private float LoadingFifoShowDelay = 2f;
    private string SelectedShaderText = "";
    private string LoadingShaderText = File.ReadAllText("Assets/Shaders/loading.fs");
    private string LoadingSwapShaderText = File.ReadAllText("Assets/Shaders/loading_swap.fs");
    private bool LoadingShaderOverriden = false;
    private bool LoadingSwapShaderOverriden = false;
    private TextureHandle LoadingTexture = Runtime.CurrentRuntime.Textures["loading.png"];
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

    private bool ApplyBulletEffect = false;
    private int TickStart = 0;
    private int CurrentTick = 0;
    private TargetHandle? TextureEffectTest;

    private int TickTest = 0;
    private float TimeTest = 0;
    private TargetHandle? SplashTimeTexture;
    
    
    private ShaderHandle
        LoadingTileShader = Runtime.CurrentRuntime.Shaders["loading"],
        LoadingSwapShader = Runtime.CurrentRuntime.Shaders["loading_swap"];

    private static Rect
        LoadingBufferSource = new Rect(0, -480, 640, 480),
        LoadingTarget = new Rect(0, 0, 640, 480);
    private Rect
        LoadingSource = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["loading.png"]);
    
    
    public override void TopUpdate()
    {
        var s = Gfx.GetTime() - TimeStart;
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
        if (MenuItem("Text"))
            Page = 5;
        if (MenuItem("Timer"))
            Page = 6;
        if (MenuItem("Spell"))
            Page = 7;
        if (MenuItem("Alias"))
            Page = 8;
        if (MenuItem("Bonus"))
            Page = 9;
        if (MenuItem("Exit"))
            Runtime.CurrentRuntime.RemoveScreen(this);
        EndMainMenuBar();
        switch (Page)
        {
            case 0:
                string[] j = Runtime.CurrentRuntime.BulletVisualPresets.Select(x => x.Key).ToArray();
                Begin("Bullet Selection");
                BeginTable("Bullet Selection Table", 3);
                EndTable();
                ColorEdit3("Bullet Color", ref PickerColor);
                SliderFloat("Zoom", ref Zoom, 0.01f, 10);
                if(Button("Reset Zoom"))
                    Zoom = 1;
                Checkbox("Show Full", ref ShowFull);
                
                Checkbox("Highlight Current", ref HighlightCurrent);
                if (ListBox("Visuals", ref Item, j, j.Length))
                {
                    var r = Effects.FirstOrDefault(x => x.Equals(Runtime.CurrentRuntime.BulletVisualPresets.ElementAt(Item).Key));
                    EffectIndex = Effects.IndexOf(r);
                    if (Item > -1)
                    {
                        var _item = Runtime.CurrentRuntime.BulletVisualPresets.ElementAt(Item).Value;
                        if (_item.Effect != "")
                        {
                            RerenderEffectPreview();
                            ShaderTestingText = File.ReadAllText($"Assets/Shaders/{_item.Effect}.fs");
                        }
                    }
                    
                }
                var item = Runtime.CurrentRuntime.BulletVisualPresets.ElementAt(Item).Value;
                if (item.Effect != "")
                {
                    if(Checkbox("Apply effect", ref ApplyBulletEffect))
                        RerenderEffectPreview();
                }
                Text($"Size: {item.SourceSize}");
                Text($"Type: {item.RenderType}");
                End();
                if (item.Effect != "")
                {
                    Begin("ShaderHandle Editor for effect");
                    if (InputTextMultiline("Text", ref ShaderTestingText, 256354, new Vector2(800, 600), ImGuiInputTextFlags.AllowTabInput))
                    {
                        var effectShader = LoadShaderFromMemory(BulletRenderingInfo.BaseVS, ShaderTestingText);
                        if (!IsShaderValid(effectShader))
                        {
                            UnloadShader(effectShader);
                        }
                        else
                        {
                            UnloadShader(Runtime.CurrentRuntime.Shaders[item.Effect]);
                            Runtime.CurrentRuntime.Shaders[item.Effect] = effectShader;
                            RerenderEffectPreview();
                        }
                    }
                    End();
                }
                
                Begin("Effect preview");
                if (TextureEffectTest != null)
                {
                    Text($"Size: {TextureEffectTest.Value.Texture.Width}x{TextureEffectTest.Value.Texture.Height}");
                    Engine.Backend.DebugUiImage(TextureEffectTest.Value);
                }
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
                        TimeStart = Gfx.GetTime();
                        PreviousValue = State;
                    }
                }
                End();
                if (UseEffect && EffectIndex > -1)
                {
                    Begin($"{Effects[EffectIndex]}.fs - ShaderHandle Editor (EFFECT)");
                    if (InputTextMultiline("Text of shader", ref EffectsFragmentShaderTexts[EffectIndex], 65536,
                            new Vector2(640, 480), ImGuiInputTextFlags.AllowTabInput))
                    {
                        var sh = LoadShaderFromMemory(Runtime.BaseVertexShader, EffectsFragmentShaderTexts[EffectIndex]);
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
                var texture = item.GetTexture(Helper.Vector3ColorToInt(PickerColor));
                Begin("Bullet View");
                BeginTextureMode(TexturePreview2);
                ClearBackground(Rgba.Black with {A = 0});
                if (UseEffect && EffectIndex != -1)
                {
                    var sPos = item.GetSpritePosition(Helper.Vector3ColorToInt(PickerColor));
                    var effect = EffectsOverride[EffectIndex] ? EffectsShadersOverrides[EffectIndex] : Runtime.CurrentRuntime.Shaders[Effects[EffectIndex]];
                    sPos.Y = texture.Height - sPos.Y;
                    Text($"Effect: {Effects[EffectIndex]}");
                    Text($"Resolution: {texture.Width} {texture.Height}");
                    Text($"Size: {item.SourceSize}");
                    Text($"Source Position: {sPos}");
                    SetShaderValue(effect, GetShaderLocation(effect, "resolution"), [(float)texture.Width, (float)texture.Height], UniformType.Vec2);
                    SetShaderValue(effect, GetShaderLocation(effect, "size"), item.SourceSize, UniformType.Vec2);
                    SetShaderValue(effect, GetShaderLocation(effect, "position"),  sPos, UniformType.Vec2);
                    SetShaderValue(effect, GetShaderLocation(effect, "statement"), State, UniformType.Float);
                    BeginShaderMode(effect);
                }
                DrawTexture(texture, 0, 0, Rgba.White);
                EndTextureMode();
                EndShaderMode();
                BeginTextureMode(TexturePreview);
                ClearBackground(Rgba.Black with {A = 0});
                if (ShowFull && HighlightCurrent)
                {
                    var rc = new Rect(item.GetSpritePosition(Helper.Vector3ColorToInt(PickerColor)), item.SourceSize);
                    DrawRectangle((int)rc.X,
                        (int)(rc.Y+rc.Height),
                        (int)rc.Width,
                        (int)Math.Abs(rc.Height),
                        Rgba.Magenta with { A = 128 });
                }
                DrawTexture(TexturePreview2.Texture, 0, 0, Rgba.White);
                EndTextureMode();
                Text($"Type: {item.RenderType}");
                Text($"Texture: {item.Texture}");
                Text($"Source Size: {item.SourceSize}");
                Text($"Sprite Position: {item.SpritePosition}");
                Text($"Collision: {item.Collision}");
                if (ShowFull)
                {
                    var size = Helper.GetSize(texture);
                    var rc = new Rect(Vector2.Zero, size);
                    if (item.RenderType == BulletVisualRenderType.FromShader)
                        rc.Size *= new Vector2(1, -1);
                    Text($"Full Size: {size}");
                    Text($"Rect: {rc}");
                    Engine.Backend.DebugUiImage(texture);
                }
                else
                {
                    var ss = item.SourceSize;
                    var rc = new Rect(item.GetSpritePosition(Helper.Vector3ColorToInt(PickerColor)), item.SourceSize);
                    Text($"Rect: {rc}");
                    Text($"Position: {item.GetSpritePosition(Helper.Vector3ColorToInt(PickerColor))}");
                    Engine.Backend.DebugUiImage(texture);
                }
                End();
                if (item.RenderType == BulletVisualRenderType.FromSprite)
                    break;
                Begin($"{item.Texture}.fs - ShaderHandle Editor (TEXTURE)");
                if(Button("Reload ShaderHandle"))
                    item.ReloadTextureShader();
                if (InputTextMultiline("text", ref item.TextureShaderText, 65536, new Vector2(640, 480), ImGuiInputTextFlags.AllowTabInput))
                    item.ReloadTextureShader();
                End();
                break;
            case 1:
                Begin("Effect Selector");
                ListBox("Select effect for gameplay", ref EffectIndex, Effects, Effects.Length);
                SliderFloat("Time", ref Time, 0, 1);
                if (Button("Play Animation"))
                    TimeStart2 = Gfx.GetTime();
                SliderFloat2("Position", ref Position, 0, 448);
                End();
                if(EffectIndex == -1)
                    break;
                Begin($"{Effects[EffectIndex]}.fs - ShaderHandle Editor (GAMESCREEN EFFECT)");
                if (InputTextMultiline("Text of shader", ref EffectsFragmentShaderTexts[EffectIndex], 65536,
                        new Vector2(640, 480), ImGuiInputTextFlags.AllowTabInput))
                {
                    var sh = LoadShaderFromMemory(Runtime.BaseVertexShader, EffectsFragmentShaderTexts[EffectIndex]);
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
                SetShaderValue(shader,GetShaderLocation(shader,"position"), Position * new Vector2(1, -1), UniformType.Vec2);
                SetShaderValue(shader,GetShaderLocation(shader,"time"), Time, UniformType.Float);
                BeginShaderMode(shader);
                DrawTexturePro(Runtime.CurrentRuntime.Textures["384x448"], new Rect(0,448,384,-448), new Rect(0,0,384,448),
                    Vector2.Zero, 0, Rgba.White);
                EndShaderMode();
                EndTextureMode();
                
                Begin("Gameplay");
                Text($"Position: {Position}");
                Text($"Time: {Time}");
                Engine.Backend.DebugUiImage(GameplayPreview);
                End();
                break;
            case 2:
                float time = (float)Gfx.GetTime();
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
                Begin("loading.fs - ShaderHandle Editor (LOADING)");
                if (InputTextMultiline("Text", ref LoadingShaderText, 65536, new Vector2(640, 480),
                        ImGuiInputTextFlags.AllowTabInput))
                {
                    var sh =  LoadShaderFromMemory(Runtime.BaseVertexShader, LoadingShaderText);
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
                Begin("loading_swap.fs - ShaderHandle Editor (LOADING)");
                if (InputTextMultiline("Text", ref LoadingSwapShaderText, 65536, new Vector2(640, 480),
                        ImGuiInputTextFlags.AllowTabInput))
                {
                    var sh =  LoadShaderFromMemory(Runtime.BaseVertexShader, LoadingSwapShaderText);
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
                ClearBackground(Rgba.Black with {A=0});
                SetShaderValue(LoadingTileShader, GetShaderLocation(LoadingTileShader, "time"), time,
                    UniformType.Float);                
                SetShaderValue(LoadingTileShader, GetShaderLocation(LoadingTileShader, "speed"), Speed,
                    UniformType.Float);
                SetShaderValue(LoadingTileShader, GetShaderLocation(LoadingTileShader, "textureRes"), LoadingSource.Size * 2,
                    UniformType.Vec2);
                SetShaderValue(LoadingTileShader, GetShaderLocation(LoadingTileShader, "outputRes"), LoadingTarget.Size,
                    UniformType.Vec2);
                BeginShaderMode(LoadingTileShader);
                DrawTexturePro(LoadingTexture, LoadingSource, LoadingTarget, Vector2.Zero, 0, Rgba.White);
                EndShaderMode();
                EndTextureMode();
                BeginTextureMode(LoadingPreview);
                ClearBackground(Rgba.Black with {A=0});
                SetShaderValue(LoadingSwapShader, GetShaderLocation(LoadingSwapShader, "time"), LoadingScreenFadeState,
                    UniformType.Float);
                BeginShaderMode(LoadingSwapShader);
                DrawTexturePro(LoadingBuffer.Texture, LoadingBufferSource, LoadingTarget, Vector2.Zero, 0, Rgba.White);
                EndShaderMode();
                EndTextureMode();
                Begin("Loading Preview");
                Engine.Backend.DebugUiImage(LoadingPreview);
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
            case 5:
                Begin("Background tester");
                Checkbox("Enabled", ref BackgroundTesterEnabled);
                if (BackgroundTesterEnabled)
                {
                    SliderInt("X", ref BGTesterX, -32, 448);
                    SliderInt("Y", ref BGTesterY, -32, 512);
                    Combo("Background", ref BackgroundTestIndex, Textures, Textures.Length);
                    if (BackgroundTestIndex != -1)
                    {
                        Vector2 s = new Vector2(BGTesterX, BGTesterY);
                        var shader_ = Runtime.CurrentRuntime.Shaders[Shaders[SelectedShaderIndex]];
                        SetShaderValue(shader_, GetShaderLocation(shader_, "time"), (float)(Gfx.GetTime() - TimeFrom) / 60, UniformType.Float);
                        SetShaderValue(shader_, GetShaderLocation(shader_, "pos"), s, UniformType.Vec2);
                        BeginTextureMode(BackgroundTestTexture);
                        ClearBackground(Rgba.White);
                        BeginShaderMode(shader_);
                        DrawTexture(Runtime.CurrentRuntime.Textures[Textures[BackgroundTestIndex]],
                            0, 0, Rgba.White);
                        EndShaderMode();
                        EndTextureMode();
                        Engine.Backend.DebugUiImage(BackgroundTestTexture);
                    }
                }
                End();
                Begin("Text ShaderHandle Editor");
                if (SliderFloat("Spacing", ref Spacing, 0f, 32f)) 
                    RerenderPreviewText();
                if (SliderFloat("HPadding", ref HPadding, 0f, 32f)) 
                    RerenderPreviewText();
                if (SliderFloat("VPadding", ref VPadding, 0f, 32f)) 
                    RerenderPreviewText();
                if (SliderFloat("FontHandle Size", ref FontSize, 0f, 256f)) 
                    RerenderPreviewText();
                if (SliderFloat("Scale", ref BorderWidth, 0f, 32f)) 
                    RerenderPreviewText();
                if(InputText("Testing text", ref ShaderTestingText, 256))
                    RerenderPreviewText();
                if(Combo("FontHandle", ref SelectedFontIndex, Fonts, Fonts.Length))
                    RerenderPreviewText();
                if (Combo("ShaderHandle", ref SelectedShaderIndex, Shaders, Shaders.Length))
                {
                    ShaderText = File.ReadAllText($"Assets/Shaders/{Shaders[SelectedShaderIndex]}.fs");
                    RerenderPreviewText();
                }
                if (InputTextMultiline("ShaderHandle text", ref ShaderText, 65536, new Vector2(640, 480), ImGuiInputTextFlags.AllowTabInput))
                {
                    var sh = LoadShaderFromMemory(File.ReadAllText("Assets/Shaders/base.vs"), ShaderText);
                    if (!IsShaderValid(sh))
                    {   
                        UnloadShader(sh);
                        End();
                        return;
                    }
                    UnloadShader(Runtime.CurrentRuntime.Shaders[Shaders[SelectedShaderIndex]]);
                    Runtime.CurrentRuntime.Shaders[Shaders[SelectedShaderIndex]] = sh;
                    Helper.ReprepareTimerShader();
                    RerenderPreviewText();
                }
                if(TextTestTexture != null)
                    Engine.Backend.DebugUiImage(TextTestTexture.Value);
                End();
                
                Helper.PrepareTimer((int)((Gfx.GetTime() * 60) % 6000));
                Helper.DrawTimer(12, 36, false);
                break;
            case 6:
                Begin("Test Splash");
                if(SliderFloat("Time", ref TimeTest, 0f, 1000f) || SliderInt("Ticks", ref TickTest, 0, 6000))
                    RedrawTimer();
                End();
                if (SplashTimeTexture != null)
                {
                    Begin("Image");
                    var jv = Helper.GetFullSourceRenderTexture(SplashTimeTexture.Value);
                    Text($"Rect: {jv}");
                    Engine.Backend.DebugUiImage(SplashTimeTexture.Value);
                    End();
                }
                Begin("Score Subtitle Preview");
                if (InputInt("Score", ref Score, 1))
                    RedrawSpellSubtitle();
                if (InputInt("Success Attempts", ref SuccessAttempts, 1))
                    RedrawSpellSubtitle();
                if (InputInt("Total Attempts", ref TotalAttempts, 1))
                    RedrawSpellSubtitle();
                Engine.Backend.DebugUiImage(TextureTestSpellSubtitle);
                End();
                break;
            case 7:
                Begin("Test Spell Splash");
                if (InputText("Score", ref ScoreText, 40))
                    RerenderSpellText();
                if(IsRenderTextureValid(SpellTestTexture))
                    Engine.Backend.DebugUiImage(SpellTestTexture);
                End();
                break;
            case 8:
                Begin("Text Testing Window");
                if(InputText("text", ref FontTextTest, 65536))
                    RedrawFontTest();
                if(Combo("font", ref SelectedFontIndex, Fonts, Fonts.Length))
                    RedrawFontTest();
                if(SliderFloat("font size", ref FontSize1, 8, 64))
                    RedrawFontTest();
                End();
                Begin("Score Testing Window");
                InputInt("Score", ref TesterScore);
                SliderInt("Continue", ref TesterContinue, 0, 9);
                Text(Helper.FormatScore(TesterScore, TesterContinue));
                End();
                Begin("Preview");
                if(IsRenderTextureValid(FontTestTexture))
                    Engine.Backend.DebugUiImage(FontTestTexture);
                if(IsRenderTextureValid(FontTestTexture2))
                    Engine.Backend.DebugUiImage(FontTestTexture2);
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

    private void RedrawSubtitle()
    {
        
    }

    private void Error(string text)
    {
        ErrorText = text;
        ShowError = true;
    }

    private void RedrawFontTest()
    {
        if (SelectedFontIndex == -1)
            return;
        if(IsRenderTextureValid(FontTestTexture))
            UnloadRenderTexture(FontTestTexture);
        if(IsRenderTextureValid(FontTestTexture2))
            UnloadRenderTexture(FontTestTexture2);
        Helper.DrawTextAliased(out FontTestTexture,out FontTestTexture2,
            Runtime.CurrentRuntime.Fonts[Fonts[SelectedFontIndex]],
            FontSize1, 0, FontTextTest, Rgba.White);
    }

    private string FontTextTest = "";
    private TargetHandle? TextTestTexture = null;
    private TargetHandle SpellTestTexture = new TargetHandle();
    private TargetHandle FontTestTexture = new TargetHandle();
    private TargetHandle FontTestTexture2 = new TargetHandle();
    private float FontSize1 = 10;
    private float FontSize = 14;
    private float Spacing = 2;
    private float BorderWidth = 1;
    private string[] Fonts => Runtime.CurrentRuntime.Fonts.Keys.ToArray();
    private int SelectedFontIndex = -1;
    private FontHandle SelectedFont => SelectedFontIndex == -1
        ? GetFontDefault()
        : Runtime.CurrentRuntime.Fonts[Fonts[SelectedFontIndex]];
    private string[] Shaders => Runtime.CurrentRuntime.Shaders.Keys.ToArray();
    private int SelectedShaderIndex = 0;
    private float HPadding = 0;
    private float VPadding = 0;
    private string ShaderText = "";
    private string ShaderTestingText = "";
    private TargetHandle? TextureTestScore = null;
    private float LetterWidthScore = 0;
    private float PaddingScore = 0;
    private string ScoreText = "";
    private int TesterScore = 0;
    private int TesterContinue = 0;
    
    void RerenderSpellText()
    {
        if(TextureTestScore != null)
            UnloadRenderTexture(TextureTestScore.Value);
        Helper.DrawSpellScore(ScoreText, ref SpellTestTexture, out LetterWidthScore, out PaddingScore);
    }

    private TargetHandle TextureTestSpellSubtitle = LoadRenderTexture(8192,8192);
    private int Score = -1;
    private int TotalAttempts = 0;
    private int SuccessAttempts = 0;
    
    void RedrawSpellSubtitle()
    {
        Helper.DrawSpellSubtitle(TextureTestSpellSubtitle, Score, TotalAttempts, SuccessAttempts);
    }
    
    private void RerenderPreviewText()
    {
        if(TextTestTexture != null)
            UnloadRenderTexture(TextTestTexture.Value);
        var size = MeasureTextEx(SelectedFont, ShaderTestingText, FontSize, Spacing) + new Vector2(HPadding, VPadding) * 2;
        TextTestTexture = LoadRenderTexture((int)size.X, (int)size.Y);
        var temp = LoadRenderTexture((int)size.X, (int)size.Y);
        BeginTextureMode(temp);
        DrawTextEx(
            SelectedFont, ShaderTestingText, new Vector2(HPadding, VPadding), FontSize, Spacing, Rgba.White
            );
        EndTextureMode();
        BeginTextureMode(TextTestTexture.Value);
        SetShaderValue(Runtime.CurrentRuntime.Shaders[Shaders[SelectedShaderIndex]],
            GetShaderLocation(Runtime.CurrentRuntime.Shaders[Shaders[SelectedShaderIndex]], "res"),
            size, UniformType.Vec2);
        SetShaderValue(Runtime.CurrentRuntime.Shaders[Shaders[SelectedShaderIndex]],
            GetShaderLocation(Runtime.CurrentRuntime.Shaders[Shaders[SelectedShaderIndex]], "border_width"),
            BorderWidth, UniformType.Float);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders[Shaders[SelectedShaderIndex]]);
        DrawTexturePro(temp.Texture,
            new Rect(0, TextTestTexture.Value.Texture.Height, 
                TextTestTexture.Value.Texture.Width, TextTestTexture.Value.Texture.Height),
            new Rect(0,0,TextTestTexture.Value.Texture.Width,TextTestTexture.Value.Texture.Height),
            Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        UnloadRenderTexture(temp);
        
    }

    public void RerenderEffectPreview()
    {
        if(TextureEffectTest != null)
            UnloadRenderTexture(TextureEffectTest.Value);
        var item = Runtime.CurrentRuntime.BulletVisualPresets.ElementAt(Item).Value;
        var shader = Runtime.CurrentRuntime.Shaders[item.Effect];
        TextureEffectTest = LoadRenderTexture((int)(64+item.SourceSize.X), (int)(64+item.SourceSize.Y));
        BeginTextureMode(TextureEffectTest.Value);
        var c = Helper.Vector3ColorToInt(PickerColor);
        var t = item.GetTexture(c);
        var j = Helper.GetSize(t);
        var v = item.GetSpritePosition(c);
        SetShaderValue(shader, GetShaderLocation(shader, "resolution"), item.SourceSize, UniformType.Vec2);
        SetShaderValue(shader, GetShaderLocation(shader, "output_resolution"),  j, UniformType.Vec2);
        SetShaderValue(shader, GetShaderLocation(shader, "position"), v, UniformType.Vec2);
        SetShaderValue(shader, GetShaderLocation(shader, "time"), CurrentTick, UniformType.Int);
        SetShaderValue(shader, GetShaderLocation(shader, "created_at"), TickStart, UniformType.Int);
        BeginShaderMode(shader);
        DrawTexturePro(
            t,
            new Rect(item.SpritePosition - new Vector2(32), item.SourceSize + new Vector2(64)),
            new Rect(Vector2.Zero, item.SourceSize + new Vector2(64)),
            Vector2.Zero, 0, Rgba.White
        );
        EndShaderMode();
        EndTextureMode();
    }
    
    public void RedrawTimer()
    {
        Helper.DrawTimerSplash(SplashTimeTexture.Value, TickTest, TimeTest);
    }
#endif
    
    public override void Unload()
    {
        UnloadRenderTexture(TexturePreview);
        base.Unload();
    }
}
using DmitryAndDemid.Rendering;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;
#if DEBUG
using static ImGuiNET.ImGui;
#endif

namespace DmitryAndDemid.Screens;

public class EndingScreen : Screen
{
    private List<EndingElement> Elements;
    private bool ShowStaffRoll = false;
    private int Difficulty = 0;
    
    
    private GameplayScreen? ClearedRun;

    public EndingScreen(int difficulty, EndingInfo info, bool showStaffRoll, GameplayScreen? clearedRun = null)
    {
        // Ending art is its own texture group ("ending"), not part of the main or game sets — bring it in
        // before anything draws, whichever set the screen we arrived from had loaded.
        Runtime.CurrentRuntime.LoadTextureGroup("ending");
        Difficulty = difficulty;
        ShowStaffRoll = showStaffRoll;
        ClearedRun = clearedRun;
        PlayerData.Instance.SetMusicUnlocked(9, true);   // ending theme is now unlocked in the music room
        PreviousSwitch = Gfx.GetTime();
        Elements = new List<EndingElement>();
        Elements.AddRange(info.AddTexts);
        Elements.AddRange(info.ClearTexts);
        Elements.AddRange(info.PictureSwitchers);
        Elements = Elements.OrderBy(x => x.ID).ToList();
        Y = Runtime.CurrentRuntime.Height / 4 * 3;
    }
    
    public int Index = -1;
    
    public override void Render()
    {
        double time = Gfx.GetTime();
        byte transparency = Helper.TimeToTransparency(
                Helper.ComputeObjectTime(time,
                    PreviousSwitchBackground[0],
                    0.5,
                    PreviousSwitchBackground[1],
                    0.5)
            );
        byte tp = Helper.TimeToTransparency(
            1-Helper.ComputeObjectTime(
            time,
            TimeAppear,
            1,
            TimeDisappear,
            1)
        );
        DrawRectangle(0,0,Runtime.CurrentRuntime.Width,Runtime.CurrentRuntime.Height, Rgba.Black);
        DrawTexture(Runtime.CurrentRuntime.Textures["ending_background.png"], 0, 0, Rgba.White);
        // Crossfade between slides: the two Background slots alternate as pictures switch, so the one set more
        // recently is the incoming image. The slides are illustrations with large transparent areas, so the
        // outgoing one has to fade *out* over the same window the incoming one fades in — leaving it at full
        // opacity would let every picture shown so far keep showing through the current one.
        float bgScale = Runtime.CurrentRuntime.ScaleF / 4f;
        int newer = PreviousSwitchBackground[1] >= PreviousSwitchBackground[0] ? 1 : 0;
        int older = 1 - newer;
        float incomingA = (float)Math.Clamp((time - PreviousSwitchBackground[newer]) / BackgroundFadeDuration, 0, 1);
        if (incomingA < 1f)
            DrawTextureEx(Backgrounds[older], Vector2.Zero, 0, bgScale,
                Rgba.White with { A = (byte)(OutgoingAlpha * (1f - incomingA) * 255) });
        DrawTextureEx(Backgrounds[newer], Vector2.Zero, 0, bgScale, Rgba.White with { A = (byte)(incomingA * 255) });
        for (int i = 0; i < 4; i++)
        {
            if (RuntimeTexts[i] == null)
                break;
            DrawTexturePro(
                    RuntimeTexts[i].Texture,
                    RuntimeTexts[i].Source,
                    RuntimeTexts[i].Destination,
                    Vector2.Zero, 0, Rgba.White
                );
        }
        DrawRectangle(0,0,Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height, Rgba.Black with {A = tp});
        base.Render();
    }

    public void SwitchPicture(BasicTexture image)
    {
        // The slot we are about to overwrite is the one that has already settled; the *other* slot holds the
        // picture currently on screen and becomes the outgoing one. Snapshot how far its own fade-in got, so a
        // switch during a dissolve (mashing Enter through two picture elements) fades it out from where it is
        // instead of popping it back to full opacity.
        int onScreen = 1 - BackgroundIndex;
        OutgoingAlpha = (float)Math.Clamp(
            (Gfx.GetTime() - PreviousSwitchBackground[onScreen]) / BackgroundFadeDuration, 0, 1);
        Backgrounds[BackgroundIndex] = image;
        PreviousSwitchBackground[BackgroundIndex] = Gfx.GetTime();
        BackgroundIndex = (BackgroundIndex + 1) % 2;
    }

    public void AddText(string text)
    {
        if(RuntimeTexts[LastTextIndex] != null)
            RuntimeTexts[LastTextIndex].Dispose();
        RuntimeTexts[LastTextIndex] = new RuntimeEndingText(text, Gfx.GetTime(), Y);
        Y += RuntimeTexts[LastTextIndex].Texture.Height;
        LastTextIndex++;
    }

    public void ClearText()
    {
        foreach (var text in RuntimeTexts)
            text.Disappear();
        LastTextIndex = 0;
        Y = Runtime.CurrentRuntime.Height / 4 * 3;
    }
    
    #if DEBUG
    public override void DrawImgui()
    {
        base.DrawImgui();
        Begin("Ending Debug Info");
        Text($"Current Index: {Index}");
        Text($"Texts count: {LastTextIndex}\n\n");
        foreach (var text in RuntimeTexts)
            if(text != null)
                Text($"{Helper.Transliterate(text.Text)}");
        Text($"\nPrevious Background Switchers: {PreviousSwitchBackground[0]} {PreviousSwitchBackground[1]}");
        Text($"Previous Switch: {PreviousSwitch}");
        Text($"Background Index: {BackgroundIndex}");
        byte transparency = Helper.TimeToTransparency(
            Helper.ComputeObjectTime(Gfx.GetTime(),
                PreviousSwitchBackground[0],
                0.5,
                PreviousSwitchBackground[1],
                0.5)
        );
        Text($"Transparency: {transparency}");
        End();
    }
#endif

    private const double AutoSwitchDelay = 5;
    private const double SwitchDelay = 0.25;
    private const double BackgroundFadeDuration = 0.6;   // seconds to crossfade one ending slide into the next
    private int Y;
    private double PreviousSwitch;
    private double[] PreviousSwitchBackground = new double[2];
    private int BackgroundIndex = 0;
    private float OutgoingAlpha = 1;     // opacity the outgoing slide had when it started fading out
    private BasicTexture[] Backgrounds = new BasicTexture[2];
    RuntimeEndingText?[] RuntimeTexts = new RuntimeEndingText[4];
    private int LastTextIndex = 0;
    
    public override void TopUpdate()
    {
        base.TopUpdate();
        double time = Gfx.GetTime();
        if (TimeDisappear < Gfx.GetTime())
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
            // The ending has played out; roll into the staff roll when this clear asked for it, carrying the run
            // forward so the staff roll can hand off to the results / replay-save screens. Bridge the two with a
            // black screen carrying the rotating fifo loader: the credits screen is placed underneath and the
            // loader fades out to reveal it, so the ending doesn't cut abruptly into the staff roll.
            if (ShowStaffRoll)
            {
                Runtime.CurrentRuntime.AddScreen(new CreditsScreen(ClearedRun));
                BlackLoadingScreen? loader = null;
                loader = new BlackLoadingScreen(1.8, 0.5,
                    () => Runtime.CurrentRuntime.RemoveScreen(loader!), true, 0.3);
                Runtime.CurrentRuntime.AddScreen(loader);
            }
        }
        if (time - PreviousSwitch > AutoSwitchDelay)
        {
            NextIndex();
            return;
        }
        if ((IsKeyDown(KeyCode.Enter)  || Controller.IsButtonDown(Configuration.Config.ShootButton) || IsKeyDown(KeyCode.Z))&&time - PreviousSwitch > SwitchDelay)
        {
            PreviousSwitch = time;
            NextIndex();
        }
    }

    void NextIndex()
    {
        if (TimeDisappear < Gfx.GetTime() + 0.5)
            return;
        Index++;
        if (Elements.Count <= Index)
        {
            TimeDisappear = (float)(Gfx.GetTime() + 0.5);
            return;
        }
        var element = Elements[Index];
        PreviousSwitch = Gfx.GetTime();
        element.Apply(this);
    }

    class RuntimeEndingText : IDisposable
    {
        public string Text;
        public int X, Y;
        private double TimeAppear;
        private double TimeDisappear;
        private RenderedTexture RenderTexture;
        
        public RuntimeEndingText(string text, double time, int y)
        {
            Y = y;
            Text = Helper.Transliterate(text);
            TimeAppear = time;
            TimeDisappear = float.MaxValue;
            RenderTexture = Helper.DrawTextScaled(Helper.Transliterate(text), 16, 4, 4, 1, Runtime.CurrentRuntime.Fonts["newsreader"]);
            X = (Runtime.CurrentRuntime.Width - Texture.Width) / 2;
            Source = Helper.GetFullSource(RenderTexture.Texture);
            
        }
        
        public void Disappear()
        {
            TimeDisappear = Gfx.GetTime() + 0.5; 
        }

        public BasicTexture Texture => RenderTexture.Texture;
        public Rect Source;
        public Rect Destination => new Rect(X, Y, Source.Width, (float)(Source.Height * Helper.ComputeObjectTime(Gfx.GetTime(), TimeAppear, 0.5, TimeDisappear + 0.5, 0.5)));
        
        public void Dispose()
        {
            UnloadRenderTexture(RenderTexture);
        }
    }
}
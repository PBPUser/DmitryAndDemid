using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using Gtk;
using Raylib_cs;
using static Raylib_cs.Raylib;
using static ImGuiNET.ImGui;

namespace DmitryAndDemid.Screens;

public class EndingScreen : Screen
{
    private List<EndingElement> Elements;
    private bool ShowStaffRoll = false;
    private int Difficulty = 0;
    
    
    public EndingScreen(int difficulty, EndingInfo info, bool showStaffRoll)
    {
        Difficulty = difficulty;
        ShowStaffRoll = showStaffRoll;
        PreviousSwitch = Raylib.GetTime();
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
        double time = Raylib.GetTime();
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
        DrawRectangle(0,0,Runtime.CurrentRuntime.Width,Runtime.CurrentRuntime.Height, Color.Black);
        DrawTextureEx(Runtime.CurrentRuntime.Textures["ending_background.png"], Vector2.Zero, 0,
            Runtime.CurrentRuntime.ScaleF / 4, Color.White);
        DrawTextureEx(Backgrounds[0], Vector2.Zero, 0, Runtime.CurrentRuntime.ScaleF / 4f, Color.White with { A = 255 });
        DrawTextureEx(Backgrounds[1], Vector2.Zero, 0, Runtime.CurrentRuntime.ScaleF / 4f, Color.White with { A = 255 });
        for (int i = 0; i < 4; i++)
        {
            if (RuntimeTexts[i] == null)
                break;
            DrawTexturePro(
                    RuntimeTexts[i].Texture,
                    RuntimeTexts[i].Source,
                    RuntimeTexts[i].Destination,
                    Vector2.Zero, 0, Color.White
                );
        }
        DrawRectangle(0,0,Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height, Color.Black with {A = tp});
        base.Render();
    }

    public void SwitchPicture(Texture2D image)
    {
        Backgrounds[BackgroundIndex] = image;
        PreviousSwitchBackground[BackgroundIndex] = Raylib.GetTime();
        BackgroundIndex = (BackgroundIndex + 1) % 2;
    }

    public void AddText(string text)
    {
        if(RuntimeTexts[LastTextIndex] != null)
            RuntimeTexts[LastTextIndex].Dispose();
        RuntimeTexts[LastTextIndex] = new RuntimeEndingText(text, Raylib.GetTime(), Y);
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
            Helper.ComputeObjectTime(Raylib.GetTime(),
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
    private int Y;
    private double PreviousSwitch;
    private double[] PreviousSwitchBackground = new double[2];
    private int BackgroundIndex = 0;
    private Texture2D[] Backgrounds = new Texture2D[2];
    RuntimeEndingText?[] RuntimeTexts = new RuntimeEndingText[4];
    private int LastTextIndex = 0;
    
    public override void TopUpdate()
    {
        base.TopUpdate();
        double time = Raylib.GetTime();
        if (TimeDisappear < Raylib.GetTime())
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
        }
        if (time - PreviousSwitch > AutoSwitchDelay)
        {
            NextIndex();
            return;
        }
        if ((IsKeyDown(KeyboardKey.Enter)  || Controller.IsButtonDown(Configuration.Config.ShootButton) || IsKeyDown(KeyboardKey.Z))&&time - PreviousSwitch > SwitchDelay)
        {
            PreviousSwitch = time;
            NextIndex();
        }
    }

    void NextIndex()
    {
        if (TimeDisappear < Raylib.GetTime() + 0.5)
            return;
        Index++;
        if (Elements.Count <= Index)
        {
            TimeDisappear = (float)(Raylib.GetTime() + 0.5);
            return;
        }
        var element = Elements[Index];
        PreviousSwitch = Raylib.GetTime();
        element.Apply(this);
    }

    class RuntimeEndingText : IDisposable
    {
        public string Text;
        public int X, Y;
        private double TimeAppear;
        private double TimeDisappear;
        private RenderTexture2D RenderTexture;
        
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
            TimeDisappear = Raylib.GetTime() + 0.5; 
        }

        public Texture2D Texture => RenderTexture.Texture;
        public Rectangle Source;
        public Rectangle Destination => new Rectangle(X, Y, Source.Width, (float)(Source.Height * Helper.ComputeObjectTime(Raylib.GetTime(), TimeAppear, 0.5, TimeDisappear + 0.5, 0.5)));
        
        public void Dispose()
        {
            Raylib.UnloadRenderTexture(RenderTexture);
        }
    }
}
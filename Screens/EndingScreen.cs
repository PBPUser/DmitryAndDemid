using System.Numerics;
using System.Text.Json;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using Raylib_cs;

namespace DmitryAndDemid.Screens;

public class EndingScreen : Screen
{
    private EndingInfo Info;
    private bool ShowStaffRoll = false;
    
    public EndingScreen(string id, bool showStaffRoll)
    {
        ShowStaffRoll = showStaffRoll;
        PreviousSwitch = Raylib.GetTime();
        Info = JsonSerializer.Deserialize<EndingInfo>(File.ReadAllText($"Assets/Data/Endings/{id}.json"))!;
    }
    
    public int Index = -1;
    
    public override void Render()
    {
        Raylib.DrawRectangle(0,0,Runtime.CurrentRuntime.Width,Runtime.CurrentRuntime.Height, Color.Black);
        Raylib.DrawTextureEx(Runtime.CurrentRuntime.Textures["ending_background.png"], Vector2.Zero, 0,
            Runtime.CurrentRuntime.ScaleF / 4, Color.White);
        base.Render();
    }

    public void SwitchImage(Texture2D image)
    {
        Backgrounds[BackgroundIndex] = image;
        BackgroundIndex = (BackgroundIndex + 1) % 2;
    }

    private const double AutoSwitchDelay = 5;
    private const double SwitchDelay = 0.25;
    private double PreviousSwitch;
    private int BackgroundIndex = 0;
    private Texture2D[] Backgrounds = new Texture2D[2];
    
    public override void TopUpdate()
    {
        base.TopUpdate();
        double time = Raylib.GetTime();
        if (time - PreviousSwitch > AutoSwitchDelay)
        {
            NextIndex();
            return;
        }
        if ((Raylib.IsKeyDown(KeyboardKey.Enter)  || Raylib.IsKeyDown(KeyboardKey.Z))&&time - PreviousSwitch > SwitchDelay)
        {
            PreviousSwitch = time;
            NextIndex();
        }
    }

    void NextIndex()
    {
        Index++;
        var element = Info.Elements.FirstOrDefault(x => x.Index == Index);
        if (element == null)
            return;
        TimeDisappear = (float)Raylib.GetTime() + .5f;
    }
}
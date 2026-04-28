using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Screens;

public class PauseMenu : MenuScreen
{
    private GameplayScreen GameplayScreen;
    
    public PauseMenu(GameplayScreen screen)
    {
        GameplayScreen = screen;
        CurrentY = (int)(256 * Runtime.CurrentRuntime.ScaleF);
    }

    public override void CreateMenu()
    {
        Menu["ingame.continue"] = (a, b) =>
        {
            if (GameplayScreen.Game!.ForcedPause)
                return;
            GameplayScreen.Resume();
            Runtime.CurrentRuntime.RemoveScreen(this);
        };
        Menu["ingame.save"] = (a, b) =>
        {
            
        };
        Menu["ingame.save_and_exit"] = (a, b) =>
        {
            
        };
        Menu["ingame.restart"] = (a, b) =>
        {
            
        };
        Menu["ingame.exit"] = (a, b) =>
        {
            Runtime.CurrentRuntime.RemoveScreen(this); 
            Runtime.CurrentRuntime.RemoveScreen(GameplayScreen);
        };
    }

    public override void Activated()
    {
        TimeAppear = (float)GetTime();
        TimeDisappear = float.MaxValue;
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["pause"]);
        base.Activated();
    }

    public override void Render()
    {
        float time = (float)GetTime();
        CurrentX = (int)((160f - 64f * Helper.ComputeObjectTime(time, TimeAppear, 0.25, TimeDisappear + 0.25, 0.25)) * Runtime.CurrentRuntime.ScaleF);
        DrawMenu();
    }
}
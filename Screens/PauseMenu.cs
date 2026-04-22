using DmitryAndDemid.Common;

namespace DmitryAndDemid.Screens;

public class PauseMenu : MenuScreen
{
    private GameplayScreen GameplayScreen;
    
    public PauseMenu(GameplayScreen screen)
    {
        GameplayScreen = screen;
    }

    public override void CreateMenu()
    {
        Menu["npoDoJljuTb"] = (a, b) =>
        {
            GameplayScreen.Resume();
            Runtime.CurrentRuntime.RemoveScreen(this);
        };
        Menu["BbijTu"] = (a, b) =>
        {
            Runtime.CurrentRuntime.RemoveScreen(this); 
            Runtime.CurrentRuntime.RemoveScreen(GameplayScreen);
        };
    }

    public override void Render()
    {
        DrawMenu();
    }
}
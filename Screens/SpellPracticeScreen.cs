using DmitryAndDemid.Common;
using DmitryAndDemid.Data;

namespace DmitryAndDemid.Screens;

public class SpellPracticeScreen : MenuScreen
{
    public static SpellPracticeScreen Instance = new();
    private ProtogonistData ProtogonistData;
    
    SpellPracticeScreen()
    {
        SetTitle(Runtime.CurrentRuntime.Textures["spell_practice.png"]);
    }

    public override void CreateMenu()
    {
        MenuItems.Add(new MenuItem("practice.stage", "1", i => {}));
        MenuItems.Add(new MenuItem("practice.stage", "2", i => {}));
        MenuItems.Add(new MenuItem("practice.stage", "3", i => {}));
        MenuItems.Add(new MenuItem("practice.stage", "ex", i => {}));
        base.CreateMenu();
    }

    public override void Render()
    {
        CurrentX = (int)(Runtime.CurrentRuntime.ScaleF * 410);
        CurrentY = (int)(Runtime.CurrentRuntime.ScaleF * 160);
        this.DrawBackground();
        DrawMenu();
        DrawTitle();
        base.Render();
    }

    public void SetProtogonistData(ProtogonistData pData)
    {
        ProtogonistData = pData;
    }
}
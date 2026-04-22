using DmitryAndDemid;
using static Raylib_cs.Raylib;
using static DmitryAndDemid.Runtime;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Common;

public class MenuScreen : Screen
{
    const double MenuSwitchCooldown = 0.125;
    public const double MenuActivateCooldown = 0.5;

    protected Dictionary<string, EventHandler<int>> Menu = new();
    protected RenderTexture2D[] MenuItems;
    protected int CurrentY = 0;
    protected int CurrentX = 0;
    protected int SelectedIndex = 0;
    protected bool AllowExitWithEscape = true;

    public MenuScreen()
    {
        CreateMenu();
        MenuItems = new RenderTexture2D[Menu.Count()];

        for (int i = 0; i < Menu.Count(); i++)
        {
            MenuItems[i] = DrawMenuItem(Menu.Keys.ElementAt(i));
        }
    }

    static RenderTexture2D DrawMenuItem(string text)
    {
        return Helper.DrawText(text, 16, 8, 8, GetFontDefault());
    }

    public virtual void CreateMenu()
    {

    }

    public static double PreviousKeyTimestamp = 0;
    EventHandler<int>? Event;

    bool ItemActivated = false;

    public override void TopUpdate()
    {
        if (ItemActivated)
        {
            if (GetTime() - PreviousKeyTimestamp < MenuActivateCooldown)
                return;
            ItemActivated = false;
            if (Event != null)
                Event.Invoke(null, 0);
        }
        if (GetTime() - PreviousKeyTimestamp < MenuSwitchCooldown)
            return;
        if (Raylib.IsKeyDown(KeyboardKey.Up))
        {
            PreviousKeyTimestamp = GetTime();
            SelectedIndex = (SelectedIndex - 1 + MenuItems.Count()) % MenuItems.Count();
        }
        else if (Raylib.IsKeyDown(KeyboardKey.Down))
        {
            PreviousKeyTimestamp = GetTime();
            SelectedIndex = (SelectedIndex + 1) % MenuItems.Count();
        }
        else if (Raylib.IsKeyDown(KeyboardKey.Enter))
        {
            PreviousKeyTimestamp = GetTime();
            ItemActivated = true;
            Event = Menu.ElementAt(SelectedIndex).Value;
        }
        else if (AllowExitWithEscape && Raylib.IsKeyDown(KeyboardKey.Escape))
        {
            Exiting();
            PreviousKeyTimestamp = GetTime();
            Event = (a, b) => Runtime.CurrentRuntime.RemoveScreen(this);
            ItemActivated = true;
        }
    }

    public virtual void Exiting()
    {
        TimeDisappear = (float)Raylib.GetTime() + 0.5f;
    }

    public void DrawMenu()
    {
        int y = CurrentY;
        int index = 0;
        foreach (var x in MenuItems)
        {

            DrawTexture(x.Texture, CurrentX, y, index == SelectedIndex ? Color.Yellow : Color.Black);
            y += x.Texture.Height;
            index++;
        }
    }
}

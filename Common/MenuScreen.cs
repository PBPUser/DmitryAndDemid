using DmitryAndDemid;
using static Raylib_cs.Raylib;
using static DmitryAndDemid.Runtime;

namespace DmitryAndDemid.Common;

public class MenuScreen : Screen
{
    public Dictionary<string, EventHandler<int>> Menu = new();

    public void DrawMenu()
    {

    }
}

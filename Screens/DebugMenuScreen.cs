#if DEBUG
using System.Text.Json;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

/// <summary>
/// The debug build's own menu, sitting above everything else on the title screen: direct ways into the parts
/// of the game that normally only appear after a full clear. Every ending, each played the way a real run
/// plays it (the ending, then the staff roll), plus the staff roll on its own — that last one is how the
/// <see cref="Backgrounds.ParisChelyabinskBackground"/> behind the roll gets looked at without finishing the
/// game first.
///
/// The whole file is inside <c>#if DEBUG</c>, so a Release build has neither the screen nor the menu entry
/// that opens it (see <see cref="MainScreen.CreateMenu"/>).
/// </summary>
public class DebugMenuScreen : MenuScreen
{
    public DebugMenuScreen()
    {
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
    }

    public override void CreateMenu()
    {
        // Built from the ending files on disk rather than a hard-coded list, so an ending added later shows up
        // here without anyone remembering to add a row.
        foreach (string path in Assets.Files("Assets/Data/Endings", "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            MenuItems.Add(new MenuItem(name, "", _ => OpenEnding(path)));
        }

        MenuItems.Add(new MenuItem("staff roll", "", _ =>
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["button"]);
            Runtime.CurrentRuntime.AddScreen(new CreditsScreen());
        }));

        // No quit row: Escape / X leaves through the base handler, like the spell-practice screens.
        base.CreateMenu();
    }

    /// <summary>
    /// Plays one ending the way a cleared run would — the ending itself, then the staff roll after it — so the
    /// hand-off between the two is what is being tested and not just the slides. A malformed ending file is
    /// reported on the console and skipped rather than taking the game down.
    /// </summary>
    private static void OpenEnding(string path)
    {
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["button"]);
        try
        {
            EndingInfo? ending = JsonSerializer.Deserialize<EndingInfo>(Assets.ReadAllText(path));
            if (ending == null)
            {
                Console.WriteLine($"[debug menu] {path} deserialized to null");
                return;
            }
            Runtime.CurrentRuntime.AddScreen(new EndingScreen(0, ending, showStaffRoll: true));
        }
        catch (Exception e)
        {
            Console.WriteLine($"[debug menu] {path} failed to load: {e.Message}");
        }
    }

    public override void Render()
    {
        CurrentX = (int)(Runtime.CurrentRuntime.ScaleF * 80);
        CurrentY = (int)(Runtime.CurrentRuntime.ScaleF * 120);
        DrawBackground();
        DrawMenu();
        base.Render();
    }
}
#endif

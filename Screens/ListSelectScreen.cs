using DmitryAndDemid.Common;
using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

/// <summary>
/// A plain "pick one from a list" menu. Used for the settings that are a discrete choice rather than a value
/// to nudge — the resolution and the renderer — where a full list reads better than cycling a single row.
/// Picking an option runs its callback and closes the list.
/// </summary>
public class ListSelectScreen : MenuScreen
{
    private readonly TextureHandle TitleTexture;
    private readonly (string Label, System.Action OnSelect)[] Options;

    public ListSelectScreen(TextureHandle title, IEnumerable<(string Label, System.Action OnSelect)> options)
    {
        TitleTexture = title;
        Options = options.ToArray();
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
    }

    public override void CreateMenu()
    {
        SetTitle(TitleTexture);
        foreach ((string label, System.Action onSelect) in Options)
        {
            System.Action select = onSelect;
            MenuItems.Add(new MenuItem(label, "", _ =>
            {
                select();
                Exit();
            }));
        }
        MenuItems.Add(new MenuItem("ingame.exit", "", _ => Exit()));
        CurrentX = (int)(Runtime.CurrentRuntime.Scale * 40);
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 176);
    }

    public override void Render()
    {
        DrawBackground();
        DrawMenu();
        DrawTitle();
    }
}

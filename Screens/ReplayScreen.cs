using System.Numerics;
using System.Text.Json;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

/// <summary>
/// The replay viewer, opened from the main menu (title: replays.png). Lists the saved .rpy files, 25 per page
/// (Left/Right turn pages), and on selection launches gameplay driven by a <see cref="ReplayController"/>
/// instead of live input — the same GameplayScreen the real game uses, only with the controller swapped.
/// There is no exit entry; Escape/back leaves.
/// </summary>
public class ReplayScreen : MenuScreen
{
    private const int PerPage = 25;
    private const float ItemPadding = 1f;
    private float ItemFontSize = 10f;   // sized in CreateMenu so all PerPage rows fit the page
    private string[] AllReplays = [];
    private int Page;

    public override void Activated()
    {
        SelectedIndex = 0;
        base.Activated();
    }

    public override void CreateMenu()
    {
        SetTitle(Runtime.CurrentRuntime.Textures["replays.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        AllReplays = FindReplays();
        CurrentX = (int)(Runtime.CurrentRuntime.Scale * 32);
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 138);   // just below the title
        // Size the monospace rows so exactly PerPage (25) fit between the title and the bottom page indicator,
        // whatever the resolution. texture height ≈ (fontSize + 2*padding); the -1 keeps a little breathing room.
        float sf = Runtime.CurrentRuntime.ScaleF;
        float bottom = Runtime.CurrentRuntime.Height - 34 * sf;
        float rowPx = MathF.Max(1f, (bottom - CurrentY) / PerPage);
        ItemFontSize = Math.Clamp(rowPx / sf - 2 * ItemPadding - 1f, 6f, 13f);
        BuildPage();
    }

    /// <summary>Enough pages to hold every replay (25 each); at least one so an empty list still shows.</summary>
    private int PageCount => Math.Max(1, (AllReplays.Length + PerPage - 1) / PerPage);

    private void BuildPage()
    {
        foreach (MenuItem old in MenuItems)
            old.Dispose();
        MenuItems.Clear();

        int start = Page * PerPage;
        int end = Math.Min(start + PerPage, AllReplays.Length);
        for (int i = start; i < end; i++)
        {
            string path = AllReplays[i];
            Replay.ReplayJson? header;
            try { header = Replay.ReadHeader(path); }
            catch { continue; }
            if (header == null)
                continue;
            MenuItems.Add(new MenuItem(Label(header), "", a => Launch(path))
                { FontKey = "kodemono", FontSize = ItemFontSize, Padding = ItemPadding });
        }

        if (MenuItems.Count == 0)
            MenuItems.Add(new MenuItem("replay.empty", "", a => { })
                { Enabled = false, FontKey = "kodemono", FontSize = ItemFontSize, Padding = ItemPadding });

        SelectedIndex = 0;
    }

    public override void TopUpdate()
    {
        // Left/Right turn pages (only when there is more than one). Up/Down/Enter/Escape stay with the base.
        if (PageCount > 1 && GetTime() - PreviousKeyTimestamp > MenuSwitchCooldown)
        {
            int direction = 0;
            if (IsKeyDown(KeyCode.Left) || Controller.IsButtonDown(PadButton.LeftFaceLeft))
                direction = -1;
            else if (IsKeyDown(KeyCode.Right) || Controller.IsButtonDown(PadButton.LeftFaceRight))
                direction = 1;

            if (direction != 0)
            {
                Page = (Page + direction + PageCount) % PageCount;
                BuildPage();
                PreviousKeyTimestamp = GetTime();
                Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
                return;
            }
        }
        base.TopUpdate();
    }

    private static string[] FindReplays() => ReplayLauncher.FindReplays();

    private static string Label(Replay.ReplayJson header)
    {
        string difficulty = header.Difficulty >= 0 && header.Difficulty < Helper.DifficultyIds.Length
            ? Helper.DifficultyIds[header.Difficulty]
            : header.Difficulty.ToString();
        string nickname = string.IsNullOrWhiteSpace(header.Nickname) ? "----" : header.Nickname;
        return $"{nickname}  {header.Person}  {difficulty}  {header.Timestamp:yy/MM/dd}";
    }

    public override void Render()
    {
        DrawBackground();
        DrawMenu();
        DrawTitle();

        if (PageCount > 1)
        {
            FontHandle font = Runtime.CurrentRuntime.Fonts["kodemono"];
            string label = $"<  {Page + 1} / {PageCount}  >";
            float size = 18 * Runtime.CurrentRuntime.ScaleF;
            Vector2 measure = MeasureTextEx(font, label, size, 1);
            DrawTextEx(font, label,
                new Vector2((Runtime.CurrentRuntime.Width - measure.X) / 2f,
                    Runtime.CurrentRuntime.Height - measure.Y - 20 * Runtime.CurrentRuntime.ScaleF),
                size, 1, Rgba.White);
        }
    }

    private void Launch(string path)
    {
        GameplayScreen? screen = ReplayLauncher.Build(path, demo: false);
        if (screen != null)
            Runtime.CurrentRuntime.AddScreen(screen);
    }
}

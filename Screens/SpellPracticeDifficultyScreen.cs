using DmitryAndDemid.Rendering;
using System.Numerics;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

/// <summary>
/// The difficulty step of spell practice. Every tier (Easy..Max) is listed by the card's NAME at that
/// difficulty (small font, above), with its records below: spell-practice tries, then practice+main-game/extra
/// tries and the max score. A tier the card does not modify shows a dimmed "nothing" placeholder. Semi-
/// transparent overlay; no background of its own.
/// </summary>
public class SpellPracticeDifficultyScreen : MenuScreen
{
    private readonly FileStageInfo Stage;
    private readonly ProtogonistData Protogonist;
    private readonly int ChapterIndex;
    private readonly int Number;
    private readonly FileChapterInfo Chapter;

    /// <summary>The four playable tiers, in order (Easy, Normal, Hard, Max). Extra is a separate mode.</summary>
    private const int TierCount = 4;
    private readonly bool[] Available = new bool[TierCount];

    public SpellPracticeDifficultyScreen(FileStageInfo stage, ProtogonistData protogonist,
        int chapterIndex, int number)
    {
        Stage = stage;
        Protogonist = protogonist;
        ChapterIndex = chapterIndex;
        Number = number;
        Chapter = stage.Chapters[chapterIndex];
        SetTitle(Runtime.CurrentRuntime.Textures["rang_select.png"]);
    }

    public override void CreateMenu()
    {
        bool anyDefined = false;
        for (int d = 0; d < TierCount; d++)
            if (Helper.SpellcardDifficultyName(Number, d) != null)
                anyDefined = true;

        for (int d = 0; d < TierCount; d++)
        {
            string? name = Helper.SpellcardDifficultyName(Number, d);
            if (name == null && !anyDefined)   // un-migrated card: offer every tier under its base title
                name = Helper.HasTranslation(Chapter.SpellcardTitle)
                    ? Helper.TranslateRaw(Chapter.SpellcardTitle)
                    : Chapter.SpellcardTitle;

            int difficulty = d;
            if (name != null)
            {
                Available[d] = true;
                MenuItems.Add(new MenuItem(name, "", _ => Start(difficulty)) { FontSize = 11f, Padding = 20f });
            }
            else
            {
                Available[d] = false;
                MenuItems.Add(new MenuItem("nothing", "", null) { Enabled = false, FontSize = 11f, Padding = 20f });
            }
        }
        MenuItems.Add(new MenuItem("ingame.exit", "", _ => Exit()) { FontSize = 12f });
    }

    private void Start(int difficulty)
    {
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["swap"]);
        Runtime.CurrentRuntime.AddScreen(new GameplayScreen(Protogonist, difficulty, [Stage], ChapterIndex, true,
            mode: GameType.SpellPractice));

        TiledLoadingScreen? loading = null;
        loading = new TiledLoadingScreen(3, 0.5, () => Runtime.CurrentRuntime.RemoveScreen(loading!), true, 0);
        Runtime.CurrentRuntime.AddScreen(loading);
    }

    private static string Rec(int total, int success) =>
        success > 99 ? "99+" : $"{success:00}/{(total > 99 ? "99+" : $"{total:00}")}";

    public override void Render()
    {
        CurrentX = (int)(Runtime.CurrentRuntime.ScaleF * 40);
        CurrentY = (int)(Runtime.CurrentRuntime.ScaleF * 150);
        // Semi-transparent: dim the card-select screen still rendering beneath, so this reads as a modal.
        DrawRectangle(0, 0, Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height, new Rgba(0, 0, 0, 150));
        DrawMenu();
        DrawTitle();

        // Under each tier's name: spell-practice tries, then practice+main-game/extra tries and the max score.
        var font = Runtime.CurrentRuntime.Fonts["newsreader"];
        float sf = Runtime.CurrentRuntime.ScaleF;
        float small = 8 * sf;
        for (int d = 0; d < TierCount; d++)
        {
            if (!Available[d])
                continue;
            Rect b = ItemBounds(d);   // menu item d maps 1:1 to tier d
            if (b.Width <= 0)
                continue;
            (int tp, int sp) = PlayerData.Instance.GetSpellcardRecord(
                Protogonist.ID, Helper.SpellRecordKey(Chapter.SpellcardTitle, d), true);
            (int tg, int sg) = PlayerData.Instance.GetSpellcardRecord(
                Protogonist.ID, Helper.SpellRecordKey(Chapter.SpellcardTitle, d), false);
            float ty = b.Y + b.Height * 0.5f;
            DrawTextEx(font, $"practice  {Rec(tp, sp)}", new Vector2(b.X, ty), small, 1, Rgba.White with { A = 200 });
            DrawTextEx(font, $"game  {Rec(tg, sg)}    max {Chapter.Header[4]}",
                new Vector2(b.X, ty + small + 2 * sf), small, 1, Rgba.White with { A = 200 });
        }

        base.Render();
    }
}

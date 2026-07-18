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
    /// <summary>Baked one-line record texture (number + hi-score + attempts) per available tier, light-blue with a
    /// subtle vertical gradient. Baked once in <see cref="CreateMenu"/> since records don't change while viewing.</summary>
    private readonly TargetHandle[] InfoText = new TargetHandle[TierCount];

    /// <summary>How many numbers each spell card reserves in the global per-tier numbering (see CreateMenu).</summary>
    private const int NumbersPerCard = 6;
    private static readonly Rgba LightBlue = new(150, 205, 255);

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
        // No explicit quit entry — Escape / X (or Back on Android) leaves the screen via the base handler.
        BakeInfoLines();
    }

    /// <summary>
    /// Bakes each available tier's single record line: its GLOBAL number, hi-score and attempts. The number
    /// counts continuously from the first tier of the first spell card, <see cref="NumbersPerCard"/> per card —
    /// so card 0 is 1..4 (Easy..Max) and card 1's Hard is 1*6 + 2 + 1 = 9. Light-blue with a subtle top-to-bottom
    /// gradient (via the text-gradient shader).
    /// </summary>
    private void BakeInfoLines()
    {
        var font = Runtime.CurrentRuntime.Fonts["newsreader"];
        float sf = Runtime.CurrentRuntime.ScaleF;
        float size = 9 * sf;
        for (int d = 0; d < TierCount; d++)
        {
            if (!Available[d])
                continue;
            string key = Helper.SpellRecordKey(Chapter.SpellcardTitle, d);
            (int total, int success) = PlayerData.Instance.GetSpellcardRecord(Protogonist.ID, key, true);
            int best = PlayerData.Instance.GetSpellcardBestScore(Protogonist.ID, key);
            int num = Number * NumbersPerCard + d + 1;
            Helper.DrawTextGradient(out InfoText[d], font, size,
                $"{num}     hi {best}     {Rec(total, success)}", LightBlue, 2 * sf);
        }
    }

    public override void Unload()
    {
        for (int d = 0; d < TierCount; d++)
            if (Available[d])
                UnloadRenderTexture(InfoText[d]);
        base.Unload();
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

        // One record line per tier, sitting just under its name inside the item's own bounds (so it never
        // collides with the neighbouring tiers the way the old two-line block did).
        float sf = Runtime.CurrentRuntime.ScaleF;
        for (int d = 0; d < TierCount; d++)
        {
            if (!Available[d])
                continue;
            Rect b = ItemBounds(d);   // menu item d maps 1:1 to tier d
            if (b.Width <= 0)
                continue;
            TextureHandle t = InfoText[d].Texture;
            DrawTexture(t, (int)(b.X), (int)(b.Y + b.Height - t.Height - 2 * sf), Rgba.White);
        }

        base.Render();
    }
}

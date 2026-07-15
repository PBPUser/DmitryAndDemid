using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

/// <summary>
/// Lists the spell cards in a stage, each with the player's record on it and the card's maximum bonus:
///
///     &lt;card name&gt;   12/34   150000
///                    ^ good/total  ^ max score
///
/// Same formatting rules as the in-game score line: more than 99 successes prints spell.master instead of the
/// pair, more than 99 attempts prints "99+".
///
/// Picking a card starts a practice run at THAT chapter — which is why GameBox.LoadStage had to be taught to
/// honour its `chapter` argument; it accepted one and always started from chapter 0 regardless.
/// </summary>
public class SpellPracticeCardSelect : MenuScreen
{
    private readonly FileStageInfo Stage;
    private readonly ProtogonistData Protogonist;
    private readonly int Difficulty;

    public SpellPracticeCardSelect(FileStageInfo stage, ProtogonistData protogonist, int difficulty)
    {
        Stage = stage;
        Protogonist = protogonist;
        Difficulty = difficulty;

        SetTitle(Runtime.CurrentRuntime.Textures["spell_practice.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
    }

    public override void CreateMenu()
    {
        for (int i = 0; i < Stage.Chapters.Length; i++)
        {
            FileChapterInfo chapter = Stage.Chapters[i];
            if ((ChapterType)chapter.Header[0] != ChapterType.Spell)
                continue;

            int index = i;   // captured per row, not the loop variable
            MenuItems.Add(new MenuItem(BuildLabel(chapter), "", _ => Start(index)));
        }

        MenuItems.Add(new MenuItem("ingame.exit", "", _ => Exit()));
    }

    /// <summary>"name   good/total   maxscore", with the master / 99+ rules.</summary>
    private string BuildLabel(FileChapterInfo chapter)
    {
        (int total, int success) = GetRecord(chapter.SpellcardTitle);

        string record = success > 99
            ? Helper.Translate("spell.master")
            : $"{success:00}/{(total > 99 ? "99+" : $"{total:00}")}";

        return $"{chapter.SpellcardTitle}   {record}   {chapter.Header[4]}";
    }

    /// <summary>
    /// This character's record on a card in spell practice: (attempts, captures). Practice keeps its own
    /// counters, so a card cleared in a real run does not show up as practised here.
    /// </summary>
    private (int Total, int Success) GetRecord(string spellName) =>
        PlayerData.Instance.GetSpellcardRecord(Protogonist.ID, spellName, true);

    private void Start(int chapterIndex)
    {
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["swap"]);

        Runtime.CurrentRuntime.AddScreen(new GameplayScreen(Protogonist, Difficulty, [Stage], chapterIndex, true));

        TiledLoadingScreen? loading = null;
        loading = new TiledLoadingScreen(3, 0.5, () => Runtime.CurrentRuntime.RemoveScreen(loading!), true, 0);
        Runtime.CurrentRuntime.AddScreen(loading);
    }

    public override void Render()
    {
        CurrentX = (int)(Runtime.CurrentRuntime.ScaleF * 40);
        CurrentY = (int)(Runtime.CurrentRuntime.ScaleF * 160);
        DrawBackground();
        DrawMenu();
        DrawTitle();
        base.Render();
    }
}

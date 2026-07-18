using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

/// <summary>
/// Lists the spell cards in a stage by NUMBER only — "Spellcard N", where N is a 0-based number that runs
/// continuously across the whole game (the base offset is the number of spell cards in the earlier stages).
/// The real names, records and max score belong to the difficulty screen that opens after a card is picked.
/// </summary>
public class SpellPracticeCardSelect : MenuScreen
{
    private readonly FileStageInfo Stage;
    private readonly ProtogonistData Protogonist;
    private readonly int GlobalBase;

    public SpellPracticeCardSelect(FileStageInfo stage, ProtogonistData protogonist, int globalBase)
    {
        Stage = stage;
        Protogonist = protogonist;
        GlobalBase = globalBase;

        SetTitle(Runtime.CurrentRuntime.Textures["spell_practice.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
    }

    public override void CreateMenu()
    {
        EnableScrolling = true;
        MaxVisibleItems = 12;

        // Every spell card in the stage, labelled "Spellcard N" (global number). Picking one opens its
        // difficulty screen.
        int local = 0;
        for (int i = 0; i < Stage.Chapters.Length; i++)
        {
            if ((ChapterType)Stage.Chapters[i].Header[0] != ChapterType.Spell)
                continue;

            int index = i;                       // chapter index in the stage
            int number = GlobalBase + local;     // global spell-card number
            MenuItems.Add(new MenuItem("spell.card", $"{number}", _ => OpenDifficulty(index, number)));
            local++;
        }
        // No explicit quit entry — Escape / X (or Back on Android) leaves the screen via the base handler.
    }

    private void OpenDifficulty(int chapterIndex, int number)
    {
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["item-switch"]);
        Runtime.CurrentRuntime.AddScreen(
            new SpellPracticeDifficultyScreen(Stage, Protogonist, chapterIndex, number));
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

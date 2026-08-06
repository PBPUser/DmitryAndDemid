using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

public class SpellPracticeScreen : MenuScreen
{
    public static SpellPracticeScreen Instance = new();
    private ProtogonistData ProtogonistData;
    private int Difficulty;
    private string[] StageFiles = [];
    
    SpellPracticeScreen()
    {
        SetTitle(Runtime.CurrentRuntime.Textures["spell_practice.png"]);
        // Render() calls DrawBackground(), which without this was drawing an unset TextureHandle (Id 0), so the
        // screen dimmed nothing and relied on whatever was underneath it.
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
    }

    public override void CreateMenu()
    {
        // Built from the stages that actually exist, rather than four hard-coded rows whose actions did
        // nothing. Campaign stages first, THEN the extra-mode stage(s): the global spell-card numbers below are
        // cumulative over this order, and a plain alphabetical sort put "extra1.sid" in front of "stage1.sid",
        // which would have renumbered every campaign card (0..6) the moment extra1 gained cards of its own.
        StageFiles = Assets.DirectoryExists("Assets/Data/SpellCards")
            ? [..FileStageInfo.CampaignStagePaths(), ..FileStageInfo.ExtraStagePaths()]
            : [];

        for (int i = 0; i < StageFiles.Length; i++)
        {
            int index = i;
            MenuItems.Add(new MenuItem("practice.stage", $"{i + 1}", _ => OpenStage(index)));
        }

        // No explicit quit entry — Escape / X (or Back on Android) leaves the screen via the base handler.
        base.CreateMenu();
    }

    private void OpenStage(int index)
    {
        BitPackage package = BitPackage.OpenStreamReadPackage(StageFiles[index]);
        FileStageInfo stage = FileStageInfo.Load(ref package);
        package.Dispose();

        // Spell cards are numbered from zero across the WHOLE game, so pass the number of spell cards in the
        // earlier stages as this stage's base offset.
        int globalBase = 0;
        for (int s = 0; s < index; s++)
        {
            var p = BitPackage.OpenStreamReadPackage(StageFiles[s]);
            FileStageInfo st = FileStageInfo.Load(ref p);
            p.Dispose();
            globalBase += st.Chapters.Count(c => c.Header[0] == 3);   // Header[0] == 3 -> spell card
        }
        Runtime.CurrentRuntime.AddScreen(new SpellPracticeCardSelect(stage, ProtogonistData, globalBase));
    }

    public override void Render()
    {
        CurrentX = (int)(Runtime.CurrentRuntime.ScaleF * 410);
        CurrentY = (int)(Runtime.CurrentRuntime.ScaleF * 160);
        DrawBackground();
        DrawMenu();
        DrawTitle();
        base.Render();
    }

    public void SetProtogonistData(ProtogonistData pData, int difficulty)
    {
        ProtogonistData = pData;
        Difficulty = difficulty;
    }
}
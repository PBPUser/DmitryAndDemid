using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

public class PracticeScreen : MenuScreen
{
    ProtogonistData Protogonist;
    private int Difficulty;

    public PracticeScreen(ProtogonistData data, int difficulty)
    {
        Difficulty = difficulty;
        SetTitle(Runtime.CurrentRuntime.Textures["spell_practice.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        Protogonist = data;
    }

    string[] StageFiles = [];

    public override void CreateMenu()
    {
        // Run the packed .sid stages (the format the game actually loads), not the editable JSON — same source
        // the spell-practice screen uses. OpenLevel starts the whole stage from chapter 0 in practice mode.
        // Same shared, .sid-filtered, ordinally-sorted list the main story uses, so "practice stage N" and the
        // main game's stage N are guaranteed to be the very same file.
        StageFiles = FileStageInfo.CampaignStagePaths();

        for (int i = 0; i < StageFiles.Length; i++)
        {
            int index = i;
            MenuItems.Add(new MenuItem("practice.stage", $"{i + 1}", _ => OpenLevel(index)));
        }

        MenuItems.Add(new MenuItem("ingame.exit", "", _ => Exit()));
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

    void OpenLevel(int index)
    {
        BitPackage package = BitPackage.OpenStreamReadPackage(StageFiles[index]);
        FileStageInfo stage = FileStageInfo.Load(ref package);
        package.Dispose();

        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["swap"]);
        Runtime.CurrentRuntime.AddScreen(new GameplayScreen(Protogonist, Difficulty, [stage], 0, true,
            mode: GameType.Practice));

        TiledLoadingScreen? loading = null;
        loading = new TiledLoadingScreen(3, 0.5, () => Runtime.CurrentRuntime.RemoveScreen(loading!), true, 0);
        Runtime.CurrentRuntime.AddScreen(loading);
    }
}

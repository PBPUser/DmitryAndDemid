using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

/// <summary>
/// Shown when a replay is picked in the viewer: lists every campaign level. Levels the replay actually covers
/// are selectable and launch playback from that stage (with the run's resources at that point restored); levels
/// the replay does not contain render as "-------" and are disabled. Reached from <see cref="ReplayScreen"/>.
/// </summary>
public class ReplayStageSelectScreen : MenuScreen
{
    private readonly string Path;
    private readonly Replay.ReplayJson Header;

    public ReplayStageSelectScreen(string path, Replay.ReplayJson header)
    {
        Path = path;
        Header = header;
        SetTitle(Runtime.CurrentRuntime.Textures["replays_select_stage.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
    }

    public override void CreateMenu()
    {
        // Which campaign stage indices this replay recorded. Same ordinal .sid list the main game and practice
        // use, so index i here is stage i everywhere.
        var contained = new HashSet<int>();
        foreach (ReplayStageInfo info in Header.ReplayStageInfo ?? [])
            contained.Add(info.Stage);

        // ...from the list of the mode it was recorded in: one Extra stage, or the campaign's three.
        int stageCount = Header.IsExtra
            ? FileStageInfo.ExtraStagePaths().Length
            : FileStageInfo.CampaignStagePaths().Length;
        for (int i = 0; i < stageCount; i++)
        {
            int index = i;
            if (contained.Contains(i))
                MenuItems.Add(new MenuItem("practice.stage", $"{i + 1}", _ => LaunchStage(index)));
            else
                MenuItems.Add(new MenuItem("replay.locked", "", _ => { }) { Enabled = false });
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

    private void LaunchStage(int index)
    {
        GameplayScreen? screen = ReplayLauncher.Build(Path, demo: false, startStage: index);
        if (screen == null)
            return;
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["swap"]);
        Runtime.CurrentRuntime.AddScreen(screen);
        // Cover the replay's start-up with the same opening animation entering a stage uses, so playback doesn't
        // pop straight into the not-yet-ready gameplay.
        TiledLoadingScreen? loading = null;
        loading = new TiledLoadingScreen(3, 0.5, () => Runtime.CurrentRuntime.RemoveScreen(loading!), true, 0);
        loading.CaptureFrom(this);   // wipe in over the stage-select screen instead of hard-cutting to it
        Runtime.CurrentRuntime.AddScreen(loading);
        // NOTE: intentionally not Exit()ing here — the loader grabs a snapshot of this screen on its first frame,
        // so it must still be alive (not unloaded) then. It stays under the gameplay and returns after playback.
    }
}

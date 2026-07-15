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
        // nothing.
        StageFiles = Assets.DirectoryExists("Assets/Data/SpellCards")
            ? Assets.Files("Assets/Data/SpellCards", "*.sid").OrderBy(f => f).ToArray()
            : [];

        for (int i = 0; i < StageFiles.Length; i++)
        {
            int index = i;
            MenuItems.Add(new MenuItem("practice.stage", $"{i + 1}", _ => OpenStage(index)));
        }

        MenuItems.Add(new MenuItem("ingame.exit", "", _ => Exit()));
        base.CreateMenu();
    }

    private void OpenStage(int index)
    {
        BitPackage package = BitPackage.OpenStreamReadPackage(StageFiles[index]);
        FileStageInfo stage = FileStageInfo.Load(ref package);
        package.Dispose();

        Runtime.CurrentRuntime.AddScreen(new SpellPracticeCardSelect(stage, ProtogonistData, Difficulty));
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
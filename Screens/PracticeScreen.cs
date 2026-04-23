using System.Text.Json;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;

namespace DmitryAndDemid.Screens;

public class PracticeScreen : MenuScreen
{
    ProtogonistData Protogonist;

    public PracticeScreen(ProtogonistData data)
    {
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        Protogonist = data;
    }

    string[] FileNames;

    public override void CreateMenu()
    {
        FileNames = Directory.GetFiles("Assets/Data/Stages", "*.json");
        foreach (var x in FileNames)
        {
            Menu[Path.GetFileNameWithoutExtension(x)] = (a, b) => OpenLevel(x);
        }
    }

    public override void Render()
    {
        DrawBackground();
        DrawMenu();
    }

    void OpenLevel(string json)
    {
        Runtime.CurrentRuntime.RemoveScreen(this);
        Runtime.CurrentRuntime.AddScreen(new GameplayScreen(Protogonist, JsonSerializer.Deserialize<Stage>(File.ReadAllText(FileNames[SelectedIndex]))));
    }
}

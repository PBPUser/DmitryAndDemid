using System.Text.Json;
using System.Text.Json.Serialization;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using Raylib_cs;

namespace DmitryAndDemid.Screens;

public class PersonSelectScreen : MenuScreen
{
    bool IsPractice;

    public PersonSelectScreen(bool isPractice) : base()
    {
        SetBackground(Runtime.CurrentRuntime.Textures["practice_background.png"]);
        IsPractice = isPractice;
    }

    string[] Files;

    public override void CreateMenu()
    {
        Files = Directory.GetFiles("Assets/Data/PlayablePersons", "*.json");
        foreach (var x in Files)
            Menu[Path.GetFileNameWithoutExtension(x)] = (a, b) => OpenNext();
    }


    public override void Render()
    {
        DrawBackground();
        DrawMenu();
    }

    public override void Exiting()
    {
    }

    void OpenNext()
    {
        if (IsPractice)
        {
            string data = File.ReadAllText(Files[SelectedIndex]);
            try
            {
                var json = JsonSerializer.Deserialize<ProtogonistData>(data);
                if (json == null)
                    throw new Exception();
                Runtime.CurrentRuntime.AddScreen(new PracticeScreen(json));
            }
            catch (Exception ex)
            {
                Console.Write(ex.StackTrace);
            }
        }
    }
}

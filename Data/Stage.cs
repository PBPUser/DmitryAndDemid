using System.Reflection;
using System.Text.Json.Serialization;
using DmitryAndDemid.Common;
using DmitryAndDemid.Gameplay;

namespace DmitryAndDemid.Data;

public class Stage
{
    [JsonInclude] public string StageArt = "akob.png";
    [JsonInclude] public string StageBackgroundClass = "StageBackground";
    [JsonInclude]
    public ChapterInfo[] Chapters = new ChapterInfo[0];
    [JsonInclude]
    public DialogInfo[] Dialogs = new DialogInfo[0];
}

public class RuntimeStage
{
    public List<StageElement> StageElements = new();
    public StageBackground Background;
    
    public RuntimeStage(Stage stage, Game game)
    {
        Background = (StageBackground)Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(x => x.IsAssignableTo(typeof(StageBackground)))
            .Where(x => x.Name == stage.StageBackgroundClass)
            .FirstOrDefault()!.GetConstructors().Where(x => x.GetParameters().Length == 0)
            .First().Invoke(new object[0]);
        for (int i = 0; true; i++)
        {
            var chapter = stage.Chapters.Where(x => x.Index == i).FirstOrDefault();
            if (chapter != null)
            {
                StageElements.Add(new Chapter(game, chapter));
                continue;
            }
            var dialog = stage.Dialogs.Where(x => x.Index == i).FirstOrDefault();
            if (dialog != null)
                StageElements.Add(new RuntimeDialog(dialog, game.ProtogonistId));
            else
                break;
        }
    }

    public void Unload()
    {
        foreach (var x in StageElements)
            x.Unload();
    }
}

public class RuntimeDialog : StageElement
{
    public List<RuntimeDialogElement> Elements = new();

    public RuntimeDialog(DialogInfo info, string id)
    {
        Index = info.Index;
        var personDialog = info.PersonDialogs.Where(x => x.ID == id).FirstOrDefault();
        if (personDialog == null)
            throw new Exception();
        foreach (var x in personDialog.Elements)
            Elements.Add(new RuntimeDialogElement(x));
    }
}

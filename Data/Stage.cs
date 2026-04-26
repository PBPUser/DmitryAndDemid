using System.Reflection;
using System.Text.Json.Serialization;
using DmitryAndDemid.Common;
using DmitryAndDemid.Gameplay;

namespace DmitryAndDemid.Data;

public class Stage
{
    [JsonInclude] public int Index = 0;
    [JsonInclude] public int TitleTick = 0;
    [JsonInclude] public string StageArt = "akob.png";
    [JsonInclude] public string StageBackgroundClass = "StageBackground";
    [JsonInclude] public ChapterInfo[] Chapters = new ChapterInfo[0];
    [JsonInclude] public DialogInfo[] Dialogs = new DialogInfo[0];
    [JsonInclude] public BossSpawnInfo[] Bosses = new BossSpawnInfo[0];
}

public class RuntimeStage
{
    public List<StageElement> StageElements = new();
    public Dictionary<string, Boss> Bosses = new();
    public StageBackground Background;
    public int Index;
    public int TitleTick;
    
    public RuntimeStage(Stage stage, Game game)
    {
        TitleTick = stage.TitleTick;
        Index = stage.Index;
        Background = (StageBackground)Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(x => x.IsAssignableTo(typeof(StageBackground)))
            .Where(x => x.Name == stage.StageBackgroundClass)
            .FirstOrDefault()!.GetConstructors().Where(x => x.GetParameters().Length == 0)
            .First().Invoke(new object[0]);
        foreach (var bossSpawnInfo in stage.Bosses)
        {
            var boss = new Boss(game, bossSpawnInfo);
            Bosses[bossSpawnInfo.DialogElementAppearID] = boss;
        }
        for (int i = 0; true; i++)
        {
            var chapter = stage.Chapters.FirstOrDefault(x => x.Index == i);
            if (chapter != null)
            {
                StageElements.Add(new Chapter(game, chapter));
                continue;
            }
            var dialog = stage.Dialogs.FirstOrDefault(x => x.Index == i);
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

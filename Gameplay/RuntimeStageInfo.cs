using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Raylib_cs;
using Microsoft.CodeAnalysis.Scripting;

namespace DmitryAndDemid.Gameplay;

public class RuntimeStageInfo
{
    public int Index;
    public int MusicID;
    Script<object>[] Scripts;
    string[] Groups;
    public Texture2D[] Backgrounds;
    RuntimeEntityObject[] Entities;
    RuntimeChapterInfo[] Chapters;
    
    public static RuntimeStageInfo LoadFromFile(FileStageInfo stageInfo)
    {
        RuntimeStageInfo stage = new RuntimeStageInfo();
        stage.Index = stageInfo.Header[1];
        stage.MusicID = stageInfo.Header[2];
        stage.Scripts = stageInfo.Scripts.Select(x => CSharpScript.Create<object>(x)).ToArray();
        stage.Backgrounds = stageInfo.Backgrounds.Select(x => Runtime.CurrentRuntime.Textures[x]).ToArray();
        stage.Entities = stageInfo.Entities.Select(x => RuntimeEntityObject.LoadFromFile(x, ref stage.Scripts)).ToArray();
        stage.Chapters = stageInfo.Chapters.Select(x => RuntimeChapterInfo.LoadFromFile(x)).ToArray();
        return stage;
    }
}
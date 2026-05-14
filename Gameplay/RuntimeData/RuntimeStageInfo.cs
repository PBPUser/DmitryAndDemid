using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay.RuntimeData;
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
    RuntimeObject[] Entities;
    public RuntimeChapter[] Chapters;
    
    public static RuntimeStageInfo LoadFromFile(FileStageInfo stageInfo, int difficulty)
    {
        RuntimeStageInfo stage = new RuntimeStageInfo();
        stage.Index = stageInfo.Header[1];
        stage.MusicID = stageInfo.Header[2];
        stage.Scripts = stageInfo.Scripts.Select(x => CSharpScript.Create<object>(x)).ToArray();
        stage.Backgrounds = stageInfo.Backgrounds.Select(x => Runtime.CurrentRuntime.Textures[x]).ToArray();
        //stage.Entities = stageInfo.Entities.Select(x => RuntimeEntityObject.LoadFromFile(x)).ToArray();
        int tick = 0;
        stage.Chapters = new RuntimeChapter[stageInfo.Chapters.Length];
        for (int i = 0; i < stage.Chapters.Length; i++)
        {
            stage.Chapters[i] = new RuntimeChapter(stageInfo.Chapters[i], tick);
            tick += stage.Chapters[i].Length + GameBox.DelayBetweenChapters;
        }
        return stage;
    }
}
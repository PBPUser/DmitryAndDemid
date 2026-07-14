using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay.RuntimeData;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace DmitryAndDemid.Gameplay;

public class RuntimeStageInfo
{
    public int Index;
    public int MusicID;
    Script<object>[] Scripts;
    string[] Groups;
    public TextureHandle[] Backgrounds;
    public FileEntityInfo[] Entities;
    public RuntimeChapter[] Chapters;
    
    public static RuntimeStageInfo LoadFromFile(FileStageInfo stageInfo, int difficulty, GameBox box)
    {
        RuntimeStageInfo stage = new RuntimeStageInfo();
        stage.Index = stageInfo.Header[1];
        stage.MusicID = stageInfo.Header[2];
        stage.Scripts = stageInfo.Scripts.Select(x => CSharpScript.Create<object>(x)).ToArray();
        stage.Backgrounds = stageInfo.Backgrounds.Select(x => Runtime.CurrentRuntime.Textures[x]).ToArray();
        int tick = 0;
        stage.Chapters = new RuntimeChapter[stageInfo.Chapters.Length];
        stage.Entities = new FileEntityInfo[stageInfo.Entities.Length];
        foreach (var i in stageInfo.Entities)
        {
            stage.Entities[i.Header[3]] = i;
        }
        for (int i = 0; i < stage.Chapters.Length; i++)
        {
            stage.Chapters[i] = new RuntimeChapter(stageInfo.Chapters[i], tick, box);
            tick += stage.Chapters[i].Length + GameBox.DelayBetweenChapters;
        }
        return stage;
    }
}
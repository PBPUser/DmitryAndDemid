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
    public BasicTexture[] Backgrounds;
    public FileEntityInfo[] Entities;
    public RuntimeChapter[] Chapters;
    
    public static RuntimeStageInfo LoadFromFile(FileStageInfo stageInfo, int difficulty, GameBox box)
    {
        RuntimeStageInfo stage = new RuntimeStageInfo();
        stage.Index = stageInfo.Header[1];
        stage.MusicID = stageInfo.Header[2];
#if SWITCH
        // mono-nx runs the Mono interpreter with no JIT/codegen, so Roslyn's CSharpScript.Create faults on-device.
        // Shipped content drives behaviour through ActionsScope delegates (see CLAUDE.md), never these compiled
        // stage Scripts — the field is written here but never executed — so skipping the compile changes nothing
        // observable. When the SWITCH build eventually strips the Microsoft.CodeAnalysis package, the Scripts
        // field and this whole seam go with it. See docs/switch-port.md (hard constraint 6).
        stage.Scripts = [];
#else
        stage.Scripts = stageInfo.Scripts.Select(x => CSharpScript.Create<object>(x)).ToArray();
#endif
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
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Gameplay.RuntimeData;
using DmitryAndDemid.Utils;
using ImGuiNET;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Pango;
using Raylib_cs;
using static ImGuiNET.ImGui;
using Script = Pango.Script;

namespace DmitryAndDemid.Screens;

public class StageEditorScreen(FileStageInfo info, string fileName) : Screen
{
    private string[] Difficulties = ["Easy", "Normal", "Hard", "Max", "Extra"];
    private FileStageInfo Info = info;
    private int TabItem = 0;
    private string FileName = fileName;
    private static string[] ChapterTypes = typeof(ChapterType).GetEnumNames();
    private string[] Textures => Runtime.CurrentRuntime.Textures.Select(x => x.Key).ToArray();
    private string[] Shaders => Runtime.CurrentRuntime.Shaders.Select(x => x.Key).ToArray();
    private string Question = "Are you sure to delete this object?";
    private bool QuestionShown = false;
    private Action? QuestionOKAction = null;
    private Vector3 Color, DeathParticlesColor, DeathRoundsColor;
    public bool SelectMode = false;
    private int Time = 0;
    private int SelectedObjectIndex = -1;
    private int SelectedTextureIndex = -1;
    private int SelectedShaderIndex = -1;
    private int 
        SelectedCreateScriptIndex = -1,
        SelectedUpdateScriptIndex = -1,
        SelectedRemoveScriptIndex = -1,
        SelectedDieScriptIndex = -1;
    private string[] Objects => Info.Entities.Select(x => Info.Entities.IndexOf(Info.Entities).ToString()).ToArray();

    private string[] Scripts =>
        TabItem == 3 ? ActionsScope.ChapterActions.Keys.ToArray() : ActionsScope.ObjectActions.Keys.ToArray();

    private string[] Visuals => 
        Info.Entities[SelectedObjectIndex].IsBullet ?
        Runtime.CurrentRuntime.BulletVisualPresets.Keys.ToArray() :
        EntityVisual.Visuals.Keys.ToArray();
    void Save()
    {
        var s = BitPackage.OpenStreamWritePackage(FileName);
        Info.Save(ref s);
        s.Dispose();
    }

    void Reload()
    {
        if (File.Exists(FileName))
        {
            var s = BitPackage.GetStreamReadPackage(File.OpenRead(FileName));
            Info = FileStageInfo.Load(ref s);
            s.Dispose();
        }
    }
    
    #if DEBUG
    public override async void DrawImgui()
    {
        bool s = false;
        Begin("Stage Editor", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.MenuBar);
        BeginMenuBar();
        MenuItem("FPS: " + Raylib.GetFPS());
        if(MenuItem("Save"))
            Save();
        if(MenuItem("Reload"))
            Reload();
        if (MenuItem("Info"))
        {
            ShowChaptersList = true;
            TabItem = 0;
            SelectedObjectIndex = -1;
        }
        if (MenuItem("Entities"))
        {
            ShowChaptersList = true;
            SelectedObjectIndex = -1;
            TabItem = 2;
        }

        if (MenuItem("Chapters"))
        {
            ShowChaptersList = true;
            SelectedObjectIndex = -1;
            TabItem = 3;
        }
        if (MenuItem("Test"))
            TabItem = 4;
        if(MenuItem("Exit"))
            Runtime.CurrentRuntime.RemoveScreen(this);
        if (MenuItem("Reload scripts"))
        {
            ActionsScope.RebuildChapterActionsList();
            ActionsScope.RebuildObjectActionsList();
        }
        EndMenuBar();
        SetWindowPos("Stage Editor", new Vector2());
        SetWindowSize("Stage Editor", new Vector2(Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height));
        switch (TabItem)
        {
            case 0:
                InputInt("Index", ref Info.Header[1], 1, 1);
                Combo("Stage Music", ref Info.Header[2], MusicInfo.MusicNames, MusicInfo.MusicNames.Length);
                Combo("Boss Music", ref Info.Header[8], MusicInfo.MusicNames, MusicInfo.MusicNames.Length);
                break;
            case 1:
                if (ShowChaptersList)
                {
                    if (ListBox("##list_select", ref SelectedObjectIndex, Info.Scripts.Select(x => Info.Scripts.IndexOf(x)+"").ToArray(), Info.Scripts.Length, 32))
                    {
                        ShowChaptersList = false;
                    }
                    if (Button("Add Script"))
                    {
                        Array.Resize(ref Info.Scripts, Info.Scripts.Length + 1);
                        Info.Scripts[^1] = "";
                    } 
                }
                else
                {
                    if (Button("Show Script List"))
                        ShowChaptersList = true;
                    if (Button("Test"))
                    {
                        try
                        {
                            var script = """
                                         float sum = 0f;
                                         foreach(var s in Array)
                                            sum += s;
                                         return sum;
                                         """; 
                            var j = 0;
                            var globals = new ScriptTestingGlobals2 { X = 0.75f, Y = 0.5f };
                            float scale = await CSharpScript.EvaluateAsync<float>("X+Y",
                                ScriptOptions.Default
                                    .AddReferences(typeof(ScriptTestingGlobals2).Assembly),
                                globals: globals,
                                globalsType: typeof(ScriptTestingGlobals2));
                            ShowQuestion($"Ok! {scale}", () => {});
                        }
                        catch(Exception e)
                        {
                            ShowQuestion($"Failed to execute!\n{e.StackTrace}\n{e.Message}", () => {});
                        }
                    }
                    InputTextMultiline("##code", ref Info.Scripts[SelectedObjectIndex], 261144, new Vector2(800, 600));
                }
                break;
            case 2:
                if (ShowChaptersList)
                {
                    if (Button("Hide List"))
                        ShowChaptersList = false;
                    if (ListBox("##list_select", ref SelectedObjectIndex,
                            Info.Entities.Select(x => x.Header[3].ToString() + (x.IsBoss && x.IsBullet ? $"BOSS ({x.Header[7]})" : "")).ToArray(),
                            Info.Entities.Length, 32))
                    {
                        Color = Helper.ColorIntToVector3(Info.Entities[SelectedObjectIndex].Header[4]);
                        SelectedTextureIndex = -1;
                        VisualIndex = Visuals.IndexOf(Info.Entities[SelectedObjectIndex].Visual);
                        SelectedCreateScriptIndex = Scripts.IndexOf(Info.Entities[SelectedObjectIndex].CreateScript);
                        SelectedUpdateScriptIndex = Scripts.IndexOf(Info.Entities[SelectedObjectIndex].UpdateScript);
                        SelectedRemoveScriptIndex = Scripts.IndexOf(Info.Entities[SelectedObjectIndex].RemoveScript);
                        SelectedDieScriptIndex = Scripts.IndexOf(Info.Entities[SelectedObjectIndex].DieScript);
                        ShowChaptersList = false;
                        DeathRoundsColor = Helper.ColorIntToVector3(Info.Entities[SelectedObjectIndex].Header[0xB]);
                        DeathParticlesColor = Helper.ColorIntToVector3(Info.Entities[SelectedObjectIndex].Header[0xC]);
                    }

                    if (Button("Add"))
                    {
                        Array.Resize(ref Info.Entities,  Info.Entities.Length + 1);
                        Info.Entities[^1] = new FileEntityInfo();
                    }
                }
                else
                {
                    if (Button("Show List"))
                        ShowChaptersList = true;
                    Checkbox("Is Bullet", ref Info.Entities[SelectedObjectIndex].IsBullet);
                    Checkbox("Clear protected", ref Info.Entities[SelectedObjectIndex].ClearProtected);
                    Checkbox("Dangerous for player when collided", ref Info.Entities[SelectedObjectIndex].DangerousForPlayer);
                    InputInt("Spawn Id", ref Info.Entities[SelectedObjectIndex].Header[3]);
                    SliderInt("Transparency", ref Info.Entities[SelectedObjectIndex].Header[2], 0, 255);
                    if (!Info.Entities[SelectedObjectIndex].IsGroupChild &&
                        !Info.Entities[SelectedObjectIndex].IsBullet)
                        Checkbox("Group parent", ref Info.Entities[SelectedObjectIndex].IsGroupParent);
                    if(!Info.Entities[SelectedObjectIndex].IsGroupParent)
                        Checkbox("Group child", ref Info.Entities[SelectedObjectIndex].IsGroupChild);
                    if(Info.Entities[SelectedObjectIndex].IsGroupParent || Info.Entities[SelectedObjectIndex].IsGroupChild)
                        InputInt("Group Id", ref Info.Entities[SelectedObjectIndex].Header[1]);
                    if (Combo("Visual", ref VisualIndex, Visuals, Visuals.Length)) 
                        Info.Entities[SelectedObjectIndex].Visual = Visuals[VisualIndex];
                    if (Info.Entities[SelectedObjectIndex].IsBullet)
                    {
                        if (ColorPicker3("Bullet Color", ref Color))
                            Info.Entities[SelectedObjectIndex].Header[4] = Helper.Vector3ColorToInt(Color);
                        InputInt("Collectable score moddifier", ref Info.Entities[SelectedObjectIndex].Header[5]);
                    }
                    else
                    {
                        Checkbox("Use bad drop scenario", ref Info.Entities[SelectedObjectIndex].UseBadDropScenario);
                        if (Info.Entities[SelectedObjectIndex].UseBadDropScenario)
                        {
                        }
                        Checkbox("Drop when cleared", ref Info.Entities[SelectedObjectIndex].DropWhenCleared);
                        Checkbox("Is Boss", ref Info.Entities[SelectedObjectIndex].IsBoss);
                        if (Info.Entities[SelectedObjectIndex].IsBoss)
                        {
                            InputInt("Boss ID", ref Info.Entities[SelectedObjectIndex].Header[7]);
                            InputInt("Boss Health Bar Percent", ref Info.Entities[SelectedObjectIndex].Header[8]);
                            InputInt("Boss Attack Index", ref Info.Entities[SelectedObjectIndex].Header[9]);
                        }
                        InputFloat("Health",  ref Info.Entities[SelectedObjectIndex].FloatingPoints[0]);
                        InputInt("Score add when killed", ref Info.Entities[SelectedObjectIndex].Header[6]);
                        Checkbox("Override Visual's Death Color",
                            ref Info.Entities[SelectedObjectIndex].OverrideDeathColor);
                        if (Info.Entities[SelectedObjectIndex].OverrideDeathColor)
                        {
                            if (ColorEdit3("Death Particles Color", ref DeathParticlesColor))
                                Info.Entities[SelectedObjectIndex].Header[0xC] = Helper.Vector3ColorToInt(DeathParticlesColor);
                            if (ColorEdit3("Death Rounds Color", ref DeathRoundsColor))
                                Info.Entities[SelectedObjectIndex].Header[0xB] = Helper.Vector3ColorToInt(DeathRoundsColor);
                        }
                        
                    }
                    InputFloat("Appear Speed", ref Info.Entities[SelectedObjectIndex].FloatingPoints[1]);
                    InputFloat("Scaling", ref Info.Entities[SelectedObjectIndex].FloatingPoints[2]);
                    if (Combo("Update Script", ref SelectedUpdateScriptIndex, Scripts, Scripts.Length))
                        Info.Entities[SelectedObjectIndex].UpdateScript = Scripts[SelectedUpdateScriptIndex];
                    Checkbox("Use create script", ref Info.Entities[SelectedObjectIndex].UseCreateScript);
                    if(Info.Entities[SelectedObjectIndex].UseCreateScript)
                        if (Combo("Create Script", ref SelectedCreateScriptIndex, Scripts, Scripts.Length))
                            Info.Entities[SelectedObjectIndex].CreateScript = Scripts[SelectedCreateScriptIndex];
                    Checkbox("Use remove script", ref Info.Entities[SelectedObjectIndex].UseRemoveScript);
                    if(Info.Entities[SelectedObjectIndex].UseRemoveScript)
                        if (Combo("Remove Script", ref SelectedRemoveScriptIndex, Scripts, Scripts.Length))
                            Info.Entities[SelectedObjectIndex].RemoveScript = Scripts[SelectedRemoveScriptIndex];
                    if (!Info.Entities[SelectedObjectIndex].IsBullet)
                    {
                        Checkbox("Use Die Script", ref Info.Entities[SelectedObjectIndex].UseDieScript);
                        if(Info.Entities[SelectedObjectIndex].UseDieScript)
                            if (Combo("Die Script", ref SelectedDieScriptIndex, Scripts, Scripts.Length))
                                Info.Entities[SelectedObjectIndex].DieScript = Scripts[SelectedDieScriptIndex];
                    }
                }
                break;
            case 3:
                if(ShowCreate)
                    break;
                if (ShowChaptersList)
                {
                    if(Button("Create new chapter"))
                        ShowCreate = true;
                    BeginChild("List", new Vector2(Runtime.CurrentRuntime.Width - 20, Runtime.CurrentRuntime.Height - 36), ImGuiChildFlags.Borders);
                    Text("Items: ");
                    if (ListBox("##list_select", ref SelectedObjectIndex, Info.Chapters.Select(x => x.Id).ToArray(),
                            Info.Chapters.Length, Info.Chapters.Length) && !SelectMode)
                    {
                        SelectedTextureIndex = Textures.IndexOf(Info.Chapters[SelectedObjectIndex].SpellcardTexture);
                        ShowChaptersList = false;
                        SelectedCreateScriptIndex = Scripts.IndexOf(Info.Chapters[SelectedObjectIndex].CreateScript);
                        SelectedUpdateScriptIndex = Scripts.IndexOf(Info.Chapters[SelectedObjectIndex].UpdateScript);
                        RerenderBossIdentifierTexture();
                        RerenderChapterTitleTexture();
                        SelectedShaderIndex = Shaders.IndexOf(Info.Chapters[SelectedObjectIndex].SpellcardShader);
                    }
                    Checkbox("Select mode", ref SelectMode);
                    if (Button("Move Down") && SelectedObjectIndex != -1 && SelectedObjectIndex != Info.Chapters.Length - 1)
                    {                            
                        (Info.Chapters[SelectedObjectIndex], Info.Chapters[SelectedObjectIndex + 1]) =
                            (Info.Chapters[SelectedObjectIndex + 1], Info.Chapters[SelectedObjectIndex]);
                        SelectedObjectIndex += 1;
                    }

                    if (Button("Move Up"))
                    {
                        if (SelectedObjectIndex > 0)
                        {
                            (Info.Chapters[SelectedObjectIndex], Info.Chapters[SelectedObjectIndex - 1]) =
                                (Info.Chapters[SelectedObjectIndex - 1], Info.Chapters[SelectedObjectIndex]);
                            SelectedObjectIndex -= 1;
                        }
                    }

                    if (Button("Clone"))
                    {
                        var fileClone = new FileChapterInfo(Info.Chapters[SelectedObjectIndex]);
                        var list = Info.Chapters.ToList();
                        list.Insert(SelectedObjectIndex, fileClone);
                        SelectedObjectIndex += 1;
                        Info.Chapters =  list.ToArray();
                    }

                    if (Button("Delete"))
                    {
                        if(SelectedObjectIndex != -1 && Info.Chapters.Length != 0)
                            ShowQuestion("Do you want to delete this chapter?", () =>
                            {
                                var list = Info.Chapters.ToList();
                                list.RemoveAt(SelectedObjectIndex);
                                if(SelectedObjectIndex > 0 || list.Count == 1)
                                    SelectedObjectIndex -= 1;
                                Info.Chapters =  list.ToArray();
                            }, Skip);
                    }
                    Checkbox("Skip approval", ref Skip);
                    EndChild();
                }
                else
                {
                    if(Button("Show Chapters"))
                        ShowChaptersList = true;
                    InputText("Chapter Id", ref Info.Chapters[SelectedObjectIndex].Id, 255);
                    Checkbox("Use create Script", ref Info.Chapters[SelectedObjectIndex].UseCreateScript);
                    if(Info.Chapters[SelectedObjectIndex].UseCreateScript)
                        if(Combo("Script Create", ref SelectedCreateScriptIndex, Scripts, Scripts.Length))
                            Info.Chapters[SelectedObjectIndex].CreateScript = Scripts[SelectedCreateScriptIndex];
                    Checkbox("Use update Script", ref Info.Chapters[SelectedObjectIndex].UseUpdateScript);
                    if (Info.Chapters[SelectedObjectIndex].UseUpdateScript)
                        if (Combo("Script Update", ref SelectedUpdateScriptIndex, Scripts, Scripts.Length))
                            Info.Chapters[SelectedObjectIndex].UpdateScript = Scripts[SelectedUpdateScriptIndex];
                    SliderInt("Length", ref Info.Chapters[SelectedObjectIndex].Header[2], 0, 2000);
                    Combo("Difficulty", ref Info.Chapters[SelectedObjectIndex].Header[3], Difficulties, Difficulties.Length);
                    Combo("Chapter Type", ref Info.Chapters[SelectedObjectIndex].Header[0], ChapterTypes, ChapterTypes.Length);
                    
                    if (Info.Chapters[SelectedObjectIndex].Header[0] > 1)
                    {
                        if (InputText("Boss Identifier", ref Info.Chapters[SelectedObjectIndex].BossName, 255))
                            RerenderBossIdentifierTexture();
                        if(BossTexturePreview != null)
                            rlImGui_cs.rlImGui.Image(BossTexturePreview.Value.Texture);

                    }
                    if (Info.Chapters[SelectedObjectIndex].Header[0] == 3)
                    {
                        if(Combo("Background", ref SelectedTextureIndex, Textures, Textures.Length))
                            Info.Chapters[SelectedObjectIndex].SpellcardTexture =  Textures[SelectedTextureIndex];
                        Checkbox("Apply Shader", ref Info.Chapters[SelectedObjectIndex].ApplyShader);
                        if (Info.Chapters[SelectedObjectIndex].ApplyShader)
                        {
                            if(Combo("Select shader", ref SelectedShaderIndex, Shaders, Shaders.Length))
                                Info.Chapters[SelectedObjectIndex].SpellcardShader = Shaders[SelectedShaderIndex];
                        }
                        if(InputText("SpellCard name", ref Info.Chapters[SelectedObjectIndex].SpellcardTitle, 255))
                            RerenderChapterTitleTexture();
                        if(ChapterTexturePreview != null)
                            rlImGui_cs.rlImGui.Image(ChapterTexturePreview.Value.Texture);
                        InputInt("Bonus max score", ref Info.Header[6], 1000, 10000);
                        InputInt("Spell Card Index on practice menu", ref Info.Header[9], 1, 1);
                        Checkbox("Boss Invincible", ref  Info.Chapters[SelectedObjectIndex].BossInvincible);
                        Checkbox("Timeout card", ref  Info.Chapters[SelectedObjectIndex].TimeoutCard);
                    }
                    else
                    {
                        Checkbox("Has dialogs", ref Info.Chapters[SelectedObjectIndex].HasDialogs);
                        if (Info.Chapters[SelectedObjectIndex].HasDialogs)
                        {
                            if (Button("Add"))
                            {
                                Array.Resize(ref Info.Chapters[SelectedObjectIndex].Dialogs, Info.Chapters[SelectedObjectIndex].Dialogs.Length + 1);
                                Info.Chapters[SelectedObjectIndex].Dialogs[^1] = new FileDialogInfo();
                                SelectedTextureIndex = Info.Chapters[SelectedObjectIndex].Dialogs.Length-1;
                            }
                            if (Button("Move Down"))
                            {
                                if (SelectedTextureIndex == Info.Chapters[SelectedObjectIndex].Dialogs.Length - 1)
                                    return;
                                (Info.Chapters[SelectedObjectIndex].Dialogs[SelectedTextureIndex], Info.Chapters[SelectedObjectIndex].Dialogs[SelectedTextureIndex+1]) = 
                                    (Info.Chapters[SelectedObjectIndex].Dialogs[SelectedTextureIndex+1], Info.Chapters[SelectedObjectIndex].Dialogs[SelectedTextureIndex]);
                            }
                            if (Button("Move Up"))
                            {
                                if (SelectedTextureIndex < 1)
                                    return;
                                (Info.Chapters[SelectedObjectIndex].Dialogs[SelectedTextureIndex], Info.Chapters[SelectedObjectIndex].Dialogs[SelectedTextureIndex-1]) = 
                                    (Info.Chapters[SelectedObjectIndex].Dialogs[SelectedTextureIndex-1], Info.Chapters[SelectedObjectIndex].Dialogs[SelectedTextureIndex]);
                            }

                            ListBox("List", ref SelectedTextureIndex,
                                Info.Chapters[SelectedObjectIndex].Dialogs.Select(x => x.Text.Split("\n").Last())
                                    .Select(x => x.Length > 16 ? x.Substring(0,16)+"..." : x).ToArray(), Info.Chapters[SelectedObjectIndex].Dialogs.Length, 32);
                            if (SelectedTextureIndex != -1)
                            {
                                if (Button("Remove"))
                                    ShowQuestion("Do you want to remove this dialog?", () =>
                                    {
                                        var nArray = new FileDialogInfo[Info.Chapters[SelectedObjectIndex].Dialogs.Length - 1];
                                        Array.Copy(Info.Chapters[SelectedObjectIndex].Dialogs, 0,  nArray, 0, SelectedTextureIndex);
                                        Array.Copy(Info.Chapters[SelectedObjectIndex].Dialogs, SelectedTextureIndex+1,  nArray, SelectedTextureIndex, Info.Chapters[SelectedObjectIndex].Dialogs.Length-SelectedTextureIndex-1);
                                        Info.Chapters[SelectedObjectIndex].Dialogs = nArray;
                                        SelectedTextureIndex = Math.Clamp(SelectedTextureIndex, -1,
                                            Info.Chapters[SelectedObjectIndex].Dialogs.Length - 1);
                                    }, Skip);
                                InputTextMultiline("Dialog Text",
                                    ref Info.Chapters[SelectedObjectIndex].Dialogs[SelectedTextureIndex].Text, 65536,
                                    new Vector2(640, 480));
                                
                            }
                        }
                        
                    }
                }
                break;
            default:
                Text("Not implemented");
                break;
        }
        End();
        if (QuestionShown)
        {
            Begin("Question", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize);
            Text(Question);
            if (Button("Cancel"))
                QuestionShown = false;
            if (Button("OK"))
            {
                QuestionShown = false;
                QuestionOKAction?.Invoke();
            }
            End();
        }

        if (ShowCreate)
        {
            Begin("Create new chapter");
            InputText("Name", ref NewChapterName, 256, ImGuiInputTextFlags.AllowTabInput);
            if (Button("Cancel"))
                ShowCreate = false;
            if (Button("OK"))
            {
                if (string.IsNullOrEmpty(NewChapterName))
                    ShowQuestion("Please enter a name for the new chapter", () => {});
                else if (Info.Chapters.Any(x => x.SpellcardTitle.ToLower().Equals(NewChapterName.ToLower())))
                    ShowQuestion("Name of chapter should be unique.", () => {});
                else
                {
                    ShowCreate = false;
                    Array.Resize(ref Info.Chapters, Info.Chapters.Length + 1);
                    Info.Chapters[^1] = new FileChapterInfo();
                    Info.Chapters[^1].Id = NewChapterName;
                    SelectedObjectIndex = Info.Chapters.Length - 1;
                }
            }
            End();
        }
        //if (SelectedObjectIndex != -1)
        //{
        //    Begin("Object Editor");
        //    Checkbox("Is Bullet",
        //        ref EditableChapterInfo.GameObjects[SelectedObjectIndex].IsBullet);
        //    SliderInt("Position X", ref EditableChapterInfo.GameObjects[SelectedObjectIndex].IntVariables[2], -32, 416);
        //    SliderInt("Position Y", ref EditableChapterInfo.GameObjects[SelectedObjectIndex].IntVariables[3], -32, 416);
        //    SliderInt("Spawn tick", ref EditableChapterInfo.GameObjects[SelectedObjectIndex].IntVariables[8], 0,
        //        EditableChapterInfo.Header[2]);
        //    Checkbox("Dangerous For Player when Collide",
        //        ref EditableChapterInfo.GameObjects[SelectedObjectIndex].DangerousForPlayer);
        //    Checkbox("Apply Shader",
        //        ref EditableChapterInfo.GameObjects[SelectedObjectIndex].ApplyShader);
        //    Checkbox("Use Creation Script",
        //        ref EditableChapterInfo.GameObjects[SelectedObjectIndex].UseAppearScript);
        //    Checkbox("Use Removal Script",
        //        ref EditableChapterInfo.GameObjects[SelectedObjectIndex].UseDisappearScript);
        //    Checkbox("Depends on Entity",
        //        ref EditableChapterInfo.GameObjects[SelectedObjectIndex].DependsOnEntity);
        //    if (EditableChapterInfo.GameObjects[SelectedObjectIndex].DependsOnEntity)
        //    {
        //        
        //    }
        //    Text("Texture");
        //    if (Combo( "Bullet Visual", ref SelectedTextureIndex, Visuals, Visuals.Length, 32))
        //    {
        //        EditableChapterInfo.GameObjects[SelectedObjectIndex].Visual = Visuals[SelectedTextureIndex];
        //    }
//
        //    if (ColorPicker3("Texture Color", ref Color))
        //    {
        //        EditableChapterInfo.GameObjects[SelectedObjectIndex].IntVariables[14] = Helper.Vector3ColorToInt(Color);
        //    }
        //    var obj = EditableChapterInfo.GameObjects[SelectedObjectIndex];
        //    if (Visuals.Contains(obj.Visual))
        //    {
        //        if (obj.IsBullet)
        //        {
        //            var color = Helper.ColorIntToVector3(obj.IntVariables[14]);
        //            var bulletVisual = BulletVisual.Constants[obj.Visual];
        //            var size = bulletVisual.GetSourceSize();
        //            var pos = bulletVisual.GetSourcePosition(color);
        //            var max = Math.Max(size.X, size.Y);
        //            rlImGui_cs.rlImGui.ImageRect(
        //                bulletVisual.GetTexture(color),
        //                (int)(size.X * 64 / max), (int)Math.Abs(size.Y * 64 / max),
        //                new Rectangle(pos, size)
        //            );
        //        }
        //        else
        //        {
        //            var visual = EntityVisual.Visuals[obj.Visual];
        //            var size = visual.RenderSize;
        //            var pos = visual.SourcePosition;
        //            var max = Math.Max(size.X, size.Y);
        //            rlImGui_cs.rlImGui.ImageRect(
        //                Runtime.CurrentRuntime.Textures[visual.Texture],
        //                (int)(size.X * 64 / max), (int)Math.Abs(size.Y * 64 / max),
        //                new Rectangle(pos, size)
        //            );
        //        }
        //    }
        //    
        //    
        //    
        //    End();
        //    Begin("Preview");
        //    SliderInt("Time", ref Time, 0, EditableChapterInfo.Header[2]);
        //    End();
        //}
        base.DrawImgui();
    }

    public int VisualIndex = -1;

    private RenderTexture2D? BossTexturePreview = null;
    private RenderTexture2D? ChapterTexturePreview = null;
    
    private void RerenderBossIdentifierTexture()
    {
        if(BossTexturePreview != null)
            Raylib.UnloadRenderTexture(BossTexturePreview.Value);
        if (TabItem != 3 || SelectedObjectIndex == -1)
            return;
        var size = Helper.GetBossTextSize(Info.Chapters[SelectedObjectIndex].BossName);
        BossTexturePreview = Raylib.LoadRenderTexture((int)size.X, (int)size.Y);
        Helper.DrawBossText(BossTexturePreview.Value, Info.Chapters[SelectedObjectIndex].BossName);
    }

    void RerenderChapterTitleTexture()
    {
        if(ChapterTexturePreview != null)
            Raylib.UnloadRenderTexture(ChapterTexturePreview.Value);
        if (TabItem != 3 || SelectedObjectIndex == -1)
            return;
        var size = Helper.GetTitleTextSize(Info.Chapters[SelectedObjectIndex].SpellcardTitle);
        ChapterTexturePreview = Raylib.LoadRenderTexture((int)size.X, (int)size.Y);
        Helper.DrawChapterTitleText(ChapterTexturePreview.Value, Info.Chapters[SelectedObjectIndex].SpellcardTitle);
    }

    public string NewChapterName = "";

    public bool ShowCreate { get; set; }

    public bool ShowChaptersList = true;
    private bool Skip;
#endif

    void ShowQuestion(string str, Action act, bool skip = false)
    {
        if (skip)
        {
            act.Invoke();
            return;
        }
        Question = str;
        QuestionShown = true;
        QuestionOKAction = act;
    }

    public class ScriptTestingGlobals
    {
        public float X = 0;
        public float Y = 0;
    }

    public class ScriptTestingGlobals2
    {
        public float X = 0;
        public float Y = 0;
    }
}
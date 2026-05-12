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
    private GameBox GameBox = new GameBox();
    private FileStageInfo Info = info;
    //private EditableChapterInfo EditableChapterInfo = editableChapterInfo;
    private int TabItem = 0;
    private string FileName = fileName;
    private static string[] ChapterTypes = typeof(ChapterType).GetEnumNames();
    private string[] Textures => Runtime.CurrentRuntime.Textures.Select(x => x.Key).ToArray();
    private string Question = "Are you sure to delete this object?";
    private bool QuestionShown = false;
    private Action? QuestionOKAction = null;
    private Vector3 Color;
    private int Time = 0;
    private int SelectedObjectIndex = -1;
    private int SelectedTextureIndex = -1;
    private int 
        SelectedCreateScriptIndex = -1,
        SelectedUpdateScriptIndex = -1;
    private string[] Objects => Info.Entities.Select(x => Info.Entities.IndexOf(Info.Entities).ToString()).ToArray();

    private string[] Scripts =>
        TabItem == 3 ? ActionsScope.ChapterActions.Keys.ToArray() : ActionsScope.ObjectActions.Keys.ToArray();

    private string[] Visuals => 
        Info.Entities[SelectedObjectIndex].IsBullet ?
        BulletVisual.Constants.Keys.ToArray() :
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
            TabItem = 0;
        if (MenuItem("Entities"))
            TabItem = 2;
        if (MenuItem("Chapters"))
            TabItem = 3;
        if (MenuItem("Test"))
            TabItem = 4;
        if(MenuItem("Exit"))
            Runtime.CurrentRuntime.RemoveScreen(this);
        EndMenuBar();
        SetWindowPos("Stage Editor", new Vector2());
        SetWindowSize("Stage Editor", new Vector2(Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height));
        switch (TabItem)
        {
            case 0:
                InputInt("Index", ref Info.Header[1], 1, 1);
                InputInt("Music ID", ref Info.Header[2], 1, 1);
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
                            var rsi = new RuntimeScriptInformation();
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
            case 3:
                if(ShowCreate)
                    break;
                if (ShowChaptersList)
                {
                    if(Button("Create new chapter"))
                        ShowCreate = true;
                    BeginChild("List", new Vector2(Runtime.CurrentRuntime.Width - 20, Runtime.CurrentRuntime.Height - 36), ImGuiChildFlags.Borders);
                    Text("Items: ");
                    if (ListBox("##list_select", ref SelectedObjectIndex, Info.Chapters.Select(x => x.Name).ToArray(),
                            Info.Chapters.Length, Info.Chapters.Length))
                    {
                        ShowChaptersList = false;
                        SelectedCreateScriptIndex = Scripts.IndexOf(Info.Chapters[SelectedObjectIndex].CreateScript);
                        SelectedUpdateScriptIndex = Scripts.IndexOf(Info.Chapters[SelectedObjectIndex].UpdateScript);
                        RerenderBossIdentifierTexture();
                        RerenderChapterTitleTexture();
                    }
                    EndChild();
                }
                else
                {
                    if(Button("Show Chapters"))
                        ShowChaptersList = true;

                    Checkbox("Use create Script", ref Info.Chapters[SelectedObjectIndex].UseCreateScript);
                    if(Info.Chapters[SelectedObjectIndex].UseCreateScript)
                        if(Combo("Script Create", ref SelectedCreateScriptIndex, Scripts, Info.Scripts.Length))
                            Info.Chapters[SelectedObjectIndex].CreateScript = Scripts[SelectedCreateScriptIndex];
                    Checkbox("Use update Script", ref Info.Chapters[SelectedObjectIndex].UseUpdateScript);
                    if (Info.Chapters[SelectedObjectIndex].UseUpdateScript)
                        if (Combo("Script Update", ref SelectedUpdateScriptIndex, Scripts, Info.Scripts.Length))
                            Info.Chapters[SelectedObjectIndex].UpdateScript = Scripts[SelectedUpdateScriptIndex];
                    
                    SliderInt("Length", ref Info.Header[2], 0, 2000);
                    Combo("Difficulty", ref Info.Header[3], Difficulties, Difficulties.Length);
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
                        Combo("Background", ref Info.Chapters[SelectedObjectIndex].Header[5], Textures, Textures.Length);
                        if(InputText("Chapter name", ref Info.Chapters[SelectedObjectIndex].Name, 255))
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
                                    });
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
                else if (Info.Chapters.Any(x => x.Name.ToLower().Equals(NewChapterName.ToLower())))
                    ShowQuestion("Name of chapter should be unique.", () => {});
                else
                {
                    ShowCreate = false;
                    Array.Resize(ref Info.Chapters, Info.Chapters.Length + 1);
                    Info.Chapters[^1] = new FileChapterInfo();
                    Info.Chapters[^1].Name = NewChapterName;
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
        var size = Helper.GetTitleTextSize(Info.Chapters[SelectedObjectIndex].Name);
        ChapterTexturePreview = Raylib.LoadRenderTexture((int)size.X, (int)size.Y);
        Helper.DrawTitleText(ChapterTexturePreview.Value, Info.Chapters[SelectedObjectIndex].Name);
    }

    public string NewChapterName = "";

    public bool ShowCreate { get; set; }

    public bool ShowChaptersList = true;
#endif

    void ShowQuestion(string str, Action act)
    {
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
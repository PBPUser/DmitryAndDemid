using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using ImGuiNET;
using Raylib_cs;
using static ImGuiNET.ImGui;

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
    private string[] Objects => Info.Entities.Select(x => Info.Entities.IndexOf(Info.Entities).ToString()).ToArray();
    private int SelectedTextureIndex = -1;

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
    public override void DrawImgui()
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
        if (MenuItem("Scripts"))
            TabItem = 1;
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
                        ShowChaptersList = false;
                    EndChild();
                }
                else
                {
                    if(Button("Show Chapters List"))
                        ShowChaptersList = true;
                    if (Info.Header[1] >= Info.Scripts.Length)
                        Info.Header[1] = -1;
                    if (Info.Header[10] >= Info.Scripts.Length)
                        Info.Header[10] = -1;
                    Combo("Script Create", ref Info.Header[10], Info.Scripts, Info.Scripts.Length);
                    Combo("Script Update", ref Info.Header[1], Info.Scripts, Info.Scripts.Length);
                    SliderInt("Length", ref Info.Header[2], 0, 2000);
                    Combo("Difficulty", ref Info.Header[3], Difficulties, Difficulties.Length);
                    Combo("Chapter Type", ref Info.Chapters[SelectedObjectIndex].Header[0], ChapterTypes, ChapterTypes.Length);
                    if (Info.Chapters[SelectedObjectIndex].Header[0] > 1)
                    {
                        InputText("Boss Identifier" , ref Info.Chapters[SelectedObjectIndex].BossName, 255);
                    }
                    if (Info.Chapters[SelectedObjectIndex].Header[0] == 3)
                    {
                        Combo("Background", ref Info.Chapters[SelectedObjectIndex].Header[5], Textures, Textures.Length);
                        InputText("Chapter name", ref Info.Chapters[SelectedObjectIndex].Name, 255);
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
                                Array.Resize(ref Info.Chapters[SelectedObjectIndex].DialogInfo, Info.Chapters[SelectedObjectIndex].DialogInfo.Length + 1);
                                Info.Chapters[SelectedObjectIndex].DialogInfo[^1] = new FileDialogInfo();
                                SelectedTextureIndex = Info.Chapters[SelectedObjectIndex].DialogInfo.Length-1;
                            }
                            if (Button("Move Down"))
                            {
                                if (SelectedTextureIndex == Info.Chapters[SelectedObjectIndex].DialogInfo.Length - 1)
                                    return;
                                (Info.Chapters[SelectedObjectIndex].DialogInfo[SelectedTextureIndex], Info.Chapters[SelectedObjectIndex].DialogInfo[SelectedTextureIndex+1]) = 
                                    (Info.Chapters[SelectedObjectIndex].DialogInfo[SelectedTextureIndex+1], Info.Chapters[SelectedObjectIndex].DialogInfo[SelectedTextureIndex]);
                            }
                            if (Button("Move Up"))
                            {
                                if (SelectedTextureIndex < 1)
                                    return;
                                (Info.Chapters[SelectedObjectIndex].DialogInfo[SelectedTextureIndex], Info.Chapters[SelectedObjectIndex].DialogInfo[SelectedTextureIndex-1]) = 
                                    (Info.Chapters[SelectedObjectIndex].DialogInfo[SelectedTextureIndex-1], Info.Chapters[SelectedObjectIndex].DialogInfo[SelectedTextureIndex]);
                            }

                            ListBox("List", ref SelectedTextureIndex,
                                Info.Chapters[SelectedObjectIndex].DialogInfo.Select(x => x.Text.Split("\n").Last())
                                    .Select(x => x.Length > 16 ? x.Substring(0,16)+"..." : x).ToArray(), Info.Chapters[SelectedObjectIndex].DialogInfo.Length, 32);
                            if (SelectedTextureIndex != -1)
                            {
                                if (Button("Remove"))
                                    ShowQuestion("Do you want to remove this dialog?", () =>
                                    {
                                        var nArray = new FileDialogInfo[Info.Chapters[SelectedObjectIndex].DialogInfo.Length - 1];
                                        Array.Copy(Info.Chapters[SelectedObjectIndex].DialogInfo, 0,  nArray, 0, SelectedTextureIndex);
                                        Array.Copy(Info.Chapters[SelectedObjectIndex].DialogInfo, SelectedTextureIndex+1,  nArray, SelectedTextureIndex, Info.Chapters[SelectedObjectIndex].DialogInfo.Length-SelectedTextureIndex-1);
                                        Info.Chapters[SelectedObjectIndex].DialogInfo = nArray;
                                        SelectedTextureIndex = Math.Clamp(SelectedTextureIndex, -1,
                                            Info.Chapters[SelectedObjectIndex].DialogInfo.Length - 1);
                                    });
                                InputTextMultiline("Dialog Text",
                                    ref Info.Chapters[SelectedObjectIndex].DialogInfo[SelectedTextureIndex].Text, 65536,
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
        //Begin("Chapter Info Editor");
        //InputInt("Length", ref Info.Header[0x2], 1, 1);
        //if (Combo("type", ref Info.Header[0x1], ChapterTypes, ChapterTypes.Length))
        //{
        //}
        //if (.Type == 3)
        //{
        //    InputInt("Spell card number", ref EditableChapterInfo.Header[3], 1, 1);
        //    Checkbox("Timeout Card", ref EditableChapterInfo.TimeoutCard);
        //}
        //if(EditableChapterInfo.Type > 1)
        //    Checkbox("Boss Invincible", ref EditableChapterInfo.BossInvincible);
        //
        //End();
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
}
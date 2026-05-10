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
    private GameBox GameBox = new GameBox();
    private FileStageInfo Info = info;
    //private EditableChapterInfo EditableChapterInfo = editableChapterInfo;
    private int TabItem = 0;
    private string FileName = fileName;
    private static string[] ChapterTypes = typeof(ChapterType).GetEnumNames();
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
            TabItem = 0;
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
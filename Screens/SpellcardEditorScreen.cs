using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using ImGuiNET;
using static ImGuiNET.ImGui;

namespace DmitryAndDemid.Screens;

public class SpellcardEditorScreen(EditableChapterInfo editableChapterInfo, string fileName) : Screen
{
    private EditableChapterInfo EditableChapterInfo = editableChapterInfo;
    private string FileName = fileName;
    private static string[] ChapterTypes = typeof(ChapterType).GetEnumNames();
    private string Question = "Are you sure to delete this object?";
    private bool QuestionShown = false;
    private Action? QuestionOKAction = null;
    private int SelectedObjectIndex = -1;
    private string[] Objects => EditableChapterInfo.GameObjects.Select(x => x.IntVariables[15].ToString()).ToArray();
    
    void Save()
    {
        
    }

    void Reload()
    {
        if (File.Exists(FileName))
            EditableChapterInfo = Data.EditableChapterInfo.Load(FileName);
    }
    
    #if DEBUG
    public override void DrawImgui()
    {
        bool s = false;
        BeginMainMenuBar();
        if(MenuItem("Save"))
            Save();
        if(MenuItem("Reload"))
            Save();
        if(MenuItem("Exit"))
            Runtime.CurrentRuntime.RemoveScreen(this);
        EndMainMenuBar();
        Begin("Object Selector");
        if (MenuItem("Add"))
        {
            int id = 0;
            while (EditableChapterInfo.GameObjects.Any(x => x.IntVariables[15] == id))
                id++;
            var obj = new EditableGameObject();
            obj.IntVariables[15] = id;
            EditableChapterInfo.GameObjects.Add(obj);
        }
        ListBox("objects", ref SelectedObjectIndex, Objects, Objects.Length, 32);
        if (MenuItem("Delete"))
            if(SelectedObjectIndex != -1)
                ShowQuestion("Are you sure to delete selected object?", () =>
                {
                    EditableChapterInfo.GameObjects.RemoveAt(SelectedObjectIndex);
                    if(SelectedObjectIndex == EditableChapterInfo.GameObjects.Count)
                        SelectedObjectIndex += -1;
                });
        End();
        Begin("Chapter Info Editor");
        InputInt("ID", ref EditableChapterInfo.Id, 1, 1);
        InputInt("Length", ref EditableChapterInfo.Length, 1, 1);
        Checkbox("Appears on easy", ref EditableChapterInfo.Easy);
        Checkbox("Appears on normal", ref EditableChapterInfo.Normal);
        Checkbox("Appears on hard", ref EditableChapterInfo.Hard);
        Checkbox("Appears on max", ref EditableChapterInfo.Max);
        Checkbox("Appears on extra", ref EditableChapterInfo.Extra);
        Combo("type", ref  EditableChapterInfo.Type, ChapterTypes, ChapterTypes.Length);
        if (EditableChapterInfo.Type == 3)
            InputInt("Spell card number", ref EditableChapterInfo.CardNumber, 1, 1);
        End();
        if (QuestionShown)
        {
            Begin("Question");
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

        if (SelectedObjectIndex != -1)
        {
            Begin("Object Editor");
            Checkbox("Is Bullet",
                ref EditableChapterInfo.GameObjects[SelectedObjectIndex].IsBullet);
            SliderInt("Position X", ref EditableChapterInfo.GameObjects[SelectedObjectIndex].IntVariables[2], -32, 416);
            SliderInt("Position Y", ref EditableChapterInfo.GameObjects[SelectedObjectIndex].IntVariables[3], -32, 416);
            Checkbox("Dangerous For Player when Collide",
                ref EditableChapterInfo.GameObjects[SelectedObjectIndex].DangerousForPlayer);
            Checkbox("Apply Shader",
                ref EditableChapterInfo.GameObjects[SelectedObjectIndex].ApplyShader);
            Checkbox("Use Creation Script",
                ref EditableChapterInfo.GameObjects[SelectedObjectIndex].UseCreateScript);
            Checkbox("Use Removal Script",
                ref EditableChapterInfo.GameObjects[SelectedObjectIndex].UseRemoveScript);
            Checkbox("Depends on Entity",
                ref EditableChapterInfo.GameObjects[SelectedObjectIndex].DependsOnEntity);
            Checkbox("Texture Generated",
                ref EditableChapterInfo.GameObjects[SelectedObjectIndex].TextureGenerated);

            End();
        }
        base.DrawImgui();
    }
    #endif

    void ShowQuestion(string str, Action act)
    {
        Question = str;
        QuestionShown = true;
        QuestionOKAction = act;
    }
}
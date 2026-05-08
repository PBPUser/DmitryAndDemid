using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using ImGuiNET;
using Raylib_cs;
using static ImGuiNET.ImGui;

namespace DmitryAndDemid.Screens;

public class SpellcardEditorScreen(EditableChapterInfo editableChapterInfo, string fileName) : Screen
{
    private GameBox GameBox = new GameBox();
    private EditableChapterInfo EditableChapterInfo = editableChapterInfo;
    private string FileName = fileName;
    private static string[] ChapterTypes = typeof(ChapterType).GetEnumNames();
    private string Question = "Are you sure to delete this object?";
    private bool QuestionShown = false;
    private Action? QuestionOKAction = null;
    private int Time = 0;
    private int SelectedObjectIndex = -1;
    private string[] Objects => EditableChapterInfo.GameObjects.Select(x => x.IntVariables[15].ToString()).ToArray();
    private int SelectedTextureIndex = -1;

    private string[] Visuals => 
        EditableChapterInfo.GameObjects[SelectedObjectIndex].IsBullet ?
        BulletVisual.Constants.Keys.ToArray() :
        EntityVisual.Visuals.Keys.ToArray();
    void Save()
    {
        EditableChapterInfo.Save(FileName);
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

        if (ListBox("objects", ref SelectedObjectIndex, Objects, Objects.Length, 32))
        {
            SelectedTextureIndex = SelectedObjectIndex == -1
                ? -1
                : Visuals.IndexOf(EditableChapterInfo.GameObjects[SelectedObjectIndex].Visual);
        }
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
        InputInt("ID", ref EditableChapterInfo.Header[1], 1, 1);
        InputInt("Length", ref EditableChapterInfo.Header[2], 1, 1);
        Checkbox("Appears on easy", ref EditableChapterInfo.Easy);
        Checkbox("Appears on normal", ref EditableChapterInfo.Normal);
        Checkbox("Appears on hard", ref EditableChapterInfo.Hard);
        Checkbox("Appears on max", ref EditableChapterInfo.Max);
        Checkbox("Appears on extra", ref EditableChapterInfo.Extra);
        if (Combo("type", ref EditableChapterInfo.Type, ChapterTypes, ChapterTypes.Length))
        {
        }
        if (EditableChapterInfo.Type == 3)
        {
            InputInt("Spell card number", ref EditableChapterInfo.Header[3], 1, 1);
            Checkbox("Timeout Card", ref EditableChapterInfo.TimeoutCard);
        }
        if(EditableChapterInfo.Type > 1)
            Checkbox("Boss Invincible", ref EditableChapterInfo.BossInvincible);
        
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
                ref EditableChapterInfo.GameObjects[SelectedObjectIndex].UseAppearScript);
            Checkbox("Use Removal Script",
                ref EditableChapterInfo.GameObjects[SelectedObjectIndex].UseDisappearScript);
            Checkbox("Depends on Entity",
                ref EditableChapterInfo.GameObjects[SelectedObjectIndex].DependsOnEntity);
            Text("Texture");
            if (Combo( "Bullet Visual", ref SelectedTextureIndex, Visuals, Visuals.Length, 32))
            {
                EditableChapterInfo.GameObjects[SelectedObjectIndex].Visual = Visuals[SelectedTextureIndex];
            }
            var obj = EditableChapterInfo.GameObjects[SelectedObjectIndex];
            if (Visuals.Contains(obj.Visual))
            {
                if (obj.IsBullet)
                {
                    var color = Helper.ColorIntToVector3(obj.IntVariables[14]);
                    var bulletVisual = BulletVisual.Constants[obj.Visual];
                    var size = bulletVisual.GetSourceSize();
                    var pos = bulletVisual.GetSourcePosition(color);
                    var max = Math.Max(size.X, size.Y);
                    rlImGui_cs.rlImGui.ImageRect(
                        bulletVisual.GetTexture(color),
                        (int)(size.X * 64 / max), (int)Math.Abs(size.Y * 64 / max),
                        new Rectangle(pos, size)
                    );
                }
                else
                {
                    var visual = EntityVisual.Visuals[obj.Visual];
                    var size = visual.RenderSize;
                    var pos = visual.SourcePosition;
                    var max = Math.Max(size.X, size.Y);
                    rlImGui_cs.rlImGui.ImageRect(
                        Runtime.CurrentRuntime.Textures[visual.Texture],
                        (int)(size.X * 64 / max), (int)Math.Abs(size.Y * 64 / max),
                        new Rectangle(pos, size)
                    );
                }
            }
            
            
            
            End();
            Begin("Preview");
            SliderInt("Time", ref Time, 0, EditableChapterInfo.Header[2]);
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
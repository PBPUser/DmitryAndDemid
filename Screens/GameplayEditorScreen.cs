using System.Numerics;
using static Raylib_cs.Raylib;
using DmitryAndDemid.Common;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using static ImGuiNET.ImGui;

namespace DmitryAndDemid.Screens;

public class GameplayEditorScreen : Screen
{
    public GameplayEditorScreen()
    {
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
    }

    private int Item = 0;
    private int Page = 0;
    private float Zoom = 1;
    private string[] Items = ["Bullet Visuals", ""];
    private Vector3 Color = Vector3.One;
    
    public override void Render()
    {
        DrawBackground();
        base.Render();
    }

#if DEBUG
    public override void DrawImgui()
    {
        Begin("Gameplay Viewer and Editor");
        if (ListBox("Menu Items", ref Page, Items, Items.Length))
            Item = 0;
        if (Button("Exit"))
        {
            Runtime.CurrentRuntime.RemoveScreen(this);
        }
        End();
        switch (Page)
        {
            case 0:
                string[] j = Data.BulletVisual.Constants.Select(x => x.Key).ToArray();
                Begin("Bullets Selector");
                ListBox("Visuals",
                    ref Item,
                    j, j.Length);
                End();
                Begin("Bullet View");
                var item = Data.BulletVisual.Constants.ElementAt(Item).Value;
                Text($"Size: {item.RenderSize}");
                Text($"Type: {item.RenderType}");
                if (ColorEdit3($"Color: ", ref Color))
                {
                    
                }
                SliderFloat("Zoom", ref Zoom, 0.01f, 100);
                var texture = item.GetTexture(Color);
                rlImGui.ImageRect(texture,
                    (int)(item.SourceSize.Value.X * Zoom), 
                    (int)(item.SourceSize.Value.Y * Zoom),
                    new Rectangle(item.GetSourcePosition(Color), item.SourceSize.Value));
                End();
                break;
        }
        base.DrawImgui();
    }
#endif
}
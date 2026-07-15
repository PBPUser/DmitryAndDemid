#if DEBUG
using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using static ImGuiNET.ImGui;

namespace DmitryAndDemid.Screens;
#if DEBUG
public class DropEditorScreen : Screen
{
    private Drop Drop;
    private Action<Drop> ActionApply;

    public DropEditorScreen(Drop drop, Action<Drop> actionApply)
    {
        ActionApply = actionApply;
        Drop = drop;
    }

    public override void DrawImgui()
    {
        Begin("Drop Editor");
        Checkbox("Drop Heart", ref Drop.DropHeart);
        Checkbox("Drop Heart Piece", ref Drop.DropHeartPiece);
        Checkbox("Drop Star", ref Drop.DropStar);
        Checkbox("Drop Star Piece", ref Drop.DropStarPiece);
        Checkbox("Drop Full Power",  ref Drop.DropFullPower);
        SliderInt("Drop Large Power", ref Drop.DropLargePower, 0, 255);
        SliderInt("Drop Power", ref Drop.DropPower, 0, 255);
        SliderInt("Drop Score", ref Drop.DropScore, 0, 255);
        if (Button("Apply"))
        {
            ActionApply.Invoke(Drop);
            Runtime.CurrentRuntime.RemoveScreen(this);
        }
        if (Button("Cancel"))
            Runtime.CurrentRuntime.RemoveScreen(this);
        End();
        base.DrawImgui();
    }
}
#endif
#endif

using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Backgrounds;

public class SwapStageBackground(StageBackground top, StageBackground bottom) : StageBackground
{
    public byte OpacityTop = 255;
    private TargetHandle TextureTemporary = LoadRenderTexture(384, 448);

    protected override void Render(TargetHandle texture, int tick, float delta)
    {
        top.Draw(TextureTemporary, tick, delta);
        bottom.Draw(texture, tick, delta);
        BeginTextureMode(TextureTemporary);
        DrawTexturePro(TextureTemporary.Texture, 
            new Rect(0, 0, 384, -448),
            new Rect(0, 0, 384, 448),
            Vector2.Zero, 0, Rgba.White with { A = OpacityTop });
        EndTextureMode();
    }

    protected override void Unload()
    {
        UnloadRenderTexture(TextureTemporary);
        base.Unload();
    }
}
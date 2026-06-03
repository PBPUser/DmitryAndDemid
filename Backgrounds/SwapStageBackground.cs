using System.Numerics;
using DmitryAndDemid.Common;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Backgrounds;

public class SwapStageBackground(StageBackground top, StageBackground bottom) : StageBackground
{
    public byte OpacityTop = 255;
    private RenderTexture2D TextureTemporary = LoadRenderTexture(384, 448);

    protected override void Render(RenderTexture2D texture, int tick, float delta)
    {
        top.Draw(TextureTemporary, tick, delta);
        bottom.Draw(texture, tick, delta);
        BeginTextureMode(TextureTemporary);
        DrawTexturePro(TextureTemporary.Texture, 
            new Rectangle(0, 0, 384, -448),
            new Rectangle(0, 0, 384, 448),
            Vector2.Zero, 0, Color.White with { A = OpacityTop });
        EndTextureMode();
    }

    protected override void Unload()
    {
        UnloadRenderTexture(TextureTemporary);
        base.Unload();
    }
}
using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Common;

namespace DmitryAndDemid.Backgrounds;

/// <summary>
/// The empty background — stage 3 plays on it. Nothing moves; it only paints a flat dark fill, because the
/// playfield's background target is not cleared before a background draws into it, and drawing nothing would
/// leave whatever the previous stage's background last rendered frozen behind the bullets.
/// </summary>
public class SillyBackground : StageBackground
{
    private static readonly Rgba Fill = new(14, 12, 22, 255);

    protected override void Render(RenderedTexture texture, int tick, float delta)
    {
        DrawRectangle(0, 0, 384, 448, Fill);
    }
}

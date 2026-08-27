using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class ItemGetBorderLineOverlay(GameBox box) : GameplayOverlay(box, 0.5f, 5)
{
    BasicTexture Texture = Runtime.CurrentRuntime.Textures["item-get-border-line.png"];
    private Rect Source = new Rect(0, 0, new Vector2(384, 128) * Runtime.CurrentRuntime.ScaleF);
    
    protected override void Draw()
    {
        // The line used to sit dead still and fade. It now breathes: it settles DOWN into place from where it
        // rests and pulses in brightness, which reads as a live threshold rather than a decal. Downward only —
        // it is anchored to the top of the playfield, so travelling up would slide it off the edge.
        float time = (float)GetTime();
        float bob = (0.5f + 0.5f * MathF.Sin(time * 2.4f)) * 3f * Runtime.CurrentRuntime.ScaleF;
        // The pulse rides ON TOP of the appear/disappear fade rather than replacing it, so the overlay still
        // arrives and leaves exactly as it did.
        float pulse = 0.78f + 0.22f * MathF.Sin(time * 4.1f);
        DrawTexturePro(Texture, Source, Source with { Y = Source.Y + bob }, Vector2.Zero, 0,
            Rgba.White with { A = Helper.TimeToTransparency(State * pulse) });
        base.Draw();
    }
}
using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

/// <summary>
/// Tiles faint fork outlines across the bottom-right corner of the playfield — the backdrop dressing for the
/// stage-3 pizza spell. A fixed grid of forkCut.png, gently breathing, fading in and out with the overlay's life.
/// (Kept as an overlay rather than a fragment shader so it needs no per-backend shader build; the visual — tiled
/// fork outlines pinned bottom-right — is the same.)
/// </summary>
public class ForkTilesOverlay(GameBox box, float length) : GameplayOverlay(box, 0.8f, length)
{
    private const float Pw = 384f, Ph = 448f;   // playfield design size
    private const int Cols = 3, Rows = 3;        // the tiled block, anchored bottom-right
    private readonly BasicTexture Fork = Runtime.CurrentRuntime.Textures["forkCut.png"];

    protected override void Draw()
    {
        float sf = Runtime.CurrentRuntime.ScaleF;
        float t = (float)Box.GetTime();
        byte a = (byte)(60 * State);
        var src = new Rect(0, 0, Fork.Width, Fork.Height);
        float aspect = Fork.Width > 0 ? Fork.Height / (float)Fork.Width : 1f;

        float cell = 46f;                         // tile pitch (design px)
        float tileW = cell * 0.86f, tileH = tileW * aspect;
        // Anchor the block to the bottom-right corner.
        float x0 = Pw - Cols * cell + (cell - tileW) / 2f;
        float y0 = Ph - Rows * cell + (cell - tileH) / 2f;

        for (int r = 0; r < Rows; r++)
        for (int col = 0; col < Cols; col++)
        {
            int i = r * Cols + col;
            float breathe = 1f + 0.06f * MathF.Sin(t * 1.2f + i);
            float rot = MathF.Sin(t * 0.5f + i * 0.7f) * 8f;
            float w = tileW * breathe, h = tileH * breathe;
            float cx = (x0 + col * cell + tileW / 2f) * sf;
            float cy = (y0 + r * cell + tileH / 2f) * sf;
            DrawTexturePro(Fork, src,
                new Rect(cx, cy, w * sf, h * sf),
                new Vector2(w * sf / 2f, h * sf / 2f), rot, new Rgba(235, 235, 245, a));
        }
        base.Draw();
    }
}

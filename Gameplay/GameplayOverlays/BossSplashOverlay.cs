using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

/// <summary>
/// The boss "splash" that plays when a boss takes a spell card.
///
/// Generic bosses: their (semi-transparent) art enters from off the top of the playfield and travels off the
/// bottom — a single sweeping pass.
///
/// Dmitry (the two-part ctor): dmitry_top.png / dmitry_bottom.png scale up from the centre (from ~0.7x to ~2x
/// the screen, with an eased scale that speeds up toward the end), the two halves drift apart on the Y axis, and
/// the whole thing fades in and out.
/// </summary>
public class BossSplashOverlay : GameplayOverlay
{
    private const float Pw = 384f, Ph = 448f;   // playfield design size

    private readonly BasicTexture Art;          // generic art, or dmitry TOP
    private readonly BasicTexture Bottom;       // dmitry BOTTOM (unused when !DmitryStyle)
    private readonly bool DmitryStyle;

    /// <summary>Generic single-art splash sweeping top-to-bottom.</summary>
    public BossSplashOverlay(GameBox box, BasicTexture art, float length) : base(box, 0.5f, length)
    {
        Art = art;
        DmitryStyle = false;
    }

    /// <summary>Dmitry's two-part splash (top + bottom halves).</summary>
    public BossSplashOverlay(GameBox box, BasicTexture top, BasicTexture bottom, float length)
        : base(box, 0.5f, length)
    {
        Art = top;
        Bottom = bottom;
        DmitryStyle = true;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    protected override void Draw()
    {
        float sf = Runtime.CurrentRuntime.ScaleF;
        float prog = Math.Clamp((float)((Box.GetTime() - TimeAppear) / Length), 0f, 1f);
        float fade = State;   // fades in and out at the ends of the overlay's life

        if (DmitryStyle)
            DrawDmitry(sf, prog, fade);
        else
            DrawGeneric(sf, prog, fade);

        base.Draw();
    }

    private void DrawGeneric(float sf, float prog, float fade)
    {
        float artH = Ph * 1.1f * sf;
        float artW = Art.Height > 0 ? artH * Art.Width / Art.Height : artH;
        float y = Lerp(-artH, Ph * sf, prog);          // starts above the top edge, exits past the bottom
        float x = (Pw * sf - artW) / 2f;
        byte a = (byte)(130 * fade);                   // semi-transparent
        DrawTexturePro(Art, new Rect(0, 0, Art.Width, Art.Height),
            new Rect(x, y, artW, artH), Vector2.Zero, 0, Rgba.White with { A = a });
    }

    private void DrawDmitry(float sf, float prog, float fade)
    {
        float screen = Pw * sf;                          // "times of screen"
        float ease = prog * prog;                        // eased scale that speeds up toward the end
        float w = Lerp(0.7f, 2f, ease) * screen;
        float cx = Pw * sf / 2f, cy = Ph * sf / 2f;
        float drift = prog * 0.18f * Ph * sf;            // the halves move apart on Y
        byte a = (byte)(190 * fade);
        Rgba tint = Rgba.White with { A = a };

        float topH = Art.Width > 0 ? w * Art.Height / Art.Width : w;
        float botH = Bottom.Width > 0 ? w * Bottom.Height / Bottom.Width : w;
        // Top half sits above the centre (bottom edge at cy - drift); bottom half below (top edge at cy + drift).
        DrawTexturePro(Art, new Rect(0, 0, Art.Width, Art.Height),
            new Rect(cx - w / 2f, cy - drift - topH, w, topH), Vector2.Zero, 0, tint);
        DrawTexturePro(Bottom, new Rect(0, 0, Bottom.Width, Bottom.Height),
            new Rect(cx - w / 2f, cy + drift, w, botH), Vector2.Zero, 0, tint);
    }
}

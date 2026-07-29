using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

/// <summary>
/// The now-playing line: the BGM's in-game name tucked flush into the bottom-right corner of the playfield —
/// no padding, no panel behind it — sliding in from off the right edge, holding, then sliding back out.
///
/// It used to live in the music room, announcing whichever track had just been previewed there. It belongs on
/// the gameplay screen instead — this is where music actually plays, so this is where naming it means anything.
/// </summary>
public class MusicTitleOverlay : GameplayOverlay
{
    private readonly string Title;

    /// <param name="musicListIndex">Index into <see cref="MusicInfo.MusicInformations"/>, which is what a stage
    /// header stores (Header[2] for the stage BGM) — not the entry's music-room Number.</param>
    public MusicTitleOverlay(GameBox box, int musicListIndex) : base(box, 0.4f, 4.5f)
    {
        MusicInfo? info = musicListIndex >= 0 && musicListIndex < MusicInfo.MusicInformations.Count
            ? MusicInfo.MusicInformations[musicListIndex]
            : null;
        string name = info == null
            ? ""
            : string.IsNullOrEmpty(info.InGameName) ? info.Title : info.InGameName;
        Title = Helper.Transliterate(name);
    }

    protected override void Draw()
    {
        // Suppressed in the attract demo, which should read as background footage rather than announce itself.
        if (Box.IsDemo || string.IsNullOrEmpty(Title))
        {
            base.Draw();
            return;
        }
        // 0 → 1 → 0 across the card's life, eased, driving both the slide and the fade. Because it rises at the
        // start and falls at the end, one value gives an entrance from the right and an exit back out the same
        // way, with no separate phase tracking.
        float state = State;
        if (state <= 0.002f)
        {
            base.Draw();
            return;
        }
        float eased = 1f - MathF.Pow(1f - state, 3f);

        float sf = Runtime.CurrentRuntime.ScaleF;
        float fieldW = 384 * sf, fieldH = 448 * sf;
        var font = Runtime.CurrentRuntime.Fonts["newsreader"];
        float textSize = 11 * sf;
        Vector2 measure = MeasureTextEx(font, Title, textSize, 1);
        if (measure.X > fieldW)
        {
            // Long track names (the stage-2 one is 40-odd characters) shrink to fit rather than run off the
            // playfield — there is no room here to wrap onto a second line.
            textSize *= fieldW / measure.X;
            measure = MeasureTextEx(font, Title, textSize, 1);
        }

        // With nothing behind it the text has to carry its own contrast over whatever the stage is doing, so it
        // is stamped with an outline instead of sitting on a panel.
        float thickness = MathF.Max(1f, 1.5f * sf);

        // Flush into the bottom-right corner — the inset is exactly the outline's reach, so it is the OUTLINE
        // that ends at the playfield edge rather than being clipped by it, with no padding beyond the ink. Off
        // its resting spot the line is pushed right by its own width, parking it fully past the edge.
        float x = fieldW - measure.X - thickness + (1f - eased) * (measure.X + thickness);
        float y = fieldH - measure.Y - thickness;
        byte a = (byte)(255 * MathF.Min(1f, eased * 1.6f));

        Helper.DrawTextOutlined(font, Title, new Vector2(x, y), textSize, 1,
            Rgba.White with { A = a }, new Rgba(0, 0, 0, (byte)(a * 0.85f)), thickness);
        base.Draw();
    }
}

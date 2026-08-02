using DmitryAndDemid.Rendering;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using DmitryAndDemid.Gameplay.RuntimeData;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

/// <summary>
/// Countdown to the mystical toilet's escape (ActionsScope.ToiletEscapeTick), hovering just below it — so the
/// "how long do I have to kill this" clock sits where the eye already is, instead of only showing up as the
/// unrelated chapter timer at the top of the screen (which isn't even on screen outside Spell/NonSpell chapters,
/// while the toilet can spawn any time Player.Signal crosses its threshold).
/// </summary>
public class ToiletTimerOverlay(GameBox box, RuntimeObject toilet) : GameplayOverlay(box, 0.25f, 99999)
{
    protected override void Draw()
    {
        if (Box.MysticalToilet != toilet)
        {
            Box.RemoveOverlay(this);
            return;
        }
        int remaining = ActionsScope.ToiletEscapeTick - (Box.CurrentTick - toilet.CreatedAt);
        Helper.PrepareTimer(remaining);
        float sf = Runtime.CurrentRuntime.ScaleF;
        // Below the toilet, not above: its wander band (y 64..128) sits near the top of the 448-tall playfield,
        // so a timer above it would clip off screen.
        Helper.DrawTimer(
            (int)(toilet.X * sf - Helper.TimerTextureSize.X / 2f),
            (int)((toilet.Y + 56) * sf),
            remaining < 180);
        base.Draw();
    }
}

using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

/// <summary>
/// The two CAUTION strips that scroll across the field, in opposite directions, when the mystical toilet
/// arrives. The strip texture is looked up per overlay, not cached in a static: it belongs to the "game"
/// texture group, which the title screen unloads and the next run reloads under a new handle, so a handle
/// kept from the first run would draw nothing (or garbage) on every run after it.
/// </summary>
public class MysticalToiletOverlay(GameBox box, float animationLength, float length) : GameplayOverlay(box, animationLength, length)
{
    private readonly Rect Target1 = new Rect(0, 64 * Runtime.CurrentRuntime.ScaleF,
        new Vector2(384, 90) * Runtime.CurrentRuntime.ScaleF);
    private readonly Rect Target2 = new Rect(0, 294 * Runtime.CurrentRuntime.ScaleF,
        new Vector2(384, 90) * Runtime.CurrentRuntime.ScaleF);

    private readonly BasicTexture Texture = Runtime.CurrentRuntime.Textures["caution.png"];

    protected override void Draw()
    {
        // The whole strip, at whatever size it was loaded (the sheet is scaled with the window, so the authored
        // 1536x360 is not its size here), scrolled by an offset the texture's REPEAT wrap turns into a loop.
        // Slicing the authored size out of the scaled texture is what cropped the message.
        Rect source = Helper.GetFullSource(Texture);
        float scroll = Box.GetTime() * Texture.Width * 0.125f;
        byte alpha = Helper.TimeToTransparency(State);
        DrawTexturePro(Texture, source with { X = scroll }, Target1, Vector2.Zero, 0, Rgba.White with { A = alpha });
        DrawTexturePro(Texture, source with { X = -scroll }, Target2, Vector2.Zero, 0, Rgba.White with { A = alpha });
        base.Draw();
    }
}

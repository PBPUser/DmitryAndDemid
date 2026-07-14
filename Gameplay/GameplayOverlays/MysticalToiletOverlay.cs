using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class MysticalToiletOverlay(GameBox box, float animationLength, float length) : GameplayOverlay(box, animationLength, length)
{
    private Rect Target1 = new Rect(0, 64 * Runtime.CurrentRuntime.ScaleF,
        new Vector2(384, 90) * Runtime.CurrentRuntime.ScaleF);
    private Rect Target2 = new Rect(0, 294 * Runtime.CurrentRuntime.ScaleF,
        new Vector2(384, 90) * Runtime.CurrentRuntime.ScaleF);

    private static TextureHandle Texture = Runtime.CurrentRuntime.Textures["caution.png"];
    private static Rect Source = new Rect(0, 0, 1536, 360);

    protected override void Draw()
    {
        DrawTexturePro(Texture, Source with { X = (Box.GetTime() * Texture.Width * 0.125f) }, Target1, Vector2.Zero, 0, Rgba.White with {A = Helper.TimeToTransparency(State)});
        DrawTexturePro(Texture, Source with { X = -(Box.GetTime() * Texture.Width * 0.125f) }, Target2, Vector2.Zero, 0, Rgba.White with {A = Helper.TimeToTransparency(State)});
        base.Draw();
    }
}
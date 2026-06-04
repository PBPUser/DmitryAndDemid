using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class MysticalToiletOverlay(GameBox box, float animationLength, float length) : GameplayOverlay(box, animationLength, length)
{
    private Rectangle Target1 = new Rectangle(0, 64 * Runtime.CurrentRuntime.ScaleF,
        new Vector2(384, 90) * Runtime.CurrentRuntime.ScaleF);
    private Rectangle Target2 = new Rectangle(0, 294 * Runtime.CurrentRuntime.ScaleF,
        new Vector2(384, 90) * Runtime.CurrentRuntime.ScaleF);

    private static Texture2D Texture = Runtime.CurrentRuntime.Textures["caution.png"];
    private static Rectangle Source = new Rectangle(0, 0, 1536, 360);

    protected override void Draw()
    {
        DrawTexturePro(Texture, Source with { X = -(Box.GetTime() * Texture.Width * 0.125f) }, Target1, Vector2.Zero, 0, Color.White with {A = Helper.TimeToTransparency(State)});
        DrawTexturePro(Texture, Source with { X = (Box.GetTime() * Texture.Width * 0.125f) }, Target2, Vector2.Zero, 0, Color.White with {A = Helper.TimeToTransparency(State)});
        base.Draw();
    }
}
using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class BasicGameplayOverlay : GameplayOverlay
{
    public BasicGameplayOverlay(GameBox box, string image, float animationLength, float length) : base(box, animationLength, length)
    {
        Texture = Runtime.CurrentRuntime.Textures[image];
        SourceRectangle = Helper.GetFullSource(Texture);
        DestinationRectangle = new Rect(0, 128 * Runtime.CurrentRuntime.ScaleF, SourceRectangle.Size / 4  * Runtime.CurrentRuntime.ScaleF);
    }

    private BasicTexture Texture;
    private Rect SourceRectangle;
    Rect DestinationRectangle;

    protected override void Draw()
    {
        float state = State;
        Rect rectangle = DestinationRectangle with { Height = DestinationRectangle.Height * state };
        DrawTexturePro(Texture, SourceRectangle, rectangle, Vector2.Zero, 0, Rgba.White);
    }
}
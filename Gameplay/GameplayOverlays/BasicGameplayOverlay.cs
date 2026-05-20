using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class BasicGameplayOverlay : GameplayOverlay
{
    public BasicGameplayOverlay(GameBox box, string image, float animationLength, float length) : base(box, animationLength, length)
    {
        Texture = Runtime.CurrentRuntime.Textures[image];
        SourceRectangle = Helper.GetFullSource(Texture);
        DestinationRectangle = new Rectangle(0, 128 * Runtime.CurrentRuntime.ScaleF, SourceRectangle.Size / 4  * Runtime.CurrentRuntime.ScaleF);
    }

    private Texture2D Texture;
    private Rectangle SourceRectangle;
    Rectangle DestinationRectangle;

    protected override void Draw()
    {
        float state = State;
        Rectangle rectangle = DestinationRectangle with { Height = DestinationRectangle.Height * state };
        DrawTexturePro(Texture, SourceRectangle, rectangle, Vector2.Zero, 0, Color.White);
    }
}
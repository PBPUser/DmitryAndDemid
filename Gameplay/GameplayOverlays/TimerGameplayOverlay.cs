using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class TimerGameplayOverlay : GameplayOverlay
{
    public TimerGameplayOverlay(GameBox box, string image, int ticks, double time, float animationLength, float length) : base(box, animationLength, length)
    {
        TimersTexture = LoadRenderTexture(
            (int)(Runtime.CurrentRuntime.ScaleF * 128),
            (int)(Runtime.CurrentRuntime.ScaleF * 96)
        );
        Texture = Runtime.CurrentRuntime.Textures[image];
        SourceRectangle = Helper.GetFullSource(Texture);
        DestinationRectangle = new Rectangle(0, 128 * Runtime.CurrentRuntime.ScaleF, SourceRectangle.Size / 4  * Runtime.CurrentRuntime.ScaleF);
        SourceRectangle2 = Helper.GetFullSourceRenderTexture(TimersTexture);
        DestinationRectangle2 =
            new Rectangle(
                TimersTexture.Texture.Width,
                176 * Runtime.CurrentRuntime.ScaleF,
                TimersTexture.Texture.Width,
                TimersTexture.Texture.Height
            );
        Helper.DrawTimerSplash(TimersTexture, ticks, time);
    }

    private RenderTexture2D TimersTexture;
    private Texture2D Texture;
    private Rectangle SourceRectangle;
    private Rectangle DestinationRectangle;
    private Rectangle SourceRectangle2;
    private Rectangle DestinationRectangle2;

    protected override void Unload()
    {
        UnloadRenderTexture(TimersTexture);
    }

    protected override void Draw()
    {
        float state = State;
        Rectangle rectangle = DestinationRectangle with { Height = DestinationRectangle.Height * state };
        DrawTexturePro(Texture, SourceRectangle, rectangle, Vector2.Zero, 0, Color.White);
        DrawTexturePro(TimersTexture.Texture, SourceRectangle2, DestinationRectangle2, Vector2.Zero, 0, Color.White with {A = Helper.TimeToTransparency(state)});
    }
}
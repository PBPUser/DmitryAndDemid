using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

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
        DestinationRectangle = new Rect(0, 128 * Runtime.CurrentRuntime.ScaleF, SourceRectangle.Size);
        SourceRectangle2 = Helper.GetFullSourceRenderTexture(TimersTexture);
        DestinationRectangle2 =
            new Rect(
                TimersTexture.Texture.Width,
                176 * Runtime.CurrentRuntime.ScaleF,
                TimersTexture.Texture.Width,
                TimersTexture.Texture.Height
            );
        Helper.DrawTimerSplash(TimersTexture, ticks, time);
    }

    private RenderedTexture TimersTexture;
    private BasicTexture Texture;
    private Rect SourceRectangle;
    private Rect DestinationRectangle;
    private Rect SourceRectangle2;
    private Rect DestinationRectangle2;

    protected override void Unload()
    {
        UnloadRenderTexture(TimersTexture);
    }

    protected override void Draw()
    {
        float state = State;
        Rect rectangle = DestinationRectangle with { Height = DestinationRectangle.Height * state };
        DrawTexturePro(Texture, SourceRectangle, rectangle, Vector2.Zero, 0, Rgba.White);
        DrawTexturePro(TimersTexture.Texture, SourceRectangle2, DestinationRectangle2, Vector2.Zero, 0, Rgba.White with {A = Helper.TimeToTransparency(state)});
    }
}
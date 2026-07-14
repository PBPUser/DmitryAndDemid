using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class SignalGameplayOverlay : GameplayOverlay
{
    public SignalGameplayOverlay(GameBox box) : base(box, 0, float.MaxValue)
    {
        NinePatch = new NinePatch()
        {
            Layout = NinePatchLayout.ThreePatchVertical,
            Top = 7,
            Bottom = 7
        };
        GrayedSource = WhiteSource = NetworkSource = new Rect(0, 188, 27, 47);
        WhiteSource.X += 32;
        NetworkSource.X += 60;
        Width = 7 * Runtime.CurrentRuntime.ScaleF;
        Height = 12 * Runtime.CurrentRuntime.ScaleF;
        Padding = 2 * Runtime.CurrentRuntime.ScaleF;
        NetworkTarget = new Rect(Padding, 432 * Runtime.CurrentRuntime.ScaleF, NetworkSource.Size / 4 * Runtime.CurrentRuntime.ScaleF);
    }

    private NinePatch NinePatch;
    private Rect GrayedSource;
    private Rect WhiteSource;
    private Rect NetworkSource;
    private Rect NetworkTarget;
    private float Height;
    private float Width;
    private float Padding;
    
    protected override void Draw()
    {
        float h, y;
        for (int i = 0; i < 7; i++)
        {
            h = (float)(Height * (0.3 + 0.1 * i));
            y = Height - h;
            DrawTextureNPatch(Runtime.CurrentRuntime.Textures["ingame-stuff.png"],
                NinePatch with { Source = (MathF.Sqrt(Box.Player.Signal) > i ? WhiteSource : GrayedSource) }, 
                new Rect(Padding + (Padding + Width) * i, (448 * Runtime.CurrentRuntime.ScaleF) - Padding - Height + y, Width, h), 
                Vector2.Zero, 0, Rgba.White);
        }
        DrawTexturePro(Runtime.CurrentRuntime.Textures["ingame-stuff.png"],
            NetworkSource, NetworkTarget, Vector2.Zero, 0, Rgba.White);
        base.Draw();
    }
}
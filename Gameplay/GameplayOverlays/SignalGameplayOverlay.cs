using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class SignalGameplayOverlay : GameplayOverlay
{
    public SignalGameplayOverlay(GameBox box) : base(box, 0, float.MaxValue)
    {
        NPatchInfo = new NPatchInfo()
        {
            Layout = NPatchLayout.ThreePatchVertical,
            Top = 7,
            Bottom = 7
        };
        GrayedSource = WhiteSource = NetworkSource = new Rectangle(0, 188, 27, 47);
        WhiteSource.X += 32;
        NetworkSource.X += 60;
        Width = 7 * Runtime.CurrentRuntime.ScaleF;
        Height = 12 * Runtime.CurrentRuntime.ScaleF;
        Padding = 2 * Runtime.CurrentRuntime.ScaleF;
        NetworkTarget = new Rectangle(Padding, 432 * Runtime.CurrentRuntime.ScaleF, NetworkSource.Size / 4 * Runtime.CurrentRuntime.ScaleF);
    }

    private NPatchInfo NPatchInfo;
    private Rectangle GrayedSource;
    private Rectangle WhiteSource;
    private Rectangle NetworkSource;
    private Rectangle NetworkTarget;
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
                NPatchInfo with { Source = Box.Player.Signal > i ? WhiteSource : GrayedSource }, 
                new Rectangle(Padding + (Padding + Width) * i, (448 * Runtime.CurrentRuntime.ScaleF) - Padding - Height + y, Width, h), 
                Vector2.Zero, 0, Color.White);
        }
        DrawTexturePro(Runtime.CurrentRuntime.Textures["ingame-stuff.png"],
            NetworkSource, NetworkTarget, Vector2.Zero, 0, Color.White);
        base.Draw();
    }
}
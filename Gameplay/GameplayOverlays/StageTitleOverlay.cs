using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class StageTitleOverlay(GameBox box, int index) : GameplayOverlay(box, 0.5f, 5)
{
    Texture2D Texture = Runtime.CurrentRuntime.Textures["stages.png"];
    private Rectangle Source1 = new Rectangle(0, index * 512, 1536, 96);
    private Rectangle Source2 = new Rectangle(0, index * 512 + 96, 1536, 320);
    private Rectangle Source3 = new Rectangle(0, index * 512 + 416, 1536, 96);
    private Rectangle Source4 = new Rectangle(1536, index * 640, 640, 640);
    private Rectangle Destination1 = new Rectangle(0, 112 * Runtime.CurrentRuntime.ScaleF, new Vector2(384, 24)*Runtime.CurrentRuntime.ScaleF);
    private Rectangle Destination2 = new Rectangle(0, 136 * Runtime.CurrentRuntime.ScaleF, new Vector2(384, 80)*Runtime.CurrentRuntime.ScaleF);
    private Rectangle Destination3 = new Rectangle(0, 216 * Runtime.CurrentRuntime.ScaleF, new Vector2(384, 24)*Runtime.CurrentRuntime.ScaleF);
    private Rectangle Destination4 = new Rectangle(288* Runtime.CurrentRuntime.ScaleF, 192 * Runtime.CurrentRuntime.ScaleF, new Vector2(40)*Runtime.CurrentRuntime.ScaleF);
    private Vector2 Origin = new Vector2(40) * Runtime.CurrentRuntime.ScaleF;
    
    protected float StateX(float offset) => 
        Raymath.Clamp((Box.GetTime() - TimeAppear + offset) / AnimationLength, 0, 1) * Raymath.Clamp((TimeAppear + Length - Box.GetTime()) / AnimationLength, 0, 1);

    
    protected override void Draw()
    {
        Raylib.DrawTexturePro(Texture, Source4, Destination4 with { Position = Destination4.Position + new Vector2(MathF.Sin(Box.GetTime()), MathF.Cos(Box.GetTime() * 1.5f)) * 16 * Runtime.CurrentRuntime.ScaleF }, Origin, MathF.Sin(Box.GetTime()) * 1.5f, Color.White with {A=Helper.TimeToTransparency(StateX(1.5f))});
        Raylib.DrawTexturePro(Texture, Source1, Destination1, Vector2.Zero, 0, Color.White with {A=Helper.TimeToTransparency(State)});
        Raylib.DrawTexturePro(Texture, Source2, Destination2, Vector2.Zero, 0, Color.White with {A=Helper.TimeToTransparency(StateX(.5f))});
        Raylib.DrawTexturePro(Texture, Source3, Destination3, Vector2.Zero, 0, Color.White with {A=Helper.TimeToTransparency(StateX(1))});
        base.Draw();
    }
}
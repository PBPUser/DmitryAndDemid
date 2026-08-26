using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class StageTitleOverlay(GameBox box, int index) : GameplayOverlay(box, 0.5f, 5)
{
    BasicTexture Texture = Runtime.CurrentRuntime.Textures["stages.png"];
    private Rect Source1 = new Rect(0, index * 512, 1536, 96);
    private Rect Source2 = new Rect(0, index * 512 + 96, 1536, 320);
    private Rect Source3 = new Rect(0, index * 512 + 416, 1536, 96);
    private Rect Source4 = new Rect(1536, index * 640, 640, 640);
    private Rect Destination1 = new Rect(0, 112 * Runtime.CurrentRuntime.ScaleF, new Vector2(384, 24)*Runtime.CurrentRuntime.ScaleF);
    private Rect Destination2 = new Rect(0, 136 * Runtime.CurrentRuntime.ScaleF, new Vector2(384, 80)*Runtime.CurrentRuntime.ScaleF);
    private Rect Destination3 = new Rect(0, 216 * Runtime.CurrentRuntime.ScaleF, new Vector2(384, 24)*Runtime.CurrentRuntime.ScaleF);
    // The stage emblem. Destination4's position is where its CENTRE goes, because Origin is half its size —
    // that way the sine wobble below rotates it in place instead of swinging it around a corner. It sits above
    // and right of the title block (which runs y 112..240 across the full width), clear of the words: drawn
    // first, it would otherwise only show as fragments poking out between the letters of the title.
    private const float EmblemSize = 96f;
    private Rect Destination4 = new Rect(312 * Runtime.CurrentRuntime.ScaleF, 76 * Runtime.CurrentRuntime.ScaleF, new Vector2(EmblemSize)*Runtime.CurrentRuntime.ScaleF);
    private Vector2 Origin = new Vector2(EmblemSize / 2) * Runtime.CurrentRuntime.ScaleF;
    
    protected float StateX(float offset) => 
        MathUtil.Clamp((Box.GetTime() - TimeAppear + offset) / AnimationLength, 0, 1) * MathUtil.Clamp((TimeAppear + Length - Box.GetTime()) / AnimationLength, 0, 1);

    
    protected override void Draw()
    {
        // The stage title splash is suppressed during the title-screen attract demo (the demo should read as
        // background footage, not announce its stage) and in spell practice — there is no stage to announce,
        // just the single card the player picked.
        if (Box.IsDemo || Box.IsSpellPractice)
        {
            base.Draw();
            return;
        }
        DrawTexturePro(Texture, Source4, Destination4 with { Position = Destination4.Position + new Vector2(MathF.Sin(Box.GetTime()), MathF.Cos(Box.GetTime() * 1.5f)) * 16 * Runtime.CurrentRuntime.ScaleF }, Origin, MathF.Sin(Box.GetTime()) * 1.5f, Rgba.White with {A=Helper.TimeToTransparency(StateX(1.5f))});
        DrawTexturePro(Texture, Source1, Destination1, Vector2.Zero, 0, Rgba.White with {A=Helper.TimeToTransparency(State)});
        DrawTexturePro(Texture, Source2, Destination2, Vector2.Zero, 0, Rgba.White with {A=Helper.TimeToTransparency(StateX(.5f))});
        DrawTexturePro(Texture, Source3, Destination3, Vector2.Zero, 0, Rgba.White with {A=Helper.TimeToTransparency(StateX(1))});
        base.Draw();
    }
}
using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class ItemGetBorderLineOverlay(GameBox box) : GameplayOverlay(box, 0.5f, 5)
{
    TextureHandle Texture = Runtime.CurrentRuntime.Textures["item-get-border-line.png"];
    private Rect Source = new Rect(0, 0, 1536, 512);
    private Rect Destination = new Rect(0, 0, new Vector2(384, 128)*Runtime.CurrentRuntime.ScaleF);
    
    protected override void Draw()
    {
        DrawTexturePro(Texture, Source, Destination, Vector2.Zero, 0, Rgba.White with {A=Helper.TimeToTransparency(State)});
        base.Draw();
    }
}
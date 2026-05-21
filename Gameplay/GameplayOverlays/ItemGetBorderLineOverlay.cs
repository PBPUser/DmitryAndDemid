using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class ItemGetBorderLineOverlay(GameBox box) : GameplayOverlay(box, 0.5f, 5)
{
    Texture2D Texture = Runtime.CurrentRuntime.Textures["item-get-border-line.png"];
    private Rectangle Source = new Rectangle(0, 0, 1536, 512);
    private Rectangle Destination = new Rectangle(0, 0, new Vector2(384, 128)*Runtime.CurrentRuntime.ScaleF);
    
    protected override void Draw()
    {
        Raylib.DrawTexturePro(Texture, Source, Destination, Vector2.Zero, 0, Color.White with {A=Helper.TimeToTransparency(State)});
        base.Draw();
    }
}
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Backgrounds;

public class DrogichinBackground : StageBackground
{
    private static DrogichinPoint[] Points =
    {
        new DrogichinPoint(234, 811, 0,-90-Helper.FindAngleDegrees(new Vector2(234,811), new Vector2(401,887))),
        new DrogichinPoint(401, 887, 100,-90-Helper.FindAngleDegrees(new Vector2(234,811), new Vector2(401,887))),
        new DrogichinPoint(581, 953, 200, -90-Helper.FindAngleDegrees(new Vector2(401,887), new Vector2(581,953))),
        new DrogichinPoint(830, 941, 300, -90-Helper.FindAngleDegrees(new Vector2(581,953), new Vector2(830,941))),
        new DrogichinPoint(1048, 927, 400, -90-Helper.FindAngleDegrees(new Vector2(830,941), new Vector2(1048,927))),
        new DrogichinPoint(1144, 926, 500, -90-Helper.FindAngleDegrees(new Vector2(1048,927), new Vector2(1144,926))),
        new DrogichinPoint(2208, 726, 660, -90-Helper.FindAngleDegrees(new Vector2(1144,926), new Vector2(2208,726))),
        new DrogichinPoint(2208, 707, 690, -90-Helper.FindAngleDegrees(new Vector2(2208,707), new Vector2(2258,255))),
        new DrogichinPoint(2258, 255, 800, -90-Helper.FindAngleDegrees(new Vector2(2258,255), new Vector2(2258,255))),
        
    };

    private static int LastTick = 0;
    
    static DrogichinBackground()
    {
        Source = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["drogichinmap.png"]);
        LastTick = Points.OrderBy(x => x.Tick).Last().Tick;
    }
    
    public DrogichinBackground()
    {
        Dest = new Rectangle(192, 224, Source.Width, Source.Height);
    }

    private static Rectangle Source;
    private Rectangle Dest;

    DrogichinPoint Get(int tick)
    {
        var p1 = Points.Where(x => x.Tick <= (tick%LastTick)).Last();
        var p2 = Points[(Points.IndexOf(p1) + 1 % Points.Length)];
        
        return DrogichinPoint.GetPointBetween(p1, p2, tick%LastTick);
    }
    
    protected override void Render(RenderTexture2D texture, int tick)
    {
        var point = Get(tick);
        //DrawTexture(Runtime.CurrentRuntime.Textures["drogichinmap.png"], 0, 0, Color.White);
        DrawTexturePro(
            Runtime.CurrentRuntime.Textures["drogichinmap.png"],
            Source, Dest, new Vector2(point.X, point.Y), point.Rotation, Color.White);
    }

    class DrogichinPoint
    {
        public int X = 0;
        public int Y = 0;
        public int Tick = 0;
        public float Rotation = 0;
        
        public DrogichinPoint(int x, int y, int tick, float rotation)
        {
            X = x;
            Y = y;
            Tick = tick;
            Rotation = rotation;
        }

        public static DrogichinPoint GetPointBetween(DrogichinPoint a, DrogichinPoint b, int tick)
        {
            float s = ((float)tick - a.Tick) / (float)(b.Tick - a.Tick);
            int X = (int)(s * (b.X - a.X) + a.X);
            int Y = (int)(s * (b.Y - a.Y) + a.Y);
            float rotation = s * (b.Rotation - a.Rotation) + a.Rotation;
            return new DrogichinPoint(X, Y, tick, rotation);
        }
    }
}
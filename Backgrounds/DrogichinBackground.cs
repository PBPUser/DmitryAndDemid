using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

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
    
    private static ShaderHandle DrogichinCloudsShader;
    private static int LastTick = 0;
    
    static DrogichinBackground()
    {
        Source = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["drogichinmap.png"]);
        LastTick = Points.OrderBy(x => x.Tick).Last().Tick;
    }
    
    public DrogichinBackground()
    {
        Temp = LoadRenderTexture(384, 448);
        DrogichinCloudsShader = Runtime.CurrentRuntime.Shaders["drogichin_clouds"];
        LocationDrogichinCloudsTime = GetShaderLocation(DrogichinCloudsShader, "time");
        LocationDrogichinCloudsTime = GetShaderLocation(DrogichinCloudsShader, "rotation");
        Dest = new Rect(192, 224, Source.Width, Source.Height);
    }

    private int LocationDrogichinCloudsTime;
    private int LocationDrogichinCloudsRotation;
    private static Rect Source;
    private Rect Dest;

    DrogichinPoint Get(int tick, float delta)
    {
        var p1 = Points.Where(x => x.Tick <= (tick%LastTick)).Last();
        var p2 = Points[(Array.IndexOf(Points, p1) + 1 % Points.Length)];
        
        return DrogichinPoint.GetPointBetween(p1, p2, tick%LastTick, delta);
    }

    private RenderedTexture Temp;
    
    protected override void Update(int tick, float delta)
    {
        var point = Get(tick, delta);
        Rotation = point.Rotation;
        BeginTextureMode(Temp);
        DrawTexturePro(
            Runtime.CurrentRuntime.Textures["drogichinmap.png"],
            Source, Dest, new Vector2(point.X, point.Y), point.Rotation, Rgba.White);
        EndTextureMode();
    }

    private float Rotation;

    protected override void Render(RenderedTexture texture, int tick, float delta)
    {
        SetShaderValue(DrogichinCloudsShader, LocationDrogichinCloudsRotation, Rotation, UniformType.Float);
        SetShaderValue(DrogichinCloudsShader, LocationDrogichinCloudsTime, tick / 60f + delta, UniformType.Float);
        BeginShaderMode(DrogichinCloudsShader);
        DrawTexturePro(Temp.Texture, Helper.GetFullSourceRenderTexture(Temp), new Rect(0,0,384,448),Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
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

        public static DrogichinPoint GetPointBetween(DrogichinPoint a, DrogichinPoint b, int tick, float delta)
        {
            float s = ((float)tick - a.Tick) / (float)(b.Tick - a.Tick) + delta;
            int x = (int)(s * (b.X - a.X) + a.X);
            int y = (int)(s * (b.Y - a.Y) + a.Y);
            float rotation = s * (b.Rotation - a.Rotation) + a.Rotation;
            return new DrogichinPoint(x, y, tick, rotation);
        }
    }
}
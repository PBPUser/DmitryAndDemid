using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Backgrounds;

/// <summary>
/// The Extra stage: a night flight down the avenue of a modern city, weaving between its glass towers — lit
/// window grids, red roof beacons, sodium lamps along the kerbs, the moon over it all. The whole scene is
/// raymarched in the <c>city_flyover.fs</c> fragment shader; this class just drives its <c>time</c> uniform
/// and blits a full-screen quad through it, the same pattern as <see cref="DrogichinFlyoverBackground"/>.
/// Low graphics skips the shader (it is a heavy per-pixel pass) for a flat night fill.
/// </summary>
public class CityFlyoverBackground : StageBackground
{
    private const int Width = 384, Height = 448;

    private readonly ShaderHandle Shader;
    private readonly int LocationTime;

    public CityFlyoverBackground()
    {
        Shader = Runtime.CurrentRuntime.Shaders["city_flyover"];
        LocationTime = GetShaderLocation(Shader, "time");
    }

    protected override void Render(RenderedTexture texture, int tick, float delta)
    {
        if (!Configuration.Config.HighGraphics)
        {
            DrawRectangle(0, 0, Width, Height, new Rgba(10, 10, 24, 255));
            return;
        }
        SetShaderValue(Shader, LocationTime, tick / 60f + delta, UniformType.Float);
        BeginShaderMode(Shader);
        DrawProceduralQuad(new Rect(0, 0, Width, Height));
        EndShaderMode();
    }
}

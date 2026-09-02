using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Backgrounds;

/// <summary>
/// Stage 1: Drogichin, opening on the square in front of the district executive committee as in the photo on
/// the town's Wikipedia page (the paved square, the Lenin statue, the white committee building with its red
/// roof, columns and flag, the cypresses, the main street off to the right), then lifting off over the street,
/// climbing through the cloud deck and cruising out over the real town. The ground is the town's
/// OpenStreetMap extract rasterised into <c>drogichin_osm.png</c> (Tools/rasterize_drogichin.py; map data
/// (c) OpenStreetMap contributors, ODbL) — streets, building footprints and storeys, greens and water — which
/// the <c>drogichin_flyover.fs</c> shader extrudes and marches. This class drives the shader's <c>time</c>
/// uniform and hands it the map through the quad, the same pattern as <see cref="HousesBackground"/>.
/// </summary>
public class DrogichinFlyoverBackground : StageBackground
{
    private readonly ShaderHandle Shader;
    private readonly int LocationTime;
    /// <summary>The town, rasterised from OpenStreetMap (Tools-side script; see the shader's header for the
    /// channel layout). Handed to the shader as texture0 through the quad it draws.</summary>
    private readonly BasicTexture Map;

    public DrogichinFlyoverBackground()
    {
        Shader = Runtime.CurrentRuntime.Shaders["drogichin_flyover"];
        LocationTime = GetShaderLocation(Shader, "time");
        Map = Runtime.CurrentRuntime.Textures["drogichin_osm.png"];
    }

    protected override void Render(RenderedTexture texture, int tick, float delta)
    {
        SetShaderValue(Shader, LocationTime, tick / 60f + delta, UniformType.Float);
        BeginShaderMode(Shader);
        DrawProceduralQuad(new Rect(0, 0, 384, 448), Map);
        EndShaderMode();
    }
}

using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Backgrounds;

/// <summary>
/// A cinematic approach to Drogichin: the camera flies low along a countryside road, climbs into a
/// cloud layer that whites the screen out, then emerges high and looks down over the town grid
/// (streets, red roofs, greenery, the central avenue). The whole move lives in the
/// <c>drogichin_flyover.fs</c> fragment shader — this class just drives its <c>time</c> uniform and
/// blits a full-screen quad through it, the same pattern as <see cref="HousesBackground"/>.
/// </summary>
public class DrogichinFlyoverBackground : StageBackground
{
    private readonly TargetHandle Temp;
    private readonly ShaderHandle Shader;
    private readonly int LocationTime;

    public DrogichinFlyoverBackground()
    {
        Temp = LoadRenderTexture(384, 448);
        Shader = Runtime.CurrentRuntime.Shaders["drogichin_flyover"];
        LocationTime = GetShaderLocation(Shader, "time");
    }

    protected override void Render(TargetHandle texture, int tick, float delta)
    {
        SetShaderValue(Shader, LocationTime, tick / 60f + delta, UniformType.Float);
        BeginShaderMode(Shader);
        DrawTexturePro(Temp.Texture, Helper.GetFullSourceRenderTexture(Temp),
            new Rect(0, 0, 384, 448), Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
    }

    protected override void Unload()
    {
        UnloadRenderTexture(Temp);
        base.Unload();
    }
}

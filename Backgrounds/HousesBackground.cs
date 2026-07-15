using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Backgrounds;

/// <summary>
/// An endless field of low houses seen from a camera flying ~9 m above the ground.
/// The engine only draws in 2D, so the perspective city lives entirely in the
/// <c>houses.fs</c> fragment shader; this class just drives its <c>time</c> uniform and
/// blits a full-screen quad through it (same pattern as <see cref="DrogichinBackground"/>).
/// </summary>
public class HousesBackground : StageBackground
{
    private readonly TargetHandle Temp;
    private readonly ShaderHandle Shader;
    private readonly int LocationTime;

    public HousesBackground()
    {
        Temp = LoadRenderTexture(384, 448);
        Shader = Runtime.CurrentRuntime.Shaders["houses"];
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

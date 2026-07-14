using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;

namespace DmitryAndDemid.Gameplay.Effects;

public class GrazeScreenEffect : GameplayScreenEffect
{
    public GrazeScreenEffect(GameBox box, Vector2 position, int index, float timeAppear, float timeDisappear, float angle) 
        : base(box, position, index, "graze_particle", timeAppear, timeDisappear)
    {
        Angle = angle;
        LocationAngle = GetShaderLocation(Shader, "angle");
        Layer = EffectLayer.BackgroundAndGameplay;
    }

    public int LocationAngle;
    public float Angle;

    public override void ApplyShading(float gameTime)
    {
        SetShaderValue(Shader, LocationAngle, Angle, UniformType.Float);
        base.ApplyShading(gameTime);
    }
}
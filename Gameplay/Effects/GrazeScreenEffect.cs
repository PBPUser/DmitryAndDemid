using System.Numerics;
using DmitryAndDemid.Common;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.Effects;

public class GrazeScreenEffect : GameplayScreenEffect
{
    public GrazeScreenEffect(GameBox box, Vector2 position, int index, float timeAppear, float timeDisappear, float angle) 
        : base(box, position, index, "graze_particle", timeAppear, timeDisappear)
    {
        Angle = angle;
        LocationAngle = Raylib.GetShaderLocation(Shader, "angle");
        Layer = EffectLayer.BackgroundAndGameplay;
    }

    public int LocationAngle;
    public float Angle;

    public override void ApplyShading(float gameTime)
    {
        Raylib.SetShaderValue(Shader, LocationAngle, Angle, ShaderUniformDataType.Float);
        base.ApplyShading(gameTime);
    }
}
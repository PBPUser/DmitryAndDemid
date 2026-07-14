using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay.Effects;

public class StrengthScreenEffect : GameplayScreenEffect
{
    public StrengthScreenEffect(GameBox box, Vector2 position, int index, float timeAppear, float timeDisappear, int particlesColor, int circleColor) 
        : base(box, position, index, "strength", timeAppear, timeDisappear)
    {
        LocationParticlesColor = GetShaderLocation(Shader, "particlesColor");
        LocationCircleColor = GetShaderLocation(Shader, "circleColor");
        LocationLeaves = GetShaderLocation(Shader, "textureLeaves");
        LocationTimeStarted = GetShaderLocation(Shader, "timeStarted");
        Layer = EffectLayer.BackgroundAndGameplay;
        TimeCreated = box.GetTime();
        ParticlesColor = Helper.ColorIntToVector3(particlesColor).AsVector4().WithElement(3, 1);
        CircleColor = Helper.ColorIntToVector3(circleColor).AsVector4().WithElement(3, 1);
    }

    int LocationLeaves, LocationTimeStarted, LocationParticlesColor, LocationCircleColor;

    float TimeCreated;
    Vector4 CircleColor, ParticlesColor;

    public override void ApplyShading(float gameTime)
    {
        SetShaderValue(Shader, LocationParticlesColor, ParticlesColor, UniformType.Vec4);
        SetShaderValue(Shader, LocationCircleColor, CircleColor, UniformType.Vec4);
        SetShaderValue(Shader, LocationTimeStarted, TimeCreated, UniformType.Float);
        SetShaderValueTexture(Shader, LocationLeaves, Runtime.CurrentRuntime.Textures["vilkaCut.png"]);
        base.ApplyShading(gameTime);
    }
}
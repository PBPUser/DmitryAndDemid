using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.Effects;

public class EntityDeathScreenEffect : GameplayScreenEffect
{
    public EntityDeathScreenEffect(GameBox box, Vector2 position, int index, float timeAppear, float timeDisappear, int particlesColor, int circleColor) 
        : base(box, position, index, "entity_die", timeAppear, timeDisappear)
    {
        LocationParticlesColor = Raylib.GetShaderLocation(Shader, "particlesColor");
        LocationCircleColor = Raylib.GetShaderLocation(Shader, "circleColor");
        LocationLeaves = Raylib.GetShaderLocation(Shader, "textureLeaves");
        LocationTimeStarted = Raylib.GetShaderLocation(Shader, "timeStarted");
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
        Raylib.SetShaderValue(Shader, LocationParticlesColor, ParticlesColor, ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(Shader, LocationCircleColor, CircleColor, ShaderUniformDataType.Vec4);
        Raylib.SetShaderValue(Shader, LocationTimeStarted, TimeCreated, ShaderUniformDataType.Float);
        Raylib.SetShaderValueTexture(Shader, LocationLeaves, Runtime.CurrentRuntime.Textures["vilkaCut.png"]);
        base.ApplyShading(gameTime);
    }
}
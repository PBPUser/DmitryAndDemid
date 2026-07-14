using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.Effects;

public class ShakeScreenEffect : GameplayScreenEffect
{
    public ShakeScreenEffect(GameBox b, float strength, float speed, int i, float appear, float duration) 
        : base(b, Vector2.Zero, i, "shake", appear, duration)
    {
        Strength = strength;
        Speed = speed;
        ShakeStrengthLocation = GetShaderLocation(Shader, "shakeStrength");
        ShakeSpeedLocation = GetShaderLocation(Shader, "shakeSpeed");
        Layer = EffectLayer.BackgroundAndGameplay;
    }

    private int ShakeStrengthLocation;
    private int ShakeSpeedLocation;
    public float Speed;
    public float Strength;

    public override void ApplyShading(float gameTime)
    {
        SetShaderValue(Shader, ShakeStrengthLocation, Strength, UniformType.Float);
        SetShaderValue(Shader, ShakeSpeedLocation, Speed, UniformType.Float);
        base.ApplyShading(gameTime);
    }
}
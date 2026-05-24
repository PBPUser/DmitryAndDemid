using System.Numerics;
using DmitryAndDemid.Common;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Gameplay.Effects;

public class DarkStrengthScreenEffect : GameplayScreenEffect
{
    public DarkStrengthScreenEffect(GameBox b, Vector2  offset, Vector2 posTo, int mask, int i, float a, float d) 
        : base(b, posTo, i, "dark_strength", a, d)
    {
        Offset = offset;
        LDSOffset = GetShaderLocation(Shader, "offset");
        LDSMask = GetShaderLocation(Shader, "mask");
        Layer = EffectLayer.BackgroundOnly;
        Mask = mask;
    }

    private int LDSOffset;
    private int LDSMask;
    private int Mask;
    private Vector2 Offset;

    public override string ToString()
    {
        return $"Dark Strength [{Offset}] [{Mask}]";
    }

    public override void ApplyShading(float gameTime)
    {
        SetShaderValue(Shader, LDSMask, Mask, ShaderUniformDataType.Int);
        SetShaderValue(Shader, LDSOffset, Offset, ShaderUniformDataType.Vec2);
        base.ApplyShading(gameTime);
    }
}
using System.Numerics;
using DmitryAndDemid.Common;
using Raylib_cs;
using static Raylib_cs.Raylib;


namespace DmitryAndDemid.Gameplay.Effects;

public class AkobSpellScreenEffect : GameplayScreenEffect
{
    public AkobSpellScreenEffect(GameBox box, Vector2 position, int index, float timeAppear, float timeDisappear) 
        : base(box, position, index, "akobspell", timeAppear, timeDisappear)
    {
        LocationTimeStarted = GetShaderLocation(Shader, "timeStarted"); 
        Layer = EffectLayer.BackgroundOnly;
    }

    private int LocationTimeStarted;

    public override void ApplyShading(float gameTime)
    {
        SetShaderValue(Shader, LocationTimeStarted, TimeAppear, ShaderUniformDataType.Float);
        base.ApplyShading(gameTime);
    }
}
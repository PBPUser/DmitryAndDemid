using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;

namespace DmitryAndDemid.Gameplay.Effects;

public class BossDeathScreenEffect : GameplayScreenEffect
{
    public BossDeathScreenEffect(GameBox box, Vector2 position, int index, float timeAppear, float timeDisappear) 
        : base(box, position, index, "entity_die", timeAppear, timeDisappear)
    {
        LocationLeaves = GetShaderLocation(Shader, "textureLeaves");
        SetShaderValueTexture(Shader, LocationLeaves, Runtime.CurrentRuntime.Textures["vilkaCut.png"]);
        Layer = EffectLayer.BackgroundAndGameplay;
    }

    public int LocationLeaves;

    public override void ApplyShading(float gameTime)
    {
        base.ApplyShading(gameTime);
    }
}
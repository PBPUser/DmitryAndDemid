using System.Numerics;
using DmitryAndDemid.Common;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.Effects;

public class BossDeathScreenEffect : GameplayScreenEffect
{
    public BossDeathScreenEffect(GameBox box, Vector2 position, int index, float timeAppear, float timeDisappear) 
        : base(box, position, index, "entity_die", timeAppear, timeDisappear)
    {
        LocationLeaves = Raylib.GetShaderLocation(Shader, "textureLeaves");
        Layer = EffectLayer.BackgroundAndGameplay;
    }

    public int LocationLeaves;

    public override void ApplyShading(float gameTime)
    {
        Raylib.SetShaderValueTexture(Shader, LocationLeaves, Runtime.CurrentRuntime.Textures["vilkaCut.png"]);
        base.ApplyShading(gameTime);
    }
}
using System.Numerics;
using DmitryAndDemid.Common;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.Effects;

public class SpellCardAttackScreenEffect : GameplayScreenEffect
{
    public SpellCardAttackScreenEffect(GameBox box, Vector2 position, int index, float timeAppear, float timeDisappear) 
        : base(box, position, index, "spellcard_attack", timeAppear, timeDisappear)
    {
        LocationAttackTexture = Raylib.GetShaderLocation(Shader, "textureAttack");
        Raylib.SetShaderValueTexture(Shader, LocationAttackTexture, Runtime.CurrentRuntime.Textures["spellcard-placeholder.png"]);
        Layer = EffectLayer.BackgroundOnly;
    }

    public int LocationAttackTexture;

    public override void ApplyShading(float gameTime)
    {
        base.ApplyShading(gameTime);
    }
}
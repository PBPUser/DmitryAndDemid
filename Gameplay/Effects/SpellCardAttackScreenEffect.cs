using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;


namespace DmitryAndDemid.Gameplay.Effects;

public class SpellCardAttackScreenEffect : GameplayScreenEffect
{
    public SpellCardAttackScreenEffect(GameBox box, Vector2 position, int index, float timeAppear, float timeDisappear) 
        : base(box, position, index, "spellcard_attack", timeAppear, timeDisappear)
    {
        SetShaderValueTexture(Shader, GetShaderLocation(Shader, "textureAttack"), Runtime.CurrentRuntime.Textures["spellcard-placeholder.png"]);
        Layer = EffectLayer.BackgroundOnly;
    }
}
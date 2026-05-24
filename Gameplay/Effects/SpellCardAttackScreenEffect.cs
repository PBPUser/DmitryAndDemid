using System.Numerics;
using DmitryAndDemid.Common;
using Raylib_cs;


namespace DmitryAndDemid.Gameplay.Effects;

public class SpellCardAttackScreenEffect : GameplayScreenEffect
{
    public SpellCardAttackScreenEffect(GameBox box, Vector2 position, int index, float timeAppear, float timeDisappear) 
        : base(box, position, index, "spellcard_attack", timeAppear, timeDisappear)
    {
        Raylib.SetShaderValueTexture(Shader, Raylib.GetShaderLocation(Shader, "textureAttack"), Runtime.CurrentRuntime.Textures["spellcard-placeholder.png"]);
        Layer = EffectLayer.BackgroundOnly;
    }
}
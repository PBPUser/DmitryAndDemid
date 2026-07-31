using System.Numerics;
using DmitryAndDemid.Common;

namespace DmitryAndDemid.Gameplay.Effects;

/// <summary>
/// The mystical toilet's arrival flourish: a large semi-transparent brown circle that collapses onto it, fading
/// as it closes, with lightning striking inward along its rim. Backed by the "toilet_appear" shader;
/// <see cref="GameBox.SpawnMysticalToilet"/> pairs it with a short screen shake.
/// </summary>
public class ToiletAppearScreenEffect : GameplayScreenEffect
{
    private readonly RuntimeObject Toilet;

    public ToiletAppearScreenEffect(GameBox box, RuntimeObject toilet, int index, float timeAppear, float timeDisappear)
        : base(box, toilet.Position, index, "toilet_appear", timeAppear, timeDisappear)
    {
        Toilet = toilet;
        // Behind the bullets and the toilet itself, like the spell-card flourish: the circle closes in BEHIND
        // the sprite that is arriving, and never hides anything the player has to dodge.
        Layer = EffectLayer.BackgroundOnly;
    }

    public override void ApplyShading(float gameTime)
    {
        // Follow the toilet: it starts wandering partway through this effect, and a circle collapsing onto the
        // empty spot it spawned at would read as missing its target.
        Position = Toilet.Position;
        base.ApplyShading(gameTime);
    }
}

using System.Numerics;
using DmitryAndDemid.Common;

namespace DmitryAndDemid.Gameplay.Effects;

/// <summary>
/// A burst of soft, semi-transparent volumetric circles swelling outward from the spell's focus point and
/// fading out over its short life. Spawned when a spell card activates, alongside the "spell card attack"
/// banner — a declaration flourish behind the playfield. Backed by the "circles" shader.
/// </summary>
public class CirclesScreenEffect : GameplayScreenEffect
{
    public CirclesScreenEffect(GameBox box, Vector2 position, int index, float timeAppear, float timeDisappear)
        : base(box, position, index, "circles", timeAppear, timeDisappear)
    {
        // Sit behind the bullets/boss so the emanation reads as a backdrop and never obscures the danger.
        Layer = EffectLayer.BackgroundOnly;
    }
}

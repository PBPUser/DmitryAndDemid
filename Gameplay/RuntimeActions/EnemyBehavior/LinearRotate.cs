using DmitryAndDemid.Common;

namespace DmitryAndDemid.Gameplay.RuntimeActions.EnemyBehavior;

public class LinearRotate : EnemyAction
{
    private float RotationSpeed;

    public override void Act(Enemy enemy)
    {
        enemy.UpdateCollisionRender(enemy.PositionTo, enemy.RotateTo + RotationSpeed);
    }

    public override void Init(string[] values, Game game, Enemy enemy)
    {
        RotationSpeed = float.Parse(values[0]);
    }
}
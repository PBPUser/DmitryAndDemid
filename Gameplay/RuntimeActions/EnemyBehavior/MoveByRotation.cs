using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay.RuntimeActions.EnemyBehavior;

public class MoveByRotation : EnemyAction
{
    public override void Act(Enemy enemy)
    {
        enemy.UpdateCollisionRender(
            enemy.PositionTo + Helper.GetDirection(enemy.RotateTo),
            enemy.RotateTo);
    }
}
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.RuntimeActions.EnemyBehavior;

public class ShootIntoPlayer : EnemyAction
{
    private int Rate;
    private int MaxY;
    private string Visual;
    
    public override void Act(Enemy enemy)
    {
        if (enemy.PositionTo.Y > MaxY)
            return;
        if ((enemy.Game.CurrentTick - enemy.SpawnTick) % Rate != 0)
            return;
        enemy.Game.AddObject(new Bullet(enemy.Game, new BulletSpawnInfo()
        {
            Speed = enemy.BulletSpeed,
            SpawnTick = enemy.Game.CurrentTick,
            BulletActionClass = "MoveByDirection",
            Args = ["UseRotationRad"],
            BulletVisual = Visual,
            Position = enemy.PositionTo,
            Rotation = Helper.FindAngle(enemy.PositionTo, enemy.Game.Player.PositionTo)
        }, 0, true));
    }

    public override void Init(string[] values, Game game, Enemy enemy)
    {
        Rate = int.Parse(values[0]);
        MaxY = int.Parse(values[1]);
        Visual = values[2];
    }
}
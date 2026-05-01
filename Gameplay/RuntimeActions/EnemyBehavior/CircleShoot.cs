using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.RuntimeActions.EnemyBehavior;

public class CircleShoot : EnemyAction
{
    private int Rate;
    private int BulletsCount;
    private bool TargetPlayer;
    private float RotationStart;
    private string Visual;
    
    public override void Act(Enemy enemy)
    {
        int tick = enemy.Game.CurrentTick - enemy.SpawnTick;
        if (tick % Rate != 0)
            return;
        float angle = TargetPlayer ? Raymath.Vector2Angle(enemy.PositionTo, enemy.Game.Player.PositionTo) : RotationStart;
        float angleDif = MathF.PI *2 / BulletsCount;
        for(int i = 0; i < BulletsCount; i++)
            enemy.Game.AddObject(new Bullet(enemy.Game, new BulletSpawnInfo()
            {
                Speed = enemy.BulletSpeed,
                SpawnTick = enemy.Game.CurrentTick,
                BulletActionClass = "MoveByDirection",
                Args = ["UseRotation"],
                BulletVisual = Visual,
                Position = enemy.PositionTo,
                Rotation = angle + angleDif * i
            }, 0, true));
    }

    public override void Init(string[] values, Game game, Enemy enemy)
    {
        Rate = int.Parse(values[0]);
        BulletsCount = int.Parse(values[1]);
        Visual =  values[2];
        TargetPlayer = bool.Parse(values[3]);
        if (!TargetPlayer)
            RotationStart = float.Parse(values[4]);
        
    }
}
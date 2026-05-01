using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.RuntimeActions.BulletBehavior;

public class MoveByDirection : BulletAction
{
    public Vector2 Direction;
    
    public override void Act(Bullet bullet)
    {
        bullet.UpdateCollisionRender(bullet.PositionTo + Direction, bullet.RotateTo);
    }

    public override void Init(string[] values, Game game, Bullet bullet)
    {
        switch (values[0])
        {
            case "WriteDirectionToPlayer":
                var r = Raymath.Vector2Angle(bullet.PositionTo, bullet.Game.Player.PositionTo);
                Direction = Helper.GetDirection(bullet.PositionTo, game.Player.PositionTo) * bullet.Speed;
                bullet.UpdateCollisionRender(bullet.PositionTo, r);
                return;
            case "SetValue":
                Direction = Helper.GetDirection(float.Parse(values[1])+MathF.PI/2) * bullet.Speed;
                return;
            case "UseRotation":
                Direction = Helper.GetDirection(bullet.RotateTo+MathF.PI/2) * bullet.Speed;
                return;
        }
    }
}
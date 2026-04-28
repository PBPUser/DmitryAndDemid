using System.Numerics;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class Bullet : RuntimeObject
{
    private static Dictionary<string, Action<RuntimeObject>> BulletUpdateActions = new();
    public bool PlayerShoot = false;
    
    static Bullet()
    {
        BulletUpdateActions["MoveByDirection"] = obj =>
        {
            obj.UpdateCollisionRender(obj.PositionTo + (Vector2)(obj.Dictionary["Direction"]),0);
        };
        BulletUpdateActions["WriteDirectionToPlayer"] = obj =>
        {
            obj.Dictionary["Direction"] = Helper.GetDirection(obj.PositionTo, obj.Game.Player.PositionTo) * obj.Speed;
        };
        BulletUpdateActions["WriteAngularDirection"] = obj =>
        {
            obj.Dictionary["Direction"] = Helper.GetDirection(obj.RotateTo+MathF.PI/2) * obj.Speed;
        };
    }
    
    public Bullet(Game game, BulletSpawnInfo info, int numberInStack) : this(game,
        info.Position,
        BulletVisual.Constants[info.BulletVisual].RenderSize,
        BulletVisual.Constants[info.BulletVisual].Collision)
    {
        Damage = info.Damage;
        RotateTo = info.Rotation;
        var constant = BulletVisual.Constants[info.BulletVisual];
        UpdateScript = BulletUpdateActions[info.BulletUpdateMethod];
        if(BulletUpdateActions.ContainsKey(info.BulletCreateMethod))
            CreateScript = BulletUpdateActions[info.BulletCreateMethod];
        Speed = info.Speed;
        SpawnTick = numberInStack * info.StackDelay + info.SpawnTick;
        SourceTexture = Runtime.CurrentRuntime.Textures[constant.Texture];
        SourceRect = new Rectangle(constant.SourcePosition,
            constant.SourceSize == null ?  constant.RenderSize :  constant.SourceSize!.Value);
        UpdateCollisionRender(PositionTo+(info.StackPositionOffset*numberInStack), RotateTo);
    }

    public static Bullet CreateInGame(Game game, BulletSpawnInfo bulletSpawnInfo, int spawnTick)
    { 
        Bullet bullet = new (game, bulletSpawnInfo, 0);
        bullet.SpawnTick = spawnTick;
        return bullet;
    }

    Bullet(Game game, Vector2 pos, Vector2 renderSize, Vector2 collisionSize) : base(game, pos, renderSize,
        collisionSize) {
        
    }

    public float Damage = 0;
    
    public override void Update()
    {
        base.Update();
        if (!CollisionEnabled)
            return;
        if (PlayerShoot)
        {
            foreach (var o in Game.Objects.Where(x => x.Attackable))
                if (Helper.IsCollied(Collision, o.Collision))
                {
                    Game.Score = Game.Score+ 10;
                    o.Attack(Damage);
                    Helper.PlaySound(Runtime.CurrentRuntime.Sounds["damage"]);
                    Game.RemoveObject(this);
                }
            return;
        }
        if (!Game.Player.CollisionEnabled)
            return;
        if(Helper.IsCollied(Game.Player.Collision, Collision))
            Game.Player.Die();
    }
}
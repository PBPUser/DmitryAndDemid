using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class Bullet : RuntimeObject
{
    private BulletAction? RuntimeAction = null;
    private static Dictionary<string, Action<RuntimeObject>> BulletUpdateActions = new();
    public bool PlayerShoot = false;
    
    static Bullet()
    {
        BulletUpdateActions["MoveByDirection"] = obj =>
        {
            obj.UpdateCollisionRender(obj.PositionTo + (Vector2)(obj.Dictionary["Direction"]),obj.RotateTo);
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
    
    public void SetCollectableState()
    {
        Alpha = 128;
        InCollectableState = true;
        UseVelocity = true;
    }
    
    public Bullet(Game game, BulletSpawnInfo info, int numberInStack, bool transferable) : this(game,
        info.Position,
        BulletVisual.Constants[info.BulletVisual].RenderSize,
        BulletVisual.Constants[info.BulletVisual].Collision)
    {
        TransferableInCollectableState = transferable;
        Damage = info.Damage;
        RotateTo = info.Rotation;
        var constant = BulletVisual.Constants[info.BulletVisual];
        Speed = info.Speed;
        SpawnTick = numberInStack * info.StackDelay + info.SpawnTick;
        SourceTexture = Runtime.CurrentRuntime.Textures[constant.Texture];
        SourceRect = new Rectangle(constant.SourcePosition,
            constant.SourceSize == null ?  constant.RenderSize :  constant.SourceSize!.Value);
        UpdateCollisionRender(PositionTo+(info.StackPositionOffset*numberInStack), info.Rotation);
        if (BulletAction.Actions.TryGetValue(info.BulletActionClass, out var action))
        {
            RuntimeAction = (BulletAction)action.GetConstructors().First().Invoke([]);
            RuntimeAction.Init(info.Args, game, this);
        }
    }

    Bullet(Game game, Vector2 pos, Vector2 renderSize, Vector2 collisionSize) : base(game, pos, renderSize,
        collisionSize) {
        
    }

    public float Damage = 0;
    private bool IsGrazed = false;
    public int ScoreWhenCollected = 100;
    
    public override void Update()
    {
        RuntimeAction?.Act(this);
        if (InCollectableState)
        {
            if (Helper.IsCollied(Game.Player.Collision, Collision))
            {
                Game.Score += ScoreWhenCollected;
                Game.RemoveObject(this);
            }
            else
            {
            }
            return;
        }
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
        
        if (Game.Player.CollisionEnabled && Helper.IsCollied(Game.Player.Collision, Collision))
            Game.Player.Die();
        else if (!IsGrazed && Helper.IsCollied(Game.Player.Collision, Collision with { Width = Collision.Width * 16 }))
        {
            Game.Player.Graze++;
            //TODO: Graze sound
            IsGrazed = true;
        }
        base.Update();
    }
}
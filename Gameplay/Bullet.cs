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
    public bool PlayerShoot = false;
    
    
    public void SetCollectableState()
    {
        Alpha = 128;
        InCollectableState = true;
        UseVelocity = true;
        Velocity = -Helper.GetDirection(PositionTo, Game.Player.PositionTo) * 4f ;
    }
    
    public Bullet(Game game, BulletSpawnInfo info, int numberInStack, bool transferable) : this(game,
        info.Position,
        BulletVisual.Constants[info.BulletVisual].RenderSize,
        Vector2.Zero,
        BulletVisual.Constants[info.BulletVisual].Collision)
    {
        IsBullet = true;
        TransferableInCollectableState = transferable;
        Damage = info.Damage;
        RotateTo = info.Rotation;
        var constant = BulletVisual.Constants[info.BulletVisual];
        EffectColor = info.EffectColor;
        Speed = info.Speed;
        SpawnTick = numberInStack * info.StackDelay + info.SpawnTick;
        Effect = constant.Effect;
        if (Effect != "")
        {
            UseEffect = true;
            EffectShader = Runtime.CurrentRuntime.Shaders[Effect];
        }
        SourceTexture = constant.GetTexture(info.EffectColor);
        SourceRect = new Rectangle(constant.GetSourcePosition(info.EffectColor),
            constant.SourceSize == null ?  constant.RenderSize :  constant.GetSourceSize());
        TextureSize = constant.GetSourceSize();
        UpdateCollisionRender(PositionTo+(info.StackPositionOffset*numberInStack), info.Rotation);
        if (BulletAction.Actions.TryGetValue(info.BulletActionClass, out var action))
        {
            RuntimeAction = (BulletAction)action.GetConstructors().First().Invoke([]);
            RuntimeAction.Init(info.Args, game, this);
        }
    }

    Bullet(Game game, Vector2 pos, Vector2 renderSize, Vector2 textureSize, Vector2 collisionSize) : base(game, pos, renderSize, textureSize,
        collisionSize) {
        
    }

    public float Damage = 0;
    private bool IsGrazed = false;
    public int ScoreWhenCollected = 100;
    private const float TransferToCollectableStateSpeed = 0.2f;
    
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
                Velocity = Raymath.Vector2MoveTowards(Velocity,
                    Helper.GetDirection(PositionTo, Game.Player.PositionTo), TransferToCollectableStateSpeed);
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
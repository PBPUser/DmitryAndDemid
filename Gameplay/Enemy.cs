using System.Numerics;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class Enemy : RuntimeObject
{
    private static Dictionary<string, Action<RuntimeObject>> Actions = new();
    static Enemy()
    {
        Actions["MoveLinearDown"] = a => a.UpdateCollisionRender(a.PositionTo+new Vector2(0,a.Speed), a.RotateTo);
        Actions["ShootIntoPlayer"] = a =>
        {
            if (a.Game.Difficulty == 0)
                return;
            var enemy = (a as Enemy);
            if (a.Game.CurrentTick % (enemy.BulletSpawnRate) == 0)
            {
                a.Game.AddObject(new Bullet(a.Game, new BulletSpawnInfo()
                {
                    Speed = enemy.BulletSpeed * a.Game.Difficulty,
                    SpawnTick = a.Game.CurrentTick,
                    BulletCreateMethod = "WriteDirectionToPlayer",
                    BulletVisual = "default",
                    BulletUpdateMethod = "MoveToPlayer",
                    Position = a.PositionTo
                }, 0));
            }
        };
        Actions["MoveLinearDownRight"] = a =>
        {
            a.UpdateCollisionRender(a.PositionTo+new Vector2(a.Speed,a.Speed), a.RotateTo);
        };
        Actions["MoveLinearDownLeft"] = a =>
        {
            a.UpdateCollisionRender(a.PositionTo+new Vector2(-a.Speed,a.Speed), a.RotateTo);
        };
    }
    
    public Enemy(Game game, EnemySpawnInfo info, int numberInStack) : base(game, info.Position, 
        EntityVisual.Visuals[info.Visual].RenderSize,
        EntityVisual.Visuals[info.Visual].Collision)
    {
        if(Actions.ContainsKey(info.Script))
            UpdateScript = Actions[info.Script];
        if(Actions.ContainsKey(info.CreateScript))
            CreateScript = Actions[info.CreateScript];
        if (Actions.ContainsKey(info.AttackScript))
            AttackScript = Actions[info.AttackScript];
        Speed = info.Speed;
        BulletSpeed = info.BulletSpeed;
        BulletSpawnRate = info.BulletSpawnRate;
        SpawnTick = (info.StackDelay * numberInStack) + info.SpawnTick;
        SourceTexture = Runtime.CurrentRuntime.Textures[EntityVisual.Visuals[info.Visual].Texture];
        SourceRect = new Rectangle(EntityVisual.Visuals[info.Visual].SourcePosition, EntityVisual.Visuals[info.Visual].RenderSize);
        UpdateCollisionRender((info.StackPositionOffset * numberInStack) + PositionTo, RotateTo);
    }

    public float BulletSpeed;
    public float BulletSpawnRate;
    public Action<RuntimeObject>? AttackScript { get; set; }

    public override void Update()
    {
        AttackScript?.Invoke(this);
    }
}

using System.Numerics;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Gameplay.Collectables;
using DmitryAndDemid.Utils;
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
            if (a.PositionTo.Y > 320)
                return;
            if (a.Game.Difficulty == 0)
                return;
            var enemy = (a as Enemy);
            if (a.Game.CurrentTick % (enemy.BulletSpawnRate * (5-a.Game.Difficulty)) == 0)
            {
                a.Game.AddObject(new Bullet(a.Game, new BulletSpawnInfo()
                {
                    Speed = enemy.BulletSpeed,
                    SpawnTick = a.Game.CurrentTick,
                    BulletCreateMethod = "WriteDirectionToPlayer",
                    BulletVisual = "default",
                    BulletUpdateMethod = "MoveByDirection",
                    Position = a.PositionTo
                }, 0, true));
            }
        };
        Actions["ShootDirectional"] = a =>
        {
            if (a.PositionTo.Y > 320)
                return;
            if (a.Game.Difficulty == 0)
                return;
            var enemy = (a as Enemy);
            if (a.Game.CurrentTick % (enemy.BulletSpawnRate) == 0)
            {
                float startingPoint = -enemy.ShootStreamCount / 2f * enemy.AngleBetweenStreams + enemy.RotateTo; 
                for(int i = 0; i < enemy.ShootStreamCount; i++)
                    a.Game.AddObject(new Bullet(a.Game, new BulletSpawnInfo()
                    {
                        Speed = enemy.BulletSpeed * a.Game.Difficulty,
                        SpawnTick = a.Game.CurrentTick,
                        BulletCreateMethod = "WriteAngularDirection",
                        BulletVisual = "default",
                        BulletUpdateMethod = "MoveByDirection",
                        Position = a.PositionTo,
                        Rotation = startingPoint + (i * enemy.AngleBetweenStreams)
                    }, 0, true));
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
        Actions["TargetLinear"] = a =>
        {
            var enemy = (a as Enemy);
            Vector2 pos = Vector2.Zero;
            if(enemy.PositionTo != enemy.Target)
            {
                pos = Raymath.Vector2MoveTowards(a.PositionTo, enemy.Target, a.Speed) - a.PositionTo;
            }
            else if (enemy.DislocationOnTargetDuration > a.Game.CurrentTick - a.SpawnTick)
                return;
            else
                pos = Raymath.Vector2MoveTowards(a.PositionTo, enemy.Final, a.Speed) - a.PositionTo;
            a.UpdateCollisionRender(
                enemy.PositionTo + pos,
                0);
        };
    }

    private EnemySpawnInfo Info;
    
    public Enemy(Game game, EnemySpawnInfo info, int numberInStack) : base(game, info.Position, 
        EntityVisual.Visuals[info.Visual].RenderSize,
        EntityVisual.Visuals[info.Visual].Collision)
    {
        Info = info;
        Attackable = true;
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
        Target = info.PositionTarget;
        DislocationOnTargetDuration = info.DislocationOnTargetDuration;
        Final = info.FinalPosition;
        Health = info.Health;
        ShootStreamCount = info.ShootStreamCount;
        AngleBetweenStreams = info.AngleBetweenStreams / 180 * MathF.PI;
        UpdateCollisionRender((info.StackPositionOffset * numberInStack) + PositionTo, RotateTo);
    }

    public Vector2 Target = Vector2.Zero, Final = Vector2.Zero;
    public int PathToTargetDuration = 0;
    public int DislocationOnTargetDuration = 0;
    public int PathFromTargetDuration = 0;
    public int ShootStreamCount = 0;
    public float AngleBetweenStreams = 0f;

    public float Health
    {
        get => health;
        set
        {
            if (health == value)
                return;
            health = value;
            if (health <= 0)
            {
                Collectable[] collectables = new Collectable[
                    Info.DropPowerPointsCount + Info.DropLargePowerPointsCount +
                    Info.DropScorePointsCount
                ];
                float angDif = MathF.PI * 2 / collectables.Length;
                for (int i = 0; i < Info.DropPowerPointsCount; i++)
                {
                    collectables[i] = new PowerCollectable(Game, PositionTo, 
                        Helper.GetDirection(MathF.PI+i*angDif));
                }
                int j = Info.DropPowerPointsCount;
                for (int i = 0; i < Info.DropLargePowerPointsCount; i++)
                {
                    collectables[i+j] = new LargePowerCollectable(Game, PositionTo, 
                        Helper.GetDirection(MathF.PI+i*angDif));
                }
                j+=Info.DropLargePowerPointsCount;
                for (int i = 0; i < Info.DropScorePointsCount; i++)
                {
                    collectables[i+j] = new ScoreCollectable(Game, PositionTo, 
                        Helper.GetDirection(MathF.PI+i*angDif));
                }
                j+=Info.DropScorePointsCount;
                foreach (var collectable in collectables)
                    Game.AddObject(collectable);
                Game.RemoveObject(this);
            }
        }
    }

    private float health = 0f;
    
    public float BulletSpeed;
    public float BulletSpawnRate;
    public Action<RuntimeObject>? AttackScript { get; set; }

    public override void Update()
    {
        AttackScript?.Invoke(this);
    }

    public override void Attack(float damage)
    {
        Health -= damage;
        base.Attack(damage);
    }
}

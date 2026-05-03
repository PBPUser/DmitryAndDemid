using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Gameplay.Collectables;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class Enemy : RuntimeObject
{
    static Enemy()
    {
    }

    private EnemySpawnInfo Info;

    public Enemy(Game game, EnemySpawnInfo info, int numberInStack) : base(game, info.Position,
        EntityVisual.Visuals[info.Visual].RenderSize,
        Helper.GetSize(Runtime.CurrentRuntime.Textures[EntityVisual.Visuals[info.Visual].Texture]),
        EntityVisual.Visuals[info.Visual].Collision)
    {
        Info = info;
        Attackable = true;
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
        UpdateCollisionRender((info.StackPositionOffset * numberInStack) + info.Position, Helper.ToRadians(info.Rotation + info.StackRotationOffset * numberInStack));
        EnemyActions = new EnemyAction[info.Actions.Length];
        for(int i =0;i<info.Actions.Length;i++)
        {
            if (EnemyAction.Actions.TryGetValue(info.Actions[i].Class, out var j))
            {
                EnemyActions[i] = (EnemyAction)j.GetConstructors().First(x => x.GetParameters().Length == 0).Invoke([]);
                EnemyActions[i].Init(info.Actions[i].Args, game, this);
                EnemyActions[i].LimitTicks = info.Actions[i].LimitTicks;
                if (EnemyActions[i].LimitTicks)
                {
                    EnemyActions[i].FromTick = info.Actions[i].FromTick;
                    EnemyActions[i].ToTick = info.Actions[i].ToTick;
                }
            }
        }    
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
                        Helper.GetDirection(MathF.PI + i * angDif));
                }
                int j = Info.DropPowerPointsCount;
                for (int i = 0; i < Info.DropLargePowerPointsCount; i++)
                {
                    collectables[i + j] = new LargePowerCollectable(Game, PositionTo,
                        Helper.GetDirection(MathF.PI + i * angDif));
                }
                j += Info.DropLargePowerPointsCount;
                for (int i = 0; i < Info.DropScorePointsCount; i++)
                {
                    collectables[i + j] = new ScoreCollectable(Game, PositionTo,
                        Helper.GetDirection(MathF.PI + i * angDif));
                }
                j += Info.DropScorePointsCount;
                foreach (var collectable in collectables)
                    Game.AddObject(collectable);
                Game.RemoveObject(this);
            }
        }
    }

    private float health = 0f;

    public float BulletSpeed;
    public float BulletSpawnRate;
    public EnemyAction[] EnemyActions;

    public override void Update()
    {
        foreach (var action in EnemyActions)
            action.InvokeAct(this);
    }

    public override void Attack(float damage)
    {
        Health -= damage;
        base.Attack(damage);
    }
}

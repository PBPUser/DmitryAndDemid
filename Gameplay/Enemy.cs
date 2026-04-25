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
            if (a.Game.CurrentTick % 2 == 0)
            {
                a.Game.AddObject(new Bullet(a.Game, new BulletSpawnInfo()
                {
                    Speed = a.Speed * 3,
                    SpawnTick = a.Game.CurrentTick,
                    BulletCreateMethod = "WriteDirectionToPlayer",
                    BulletVisual = "default",
                    BulletUpdateMethod = "MoveToPlayer",
                    Position = a.PositionTo
                }, 0));
            }
        };
    }
    
    public Enemy(Game game, EnemySpawnInfo info, int numberInStack) : base(game, info.Position, 
        EntityVisual.Visuals[info.Visual].RenderSize,
        EntityVisual.Visuals[info.Visual].Collision)
    {
        Console.WriteLine("Entity created");
        if(Actions.ContainsKey(info.Script))
            UpdateScript = Actions[info.Script];
        if(Actions.ContainsKey(info.CreateScript))
            CreateScript = Actions[info.CreateScript];
        if (Actions.ContainsKey(info.AttackScript))
            AttackScript = Actions[info.AttackScript];
        Speed = info.Speed;
        SpawnTick = (info.StackDelay * numberInStack) + info.SpawnTick;
        SourceTexture = Runtime.CurrentRuntime.Textures[EntityVisual.Visuals[info.Visual].Texture];
        SourceRect = new Rectangle(EntityVisual.Visuals[info.Visual].SourcePosition, EntityVisual.Visuals[info.Visual].RenderSize);
        UpdateCollisionRender((info.StackPositionOffset * numberInStack) + PositionTo, RotateTo);
    }
    
    public Action<RuntimeObject>? AttackScript { get; set; }

    public override void Update()
    {
        AttackScript?.Invoke(this);
    }
}

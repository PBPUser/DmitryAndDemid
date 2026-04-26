using System.Numerics;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class Boss : RuntimeObject
{
    public static Dictionary<string, Action<RuntimeObject>> Actions = new Dictionary<string, Action<RuntimeObject>>();

    static Boss()
    {
        Actions["CreateNikitos1"] = a =>
        {
            Console.WriteLine("Nikitos Creation Script Invoked!");
        };
        Actions["Stay"] = a =>
        {
            Console.WriteLine("s");
        };
        Actions["ShootNikitos1"] = a =>
        {
            Console.WriteLine("should shoot");
        };
    }
    
    public string ID;
    public Action<RuntimeObject>? ShootScript;
    
    public Boss(Game game, BossSpawnInfo info) : base(game, 
        info.Position, info.RenderSize, info.CollisionSize)
    {
        ClearProtected = true;
        ID = info.ID;
        SourceRect = new Rectangle(0, 0, info.RenderSize);
        SourceTexture = Runtime.CurrentRuntime.Textures[info.BossSpriteTexture];
    }

    private static int AppearLength = 30;
    
    public void SetSpawnTick(int tick)
    {
        SpawnTick = tick;
        UpdateCollisionRender(PositionTo, 0);
    }

    public override void Update()
    {
        ShootScript?.Invoke(this);
        base.Update();
    }

    public override (Rectangle destination, float rotation) GetRenderInfo(float state)
    {
        if(Game.CurrentTick - SpawnTick > AppearLength)
            return base.GetRenderInfo(state);
        float j = (float)Helper.ComputeObjectTimeStart(Game.CurrentTick, SpawnTick, AppearLength);
        return (new Rectangle(PositionTo-(j * RenderSize / 2),j * RenderSize), 0);
    }
}
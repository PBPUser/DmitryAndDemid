using System.Numerics;
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Data;

public class Bullet : RuntimeObject
{
    private static Dictionary<string, Action<RuntimeObject>> BulletUpdateActions = new();

    static Bullet()
    {
        BulletUpdateActions["Action1"] = obj =>
        {
            obj.UpdateCollisionRender(obj.PositionTo + new Vector2(0, 1),0);
        };
        BulletUpdateActions["MoveToPlayer"] = obj =>
        {
            obj.UpdateCollisionRender(obj.PositionTo + (Vector2)(obj.Dictionary["DirectionToPlayer"]),0);
        };
        BulletUpdateActions["WritePlayerPosition"] = obj =>
        {
            obj.Dictionary["PlayerPosition"] = obj.Game.Player.PositionTo;
        };
        BulletUpdateActions["WriteDirectionToPlayer"] = obj =>
        {
            obj.Dictionary["DirectionToPlayer"] = Helper.GetDirection(obj.PositionTo, obj.Game.Player.PositionTo) * obj.Speed;
        };
    }
    
    public Bullet(Game game, BulletSpawnInfo info) : this(game,
        new Vector2(info.X, info.Y),
        BulletVisual.Constants[info.BulletVisual].RenderSize,
        BulletVisual.Constants[info.BulletVisual].CollisionSize)
    {
        UpdateScript = BulletUpdateActions[info.BulletUpdateMethod];
        if(BulletUpdateActions.ContainsKey(info.BulletCreateMethod))
            CreateScript = BulletUpdateActions[info.BulletCreateMethod];
        Speed = info.Speed;
        SpawnTick = info.SpawnTick;
        SourceTexture = Runtime.CurrentRuntime.Textures[BulletVisual.Constants[info.BulletVisual].TextureName];
        SourceRect = new Rectangle(0, 0, BulletVisual.Constants[info.BulletVisual].RenderSize.X, BulletVisual.Constants[info.BulletVisual].RenderSize.Y);
    }

    Bullet(Game game, Vector2 pos, Vector2 renderSize, Vector2 collisionSize) : base(game, pos, renderSize,
        collisionSize)
    {
        
    } 
}

public class BulletVisual
{
    public static Dictionary<string, BulletVisual> Constants = new Dictionary<string, BulletVisual>();

    static BulletVisual()
    {
        Constants["Default"] = new BulletVisual("bullet.png", new Vector2(4,4), new Vector2(16, 16));
    }
    
    public BulletVisual(string textureName, Vector2 collisionSize, Vector2 renderSize)
    {
        TextureName = textureName;
        CollisionSize = collisionSize;
        RenderSize = renderSize;
    }
    
    public string TextureName;
    public Vector2 CollisionSize;
    public Vector2 RenderSize;
}
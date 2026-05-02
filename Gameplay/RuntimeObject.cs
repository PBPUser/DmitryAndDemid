using System.Numerics;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class RuntimeObject : IDisposable
{
    public Game Game;
    public int SpawnTick = 0;
    public float Speed = 1;
    public bool ClearProtected = false;
    public bool Attackable = false;
    public byte Alpha = 255;
    public bool InCollectableState = false;
    public bool TransferableInCollectableState = false;
    public Vector2 Velocity;
    public bool UseVelocity = false;
    public string Effect = "";
    public Vector3 EffectColor = Vector3.One;

    public RuntimeObject(Game game, Vector2 position, Vector2 renderSize, Vector2 collisionSize, float rotation = 0)
    {
        Game = game;
        PositionTo = position;
        PositionFrom = PositionTo;
        RenderSize = renderSize;
        CollisionSize = collisionSize;

        StateFrom = new Rectangle(PositionFrom - (RenderSize / 2), RenderSize);
        StateTo = StateFrom;
        Collision = new Rectangle(PositionFrom - (CollisionSize / 2), CollisionSize);
        UpdateCollisionRender(position, rotation);
    }

    public bool CollisionEnabled = true;

    Vector2 PositionFrom;
    public Vector2 PositionTo;
    public Vector2 RenderSize;
    public Vector2 CollisionSize;

    public Rectangle StateFrom;
    public Rectangle StateTo;
    public Rectangle Collision;
    public float RotateFrom;
    public float RotateTo;
    public Rectangle SourceRect;
    public Texture2D SourceTexture;

    public Dictionary<string, object>? Dictionary = new Dictionary<string, object>();

    public Action<RuntimeObject>? UpdateScript;
    public Action<RuntimeObject>? CreateScript;

    public virtual void Update()
    {

    }

    public virtual void Attack(float damage)
    {
        
    }

    public void UpdateCollisionRender(Vector2 newPos, float newRotate)
    {
        Collision.X = PositionTo.X - CollisionSize.X / 2;
        Collision.Y = PositionTo.Y - CollisionSize.Y / 2;
        PositionFrom = PositionTo;
        RotateFrom = RotateTo;
        RotateTo = newRotate;
        StateFrom = StateTo;
        PositionTo = newPos;
        StateTo.X = PositionTo.X - RenderSize.X / 2;
        StateTo.Y = PositionTo.Y - RenderSize.Y / 2;
    }

    public virtual (Rectangle destination, float rotation) GetRenderInfo(float state)
    {
        float reverseState = 1 - state;
        return (StateTo, RotateTo);
    }

    public void Dispose()
    {
        Dictionary = null;
    }
}
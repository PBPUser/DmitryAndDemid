using System.Numerics;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class Collectable : RuntimeObject
{
    public Collectable(Game game, Texture2D texture, Vector2 sourcePosition, Vector2 position, Vector2 renderSize, Vector2 velocity) : base(game, position, renderSize, new Vector2(12), 0)
    {
        SourceTexture = texture;
        SourceRect = new Rectangle(sourcePosition, renderSize);
        Velocity = velocity;
        ClearProtected = true;
    }


    private static float VelocityYC = .5f/60;
    private static float VelocityXMP = 59f/60f;
    private const float VelocityMagnetPerFrame = .5f;
    
    Vector2 Velocity;
    
    public override void Update()
    {
        if (Helper.IsCollied(Game.Player.Collision, Collision))
        {
            Apply();
            Console.WriteLine($"Collected {this.GetType()}");
            Game.RemoveObject(this);
            return;
        }
        else if (Helper.IsCollied(Game.Player.Collision with { Width = Game.Player.PointMagnetRadius }, Collision))
        {
            Console.WriteLine($"Magnets {this.GetType()}!!!");
            Velocity = Raymath.Vector2MoveTowards(Velocity, Helper.GetDirection(PositionTo, Game.Player.PositionTo),
                VelocityMagnetPerFrame);
        }
        else
        {
            Velocity.X *= VelocityXMP;
            Velocity.Y += VelocityYC;
        }
        UpdateCollisionRender(PositionTo + (Velocity * VelocityXMP), 0);
    }

    protected virtual void Apply()
    {
        
    }
}
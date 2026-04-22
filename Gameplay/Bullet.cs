using System.Numerics;
using DmitryAndDemid.Gameplay;
using Raylib_cs;

namespace DmitryAndDemid.Data;

public class Bullet : RuntimeObject
{
    public Bullet(Game game, Vector2 position, Vector2 renderSize, Vector2 collisionSize) : base(game, position, renderSize, collisionSize)
    {
        SourceTexture = Runtime.CurrentRuntime.Textures["bullet.png"];
        SourceRect = new Rectangle(0, 0, 16, 16);
    }
}

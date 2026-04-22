using System.Numerics;
using DmitryAndDemid.Gameplay;

namespace DmitryAndDemid.Data;

public class Enemy : RuntimeObject
{
    public Enemy(Game game, Vector2 position, Vector2 renderSize, Vector2 collisionSize) : base(game, position, renderSize, collisionSize)
    {

    }
}

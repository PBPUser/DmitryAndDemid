using System.Numerics;

namespace DmitryAndDemid.Gameplay;

public class Boss : RuntimeObject
{
    public string ID;

    public Boss(string id, Game game, Vector2 position, Vector2 renderSize, Vector2 collisionSize, float rotation = 0) : base(game, position, renderSize, collisionSize, rotation)
    {
        ID = id;
    }
}
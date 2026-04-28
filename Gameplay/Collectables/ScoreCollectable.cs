using System.Numerics;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.Collectables;

public class ScoreCollectable : Collectable
{
    public ScoreCollectable(Game game, Vector2 position, Vector2 velocity) 
        : base(game, Runtime.CurrentRuntime.Textures["collectables.png"], 
            new Vector2(16, 0), position, new Vector2(12), 
            velocity)
    {
    }

    protected override void Apply()
    {
        Game.Score += (int)((448 - Game.Player.PositionTo.Y) * 100); 
        base.Apply();
    }
    
    
}
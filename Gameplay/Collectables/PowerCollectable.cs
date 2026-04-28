using System.Numerics;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.Collectables;

public class PowerCollectable : Collectable
{
    public PowerCollectable(Game game, Vector2 position, Vector2 velocity) 
        : base(game, Runtime.CurrentRuntime.Textures["collectables.png"], 
            new Vector2(28, 0), position, new Vector2(12), 
            velocity)
    {
    }

    protected override void Apply()
    {
        Game.Player.Power += 1;
        base.Apply();
    }
    
    
}
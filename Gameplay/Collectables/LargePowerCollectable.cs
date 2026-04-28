using System.Numerics;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.Collectables;

public class LargePowerCollectable : Collectable
{
    public LargePowerCollectable(Game game, Vector2 position, Vector2 velocity) 
        : base(game, Runtime.CurrentRuntime.Textures["collectables.png"], 
            new Vector2(0, 0), position, new Vector2(16), 
            velocity)
    {
    }

    protected override void Apply()
    {
        Game.Player.Power += 100;
        base.Apply();
    }
    
    
}
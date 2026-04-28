using System.Numerics;
using DmitryAndDemid.Common;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class PlayerController : PlayerControllerBase
{
    public PlayerController()
    {
        
    }

    public byte[] Movements = new byte[262144];

    public override void Update(Player player, int tick)
    {
        int speed = Raylib.IsKeyDown(KeyboardKey.LeftShift) ? player.FocusSpeed : player.Speed;
        Vector2 PositionChange = Vector2.Zero;
        byte movement = 0;
        if (Raylib.IsKeyDown(KeyboardKey.Left))
        {
            PositionChange.X -= speed;
            movement += 1;
        }
        movement <<= 1;
        if (Raylib.IsKeyDown(KeyboardKey.Right))
        {
            PositionChange.X += speed;
            movement += 1;
        }
        movement <<= 1;
        if (Raylib.IsKeyDown(KeyboardKey.Up))
        {
            PositionChange.Y -= speed;
            movement += 1;
        }
        movement <<= 1;
        if (Raylib.IsKeyDown(KeyboardKey.Down))
        {
            PositionChange.Y += speed;
            movement += 1;
        }
        movement <<= 1;
        player.UpdateCollisionRender(player.PositionTo + PositionChange, 0);
        player.IsFocused = Raylib.IsKeyDown(KeyboardKey.LeftShift);
        if (player.IsFocused)
            movement += 1;
        movement <<= 1;
        player.IsShooting = Raylib.IsKeyDown(KeyboardKey.Z);
        if (player.IsShooting)
            movement += 1;
        movement <<= 1;
        player.IsBombing = Raylib.IsKeyDown(KeyboardKey.X);
        if (player.IsBombing) 
            movement += 1;
        Movements[tick] = movement;
        base.Update(player, tick);
    }
}
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
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
        Vector2 positionChange = Vector2.Zero;
        byte movement = 0;
        float
            xAxis = Controller.GetGamepadAxisValue(GamepadAxis.LeftX),
            yAxis = Controller.GetGamepadAxisValue(GamepadAxis.LeftY);
        if (Raylib.IsKeyDown(KeyboardKey.Left) || xAxis < -0.8)
        {
            positionChange.X -= speed;
            movement += 1;
        }
        movement <<= 1;
        if (Raylib.IsKeyDown(KeyboardKey.Right) || xAxis > 0.8)
        {
            positionChange.X += speed;
            movement += 1;
        }
        movement <<= 1;
        if (Raylib.IsKeyDown(KeyboardKey.Up) || yAxis < -0.8)
        {
            positionChange.Y -= speed;
            movement += 1;
        }
        movement <<= 1;
        if (Raylib.IsKeyDown(KeyboardKey.Down) || yAxis > 0.8)
        {
            positionChange.Y += speed;
            movement += 1;
        }
        movement <<= 1;
        player.UpdateCollisionRender(player.PositionTo + positionChange,
            0);
        player.IsFocused = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Controller.IsButtonDown(Configuration.Config.FocusButton);
        if (player.IsFocused)
            movement += 1;
        movement <<= 1;
        player.IsShooting = Raylib.IsKeyDown(KeyboardKey.Z) || Controller.IsButtonDown(Configuration.Config.ShootButton);
        if (player.IsShooting)
            movement += 1;
        movement <<= 1;
        player.IsBombing = Raylib.IsKeyDown(KeyboardKey.X) || Controller.IsButtonDown(Configuration.Config.BombButton);
        if (player.IsBombing) 
            movement += 1;
        Movements[tick] = movement;
        base.Update(player, tick);
    }
}
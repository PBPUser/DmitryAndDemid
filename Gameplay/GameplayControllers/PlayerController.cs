using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay;

public class PlayerController : PlayerControllerBase
{
    public PlayerController()
    {
        
    }

    public byte[] Movements = new byte[262144];

    public override void Update(Player player, int tick)
    {
        TouchControls.Update();

        int speed = IsKeyDown(KeyCode.LeftShift) ? player.FocusSpeed : player.Speed;
        Vector2 positionChange = Vector2.Zero;
        byte movement = 0;
        float
            xAxis = Controller.GetGamepadAxisValue(PadAxis.LeftX),
            yAxis = Controller.GetGamepadAxisValue(PadAxis.LeftY);
        if (IsKeyDown(KeyCode.Left) || xAxis < -0.8)
        {
            positionChange.X -= speed;
            movement += 1;
        }
        movement <<= 1;
        if (IsKeyDown(KeyCode.Right) || xAxis > 0.8)
        {
            positionChange.X += speed;
            movement += 1;
        }
        movement <<= 1;
        if (IsKeyDown(KeyCode.Up) || yAxis < -0.8)
        {
            positionChange.Y -= speed;
            movement += 1;
        }
        movement <<= 1;
        if (IsKeyDown(KeyCode.Down) || yAxis > 0.8)
        {
            positionChange.Y += speed;
            movement += 1;
        }
        movement <<= 1;
        player.X += positionChange.X;
        player.Y += positionChange.Y;
        player.IsFocused = IsKeyDown(KeyCode.LeftShift) || Controller.IsButtonDown(Configuration.Config.FocusButton);
        if (player.IsFocused)
            movement += 1;
        movement <<= 1;
        player.IsShooting = IsKeyDown(KeyCode.Z) || Controller.IsButtonDown(Configuration.Config.ShootButton);
        if (player.IsShooting)
            movement += 1;
        movement <<= 1;
        player.IsBombing = IsKeyDown(KeyCode.X) || Controller.IsButtonDown(Configuration.Config.BombButton);
        if (player.IsBombing) 
            movement += 1;
        Movements[tick] = movement;
        base.Update(player, tick);
    }
}
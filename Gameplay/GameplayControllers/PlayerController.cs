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

        // Focus (slow movement) engages on the focus key/button, and — when the option is on — also while the
        // shoot button is held, so the player can slow down without a separate finger/key ("auto slowdown").
        bool shootButton = IsKeyDown(KeyCode.Z) || Controller.IsButtonDown(Configuration.Config.ShootButton)
                           || TouchControls.ShootHeld;
        bool focus = IsKeyDown(KeyCode.LeftShift) || Controller.IsButtonDown(Configuration.Config.FocusButton)
                     || TouchControls.FocusHeld
                     || (Configuration.Config.AutoSlowdownOnShoot && shootButton);

        int speed = focus ? player.FocusSpeed : player.Speed;
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

        // Touch movement, applied on top of the keyboard/pad movement rather than through it. Two styles:
        // the virtual stick pushes the ship at the keyboard's per-tick speed in the deflected direction; the
        // drag style has the ship follow the finger 1:1 (in playfield units). Both zero out when touch is off.
        // NOTE: only the four direction bits above are written into the replay, so a run played on touch does
        // not reproduce from its .rpy — see Movements below.
        if (Configuration.Config.TouchStick)
        {
            int touchSpeed = TouchControls.FocusHeld ? player.FocusSpeed : player.Speed;
            player.X += TouchControls.MoveVector.X * touchSpeed;
            player.Y += TouchControls.MoveVector.Y * touchSpeed;
        }
        else if (TouchControls.IsDragging)
        {
            player.X += TouchControls.DragDelta.X;
            player.Y += TouchControls.DragDelta.Y;
        }

        player.IsFocused = focus;
        if (player.IsFocused)
            movement += 1;
        movement <<= 1;
        // Touch fires while moving (as in every touch danmaku) and also whenever the SHOOT button is held.
        player.IsShooting = IsKeyDown(KeyCode.Z) || Controller.IsButtonDown(Configuration.Config.ShootButton)
                            || TouchControls.WantsFire;
        if (player.IsShooting)
            movement += 1;
        movement <<= 1;
        player.IsBombing = IsKeyDown(KeyCode.X) || Controller.IsButtonDown(Configuration.Config.BombButton)
                           || TouchControls.BombHeld;
        if (player.IsBombing)
            movement += 1;

        // The replay format is one packed byte of direction/action bits per tick (see Data/Replay.cs). Touch
        // movement is a continuous delta and has no representation in those four direction bits, so it is NOT
        // captured here: replays recorded while using touch will play back wrong. Fixing that means widening
        // the replay format to store the position delta, which changes the .rpy layout.
        Movements[tick] = movement;
        base.Update(player, tick);
    }
}
using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay;

public class PlayerController : PlayerControllerBase
{
    public PlayerController()
    {

    }

    public byte[] Movements = new byte[262144];

    /// <summary>
    /// One entry per stage this run entered, in order. Each records the byte offset where the stage's inputs
    /// begin in <see cref="Movements"/> plus the player's resources at stage entry. Saved into the replay header
    /// so the viewer knows which levels the replay covers and can start playback from any of them.
    /// </summary>
    public readonly List<ReplayStageInfo> RecordedStages = new();

    /// <summary>Byte offset in <see cref="Movements"/> where the current stage's inputs start.</summary>
    private int StageBase = 0;
    /// <summary>Highest written index + 1 — where the next stage will be laid down, and the used length to save.</summary>
    public int WrittenLength = 0;

    /// <summary>
    /// Called by <see cref="GameBox"/> as each stage begins. Lays the new stage's inputs down after the previous
    /// stage's (the per-stage tick resets to 0, so stages would otherwise overwrite each other from index 0) and
    /// records the offset together with the supplied resource snapshot.
    /// </summary>
    public void BeginRecordingStage(ReplayStageInfo snapshot)
    {
        StageBase = WrittenLength;
        snapshot.Tick = StageBase;
        RecordedStages.Add(snapshot);
    }

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
        // The per-stage tick resets to 0 each stage, so offset by the current stage's base (see BeginRecordingStage)
        // to keep every stage's inputs in its own segment of the shared buffer.
        int index = StageBase + tick;
        if (index >= 0 && index < Movements.Length)
        {
            Movements[index] = movement;
            if (index + 1 > WrittenLength)
                WrittenLength = index + 1;
        }
        base.Update(player, tick);
    }
}
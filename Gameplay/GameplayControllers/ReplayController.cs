using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Data;

namespace DmitryAndDemid.Gameplay;

public class ReplayController : PlayerController
{
    private int StageFrom = 0;
    private Replay Replay;
    
    public ReplayController(Replay replay, int stageFrom)
    {
        Replay = replay;
        StageFrom = stageFrom;
    }
    
    public override void Update(Player player, int tick)
    {
        if (tick < 0 || tick >= Replay.Data.Length)
            return;
        Vector2 positionChange = Vector2.Zero;
        int tickData = Replay.Data[tick];
        player.IsBombing = tickData % 2 == 1;
        tickData >>= 1;
        player.IsShooting =  tickData % 2 == 1;
        tickData >>= 1;
        player.IsFocused = tickData % 2 == 1;
        tickData >>= 1;
        int speed = player.IsFocused ? player.FocusSpeed : player.Speed;
        positionChange.Y += tickData%2==1 ? speed:0;
        tickData >>= 1;
        positionChange.Y -= tickData%2==1 ? speed:0;
        tickData >>= 1;
        positionChange.X += tickData%2==1 ? speed:0;
        tickData >>= 1;
        positionChange.X -= tickData%2==1 ? speed:0;
        tickData >>= 1;
        player.X += positionChange.X;
        player.Y += positionChange.Y;
        // Deliberately NOT calling base.Update: that is PlayerController.Update, which re-reads live keyboard/
        // pad/touch input and would fight the replay (and overwrite the movement buffer). Playback is driven
        // solely by Replay.Data above.
    }
}
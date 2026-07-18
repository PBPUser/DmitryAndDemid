using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Data;

namespace DmitryAndDemid.Gameplay;

public class ReplayController : PlayerController
{
    /// <summary>The campaign stage index playback was launched from (the one the player picked in the viewer).</summary>
    public int StageFrom { get; }
    private readonly Replay Replay;
    private readonly ReplayStageInfo[] StageInfos;
    /// <summary>Byte offset in <see cref="Replay.Data"/> where the CURRENT stage's inputs start.</summary>
    private int ReadBase = 0;

    public ReplayController(Replay replay, int stageFrom)
    {
        Replay = replay;
        StageFrom = stageFrom;
        StageInfos = replay.Information.ReplayStageInfo ?? [];
    }

    /// <summary>The recorded snapshot for a campaign stage index, or null if the replay doesn't cover it.</summary>
    public ReplayStageInfo? InfoFor(int stageIndex)
    {
        foreach (ReplayStageInfo info in StageInfos)
            if (info.Stage == stageIndex)
                return info;
        return null;
    }

    /// <summary>
    /// Called by <see cref="GameBox"/> as each stage begins during playback. Points reads at that stage's
    /// segment of the buffer. Old replays with no per-stage index keep reading from offset 0 (their whole buffer
    /// is a single stage), preserving the pre-feature behaviour.
    /// </summary>
    public void SetReadBase(int stageIndex)
    {
        ReplayStageInfo? info = InfoFor(stageIndex);
        ReadBase = info?.Tick ?? 0;
    }

    public override void Update(Player player, int tick)
    {
        int index = ReadBase + tick;
        if (tick < 0 || index < 0 || index >= Replay.Data.Length)
            return;
        Vector2 positionChange = Vector2.Zero;
        int tickData = Replay.Data[index];
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

using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

/// <summary>
/// One entry per stage a replay actually covers. Written at record time when the run enters a stage; read back
/// by the replay viewer to know which levels a replay contains and to start playback from a chosen one.
///
/// <para><see cref="Tick"/> is the byte offset of this stage's inputs inside <see cref="Replay.Data"/>. The
/// game's tick counter resets to 0 at every stage boundary, so a single run's stages would otherwise overwrite
/// each other from index 0; the recorder lays each stage down after the previous one and stores its base here,
/// and playback reads <c>Data[Tick + perStageTick]</c>.</para>
///
/// <para>The remaining fields snapshot the player's resources as they entered the stage, so launching a replay
/// partway through reproduces the state the run had at that point instead of a fresh default stock.</para>
/// </summary>
public class ReplayStageInfo
{
    public ReplayStageInfo() { }

    public ReplayStageInfo(int tick, int stage)
    {
        Tick = tick;
        Stage = stage;
    }

    /// <summary>Byte offset of this stage's inputs within <see cref="Replay.Data"/> (see class summary).</summary>
    [JsonInclude] public int Tick = 0;
    /// <summary>Campaign stage index (0-based), matching the order of <c>FileStageInfo.CampaignStagePaths()</c>.</summary>
    [JsonInclude] public int Stage = 0;

    // Player/box snapshot at stage entry — restored when playback is launched from this stage. Position matters
    // because replay inputs are relative per-tick deltas: a run carries its position across a stage boundary, so
    // starting a stage from the wrong spot would offset every position for the rest of the run.
    [JsonInclude] public int Score = 0;
    [JsonInclude] public int Lives = 2;        // Player.HeartPoints
    [JsonInclude] public int Bombs = 3;
    [JsonInclude] public int HeartSpices = 0;
    [JsonInclude] public int BombsSpices = 0;
    [JsonInclude] public int Power = 300;
    [JsonInclude] public int Graze = 0;
    [JsonInclude] public int Signal = 0;
    [JsonInclude] public float PosX = 192;
    [JsonInclude] public float PosY = 400;
}

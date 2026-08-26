using System.Text;
using DmitryAndDemid.Data;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// The <c>.rpy</c> replay container: a JSON header plus one packed input byte per tick. The layout is
/// <c>[dataLength:4][jsonLength:4][json][data]</c> and has already been broken once by a reader using the
/// wrong field order (see the comment in <see cref="Replay.Load(byte[])"/>) — the layout test is the
/// regression guard for exactly that. Pure byte/JSON work; no GPU.
/// </summary>
public class ReplayFormatTests
{
    private static Replay SampleReplay()
    {
        var json = new Replay.ReplayJson
        {
            Nickname = "demid",
            Stage = "stage1",
            Difficulty = 2,
            Person = "dmitry",
            Timestamp = new DateTime(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc),
            Slowdown = "0.0",
            ReplayStageInfo =
            [
                new ReplayStageInfo(0, 0) { Score = 0, Lives = 2, Bombs = 3, Power = 300 },
                new ReplayStageInfo(7200, 1) { Score = 1234560, Lives = 1, PosX = 100.5f, PosY = 220.25f },
            ],
        };
        byte[] data = [0x00, 0x2A, 0x3F, 0x15, 0xFF];
        return new Replay(data, json);
    }

    [Fact]
    public void Export_then_load_round_trips()
    {
        Replay replay = SampleReplay();
        Replay loaded = Replay.Load(replay.Export());

        Assert.Equal(replay.Data, loaded.Data);
        Assert.Equal("demid", loaded.Information.Nickname);
        Assert.Equal("stage1", loaded.Information.Stage);
        Assert.Equal(2, loaded.Information.Difficulty);
        Assert.Equal("dmitry", loaded.Information.Person);
        Assert.Equal(replay.Information.Timestamp, loaded.Information.Timestamp);
        Assert.Equal("0.0", loaded.Information.Slowdown);

        Assert.Equal(2, loaded.Information.ReplayStageInfo.Length);
        ReplayStageInfo second = loaded.Information.ReplayStageInfo[1];
        Assert.Equal(7200, second.Tick);
        Assert.Equal(1, second.Stage);
        Assert.Equal(1234560, second.Score);
        Assert.Equal(1, second.Lives);
        Assert.Equal(3, second.Bombs);          // the class default survives an unset field
        Assert.Equal(100.5f, second.PosX);
        Assert.Equal(220.25f, second.PosY);
    }

    /// <summary>Field order and offsets on the wire: both little-endian lengths up front, header before
    /// payload. A writer that swaps them produces files every previous build refuses to read.</summary>
    [Fact]
    public void Export_lays_out_lengths_then_json_then_data()
    {
        Replay replay = SampleReplay();
        byte[] file = replay.Export();

        int dataLength = BitConverter.ToInt32(file, 0);
        int jsonLength = BitConverter.ToInt32(file, 4);
        Assert.Equal(replay.Data.Length, dataLength);
        Assert.Equal(file.Length, 8 + jsonLength + dataLength);

        string json = Encoding.UTF8.GetString(file, 8, jsonLength);
        Assert.Contains("\"Nickname\":\"demid\"", json);
        Assert.Equal(replay.Data, file.Skip(8 + jsonLength).Take(dataLength).ToArray());
    }

    [Fact]
    public void ReadHeader_recovers_the_json_without_touching_the_payload()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rpy");
        try
        {
            Replay replay = SampleReplay();
            replay.Save(path);

            Replay.ReplayJson? header = Replay.ReadHeader(path);
            Assert.NotNull(header);
            Assert.Equal("demid", header.Nickname);
            Assert.Equal(2, header.ReplayStageInfo.Length);

            Replay loaded = Replay.Load(path);
            Assert.Equal(replay.Data, loaded.Data);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

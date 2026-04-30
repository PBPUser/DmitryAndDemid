using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class ReplayStageInfo
{
    public ReplayStageInfo(int tick, int stage)
    {
        Tick = tick;
        Stage = stage;
    }
    
    [JsonInclude] public int Tick = 0;
    [JsonInclude] public int Stage = 0;
}
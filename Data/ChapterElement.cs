using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class ChapterElement
{
    [JsonInclude]
    public int SpawnTick = 0;
}
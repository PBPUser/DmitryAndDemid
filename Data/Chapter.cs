

using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class ChapterInfo
{
    [JsonInclude]
    public ChapterType Type = ChapterType.NonBoss;

    [JsonInclude]
    public string ChapterLabel = "";

    [JsonInclude]
    public string ChapterBossArt = "";
}

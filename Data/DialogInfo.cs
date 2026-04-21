using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class DialogInfo
{
    public class DialogElement
    {
        [JsonInclude]
        public string Text = "Sample Text";

        [JsonInclude]
        public string? Art = "";
    }
}

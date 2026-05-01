using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class TrophyInfo
{
    [JsonInclude] public string Description = "sample text";
    [JsonInclude] public int Index = 0;
    [JsonInclude] public string ID = "";
    [JsonInclude] public bool HasAction = false;
    /// <summary>
    /// dialog - Dialog
    /// ending - Ending
    /// staff - Staff Roll
    /// </summary>
    [JsonInclude] public string ActionType = "";
    [JsonInclude] public string ActionInfo = "";
}
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class EndingInfo
{
    [JsonInclude] public string ID;
    [JsonInclude] public bool IsBad = false;
    [JsonInclude] public List<AddTextEndingElement> AddTexts = new();
    [JsonInclude] public List<ClearTextEndingElement> ClearTexts = new();
    [JsonInclude] public List<SwitchPictureEndingElement> PictureSwitchers = new();
}
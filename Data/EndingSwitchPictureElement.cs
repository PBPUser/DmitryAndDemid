using System.Text.Json.Serialization;
using DmitryAndDemid.Screens;

namespace DmitryAndDemid.Data;

public class EndingSwitchPictureElement : EndingElement
{
    [JsonInclude] public string NewImagePath = "";

    public override void Apply(EndingScreen screen)
    {
        base.Apply(screen);
    }
}
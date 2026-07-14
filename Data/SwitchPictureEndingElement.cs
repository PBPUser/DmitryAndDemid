using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Text.Json.Serialization;
using DmitryAndDemid.Screens;

namespace DmitryAndDemid.Data;

public class SwitchPictureEndingElement : EndingElement
{
    [JsonInclude] public string Picture = "";
    
    public override void Apply(EndingScreen screen)
    {
        screen.SwitchPicture(Runtime.CurrentRuntime.Textures[Picture]);
    }
}
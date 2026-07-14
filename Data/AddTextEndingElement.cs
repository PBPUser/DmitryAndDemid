using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Text.Json.Serialization;
using DmitryAndDemid.Screens;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data;

public class AddTextEndingElement : EndingElement
{
    [JsonInclude] public string Text;
    
    public override void Apply(EndingScreen screen)
    {
        screen.AddText(Text);
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["dialogue"]);
    }
}
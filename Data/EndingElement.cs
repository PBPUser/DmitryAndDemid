using System.Text.Json.Serialization;
using DmitryAndDemid.Screens;

namespace DmitryAndDemid.Data;

public abstract class EndingElement
{
    [JsonInclude] public int Index = 0;

    public virtual void Apply(EndingScreen screen)
    {
        
    }
}
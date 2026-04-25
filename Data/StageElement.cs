using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class StageElement
{
    [JsonInclude] public int Index = 0;

    public virtual void Unload()
    {
        
    }
}

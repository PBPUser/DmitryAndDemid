using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class DialogInfo : StageElement
{
    [JsonInclude]
    public PersonDialog[] PersonDialogs = [];

    public class DialogElement
    {
        [JsonInclude] public bool AntogonistSpeak = false;
        [JsonInclude] public string Art = "";
        [JsonInclude] public int ArtIndex = 0;
        [JsonInclude] public string ID = "";
        [JsonInclude] public bool Skipable = true;
        [JsonInclude] public string Text = "Sample Text";
    }
    
    public class PersonDialog
    {
        [JsonInclude] public DialogElement[] Elements = [];
        [JsonInclude] public string ID = "";
    }
}
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class DialogInfo : StageElement
{
    [JsonInclude]
    public PersonDialog[] PersonDialogs = [];

    public class DialogElement
    {
        [JsonInclude] public string Text = "Sample Text";
        [JsonInclude] public string Art = "";
        [JsonInclude] public bool Skipable = true;
        [JsonInclude] public bool AntogonistSpeak = false;
        [JsonInclude] public int ArtIndex = 0;
        [JsonInclude] public string ID = "";
    }
    
    public class PersonDialog
    {
        [JsonInclude] public DialogElement[] Elements = [];
        [JsonInclude] public string ID = "";
    }
}
using System.Text.Json.Serialization;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Data;

public class DialogInfo : StageElement
{
    [JsonInclude]
    public DialogElement[] Elements = new DialogElement[0];

    public class DialogElement
    {
        [JsonInclude]
        public string Text = "Sample Text";

        [JsonInclude]
        public string Art = "";

        [JsonInclude]
        public bool Skipable = true;

        [JsonInclude]
        public bool AntogonistSpeak = false;
    }
}

public class RuntimeDialogElement
{
    public RenderTexture2D DialogTexture;
    public bool Skipable;
    public Texture2D Art;
    public bool AntogonistSpeak;

    public RuntimeDialogElement(DialogInfo.DialogElement dialogElement)
    {
        Skipable = dialogElement.Skipable;
        Art = Runtime.CurrentRuntime.Textures[dialogElement.Art];
        AntogonistSpeak = dialogElement.AntogonistSpeak;
        DialogTexture = Helper.DrawDialog(dialogElement.Text, AntogonistSpeak ? 0.79f : 2.34f);
    }

    public void Unload()
    {
        Raylib.UnloadRenderTexture(DialogTexture);
    }
}

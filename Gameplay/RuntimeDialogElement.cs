using DmitryAndDemid.Utils;
using DmitryAndDemid.Data;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay;

public class RuntimeDialogElement
{
    public RenderTexture2D DialogTexture;
    public bool Skipable;
    public Texture2D Art;
    public bool AntogonistSpeak;
    public int ArtIndex = 0;

    public RuntimeDialogElement(DialogInfo.DialogElement dialogElement)
    {
        ArtIndex = dialogElement.ArtIndex;
        Skipable = dialogElement.Skipable;
        if(dialogElement.AntogonistSpeak)
            Art = Runtime.CurrentRuntime.Textures[dialogElement.Art];
        AntogonistSpeak = dialogElement.AntogonistSpeak;
        DialogTexture = Helper.DrawDialog(dialogElement.Text, AntogonistSpeak ? 0.79f : 2.34f);
    }

    public void Unload()
    {
        Raylib.UnloadRenderTexture(DialogTexture);
    }
}
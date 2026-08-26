using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Utils;
using DmitryAndDemid.Data;

namespace DmitryAndDemid.Gameplay;

public class RuntimeDialogElement
{
    public RenderedTexture DialogTexture;
    public bool Skipable;
    public BasicTexture Art;
    public bool AntogonistSpeak;
    public int ArtIndex = 0;
    public string ID;

    public RuntimeDialogElement(DialogInfo.DialogElement dialogElement)
    {
        ID = dialogElement.ID;
        Skipable = dialogElement.Skipable;
        if(dialogElement.AntogonistSpeak)
            Art = Runtime.CurrentRuntime.Textures[dialogElement.Art];
        ArtIndex = dialogElement.ArtIndex;
        AntogonistSpeak = dialogElement.AntogonistSpeak;
        DialogTexture = Helper.DrawDialog(dialogElement.Text, AntogonistSpeak ? 0.79f : 2.34f);
    }

    public void Unload()
    {
        UnloadRenderTexture(DialogTexture);
    }
}
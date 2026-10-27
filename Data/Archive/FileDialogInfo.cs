using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data.Archive;

public class FileDialogInfo
{
    public int[] Header =  new int[4];
    public string CharacterTexture = "";
    public string Text = "";
    public bool IsPlayerDialog = false;
    public bool SwitchReaction = false;
    public bool SwitchMusic = false;
    public bool ShowBossName = false;

    /// <summary>
    /// The line cannot be hurried: pressing shoot does not advance it and holding shoot does not close the
    /// conversation — it stays up for its full <see cref="DmitryAndDemid.Gameplay.RuntimeData.RuntimeDialog.LineDuration"/>.
    /// For story beats that must actually be read (the Extra stage's two Dmitry/Demid dialogs).
    /// </summary>
    public bool Unskippable = false;

    /// <summary>
    /// The line's emotion: one symbol (as text, e.g. "☠") from Noto Sans Symbols 2, baked into a dressed-up
    /// glyph when the chapter loads (<see cref="DmitryAndDemid.Utils.EmotionGlyph"/>) and shown on the speaker's
    /// side of the dialog window. Empty shows nothing. Written after the character texture in the packed form.
    /// </summary>
    public string Emotion = "";

    /// <summary>
    /// Packs the boolean flags into <see cref="Header"/>[0], the inverse of the bit-unpacking in
    /// <see cref="Load"/>. Called at the top of <see cref="Save"/> and by the JSON importer so a hand-authored
    /// dialog line (which sets the friendly bools) produces the same <see cref="Header"/> a binary load would.
    /// </summary>
    public void PackFlags()
    {
        Header[0] = IsPlayerDialog ? 1 : 0;
        Header[0] |= SwitchReaction ? 2 : 0;
        Header[0] |= SwitchMusic ? 4 : 0;
        Header[0] |= ShowBossName ? 8 : 0;
        Header[0] |= Unskippable ? 0x10 : 0;
    }

    public void Save(ref BitPackage package)
    {
        PackFlags();
        for(int i = 0; i < 4; i++)
            package.WriteVarLong(Header[i]);
        package.WriteString(Text);
        package.WriteString(CharacterTexture);
        package.WriteString(Emotion);
    }

    public static FileDialogInfo Load(ref BitPackage package)
    {
        FileDialogInfo dialogInfo = new();
        for (int i = 0; i < 4; i++)
            dialogInfo.Header[i] = (int)package.ReadVarLong();
        dialogInfo.Text = package.ReadString();
        dialogInfo.CharacterTexture = package.ReadString();
        dialogInfo.Emotion = package.ReadString();
        dialogInfo.IsPlayerDialog = (dialogInfo.Header[0] & 0x1) == 0x1;
        dialogInfo.SwitchReaction = (dialogInfo.Header[0] & 0x2) == 0x2;
        dialogInfo.SwitchMusic = (dialogInfo.Header[0] & 0x4) == 0x4;
        dialogInfo.ShowBossName = (dialogInfo.Header[0] & 0x8) == 0x8;
        dialogInfo.Unskippable = (dialogInfo.Header[0] & 0x10) == 0x10;
        return dialogInfo;
    }
}
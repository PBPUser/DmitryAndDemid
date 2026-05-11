using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data;

public class FileDialogInfo
{
    public int[] Header =  new int[4];
    public string CharacterTexture = "";
    public string Text = "";
    public bool IsPlayerDialog = false;
    public bool SwitchReaction = false;
    public bool SwitchMusic = false;
    public bool ShowBossName = false;

    public void Save(ref BitPackage package)
    {
        Header[0] = IsPlayerDialog ? 1 : 0;
        Header[0] |= SwitchReaction ? 2 : 0;
        Header[0] |= SwitchMusic ? 4 : 0;
        Header[0] |= ShowBossName ? 8 : 0;
        for(int i = 0; i < 4; i++)
            package.WriteVarLong(Header[i]);
        package.WriteString(Text);
        package.WriteString(CharacterTexture);
    }

    public static FileDialogInfo Load(ref BitPackage package)
    {
        FileDialogInfo dialogInfo = new();
        for (int i = 0; i < 4; i++)
            dialogInfo.Header[i] = (int)package.ReadVarLong();
        dialogInfo.Text = package.ReadString();
        dialogInfo.CharacterTexture = package.ReadString();
        dialogInfo.IsPlayerDialog = (dialogInfo.Header[0] & 0x1) == 0x1;
        dialogInfo.SwitchReaction = (dialogInfo.Header[0] & 0x2) == 0x2;
        dialogInfo.SwitchMusic = (dialogInfo.Header[0] & 0x4) == 0x4;
        dialogInfo.ShowBossName = (dialogInfo.Header[0] & 0x8) == 0x8;
        return dialogInfo;
    }
}
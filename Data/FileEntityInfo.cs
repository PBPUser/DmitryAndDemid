using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data;

public class FileEntityInfo
{
    public int[] Header = new int[16];
    public float Scaling = 1f;
    public float Transparency = 1f;
    public Drop BadDrop = new Drop();
    public Drop GoodDrop = new Drop();
    
    public bool IsBullet = false;
    public bool IsGroupChild = false;
    public bool IsGroupParent = false;
    public bool UseCreateScript = false;
    public bool UseRemoveScript = false;
    public bool ClearProtected = false;
    public bool DangerousForPlayer = false;
    public bool IsBoss = false;
    public bool UseBadDropScenario = false;
    public bool DropWhenCleared = false;
    
    public string Visual = "";

    public static FileEntityInfo Load(ref BitPackage bitPackage)
    {
        FileEntityInfo fileEntityInfo = new FileEntityInfo();
        for (int i = 0; i < 16; i++)
            fileEntityInfo.Header[i] = (int)bitPackage.ReadVarLong();
        fileEntityInfo.Visual = bitPackage.ReadString();
        fileEntityInfo.IsBullet = (fileEntityInfo.Header[0] & 0x001) == 0x001;
        fileEntityInfo.IsGroupChild = (fileEntityInfo.Header[0] & 0x002) == 0x002;
        fileEntityInfo.IsGroupParent = (fileEntityInfo.Header[0] & 0x004) == 0x004;
        fileEntityInfo.UseCreateScript = (fileEntityInfo.Header[0] & 0x008) == 0x008;
        fileEntityInfo.UseRemoveScript = (fileEntityInfo.Header[0] & 0x010) == 0x010;
        fileEntityInfo.ClearProtected = (fileEntityInfo.Header[0] & 0x020) == 0x020;
        fileEntityInfo.DangerousForPlayer = (fileEntityInfo.Header[0] & 0x040) == 0x040;
        fileEntityInfo.UseBadDropScenario = (fileEntityInfo.Header[0] & 0x080) == 0x080;
        fileEntityInfo.DropWhenCleared = (fileEntityInfo.Header[0] & 0x100) == 0x100;
        fileEntityInfo.IsBoss = (fileEntityInfo.Header[0] & 0x200) == 0x200;
        fileEntityInfo.BadDrop = new Drop(fileEntityInfo.Header[7]);
        fileEntityInfo.GoodDrop = new Drop(fileEntityInfo.Header[8]);
        return fileEntityInfo;
    }
    
    public void Save(ref BitPackage bitPackage)
    {
        Header[0] = IsBullet ? 1 : 0;
        Header[0] |= IsGroupChild ? 0x2 : 0;
        Header[0] |= IsGroupParent ? 0x4 : 0;
        Header[0] |= UseCreateScript ? 0x8 : 0;
        Header[0] |= UseRemoveScript ? 0x10 : 0;
        Header[0] |= ClearProtected ? 0x20 : 0;
        Header[0] |= DangerousForPlayer ? 0x40 : 0;
        Header[0] |= UseBadDropScenario ? 0x80 : 0;
        Header[0] |= DropWhenCleared ? 0x100 : 0;
        Header[0] |= IsBoss ? 0x200 : 0;
        Header[7] = BadDrop.ToInt32();
        Header[8] = GoodDrop.ToInt32();
        for(int i = 0; i < 16; i++)
            bitPackage.WriteVarLong(Header[i]);
        bitPackage.WriteString(Visual);
    }

}
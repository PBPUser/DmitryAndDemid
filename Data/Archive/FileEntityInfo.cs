using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data.Archive;

public class FileEntityInfo
{
    public FileEntityInfo()
    {
        Header[0] = 0b_0100_0000;
        Header[2] = 255;
        FloatingPoints[2] = 1f;
    }
    
    public int[] Header = new int[16];
    public float[] FloatingPoints = new float[16];
    public Drop BadDrop = new Drop();
    public Drop GoodDrop = new Drop();
    public string CreateScript = "";
    public string UpdateScript = "";
    public string RemoveScript = "";
    public string DieScript = "";
    
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
    public bool UseDieScript = false;
    public bool OverrideDeathColor = false;
    public bool IsFinalChapterBoss = false;
    
    public string Visual = "";

    public FileEntityInfo Clone()
    {
        FileEntityInfo f =  new FileEntityInfo();
        f.Header = Header.ToArray();
        f.FloatingPoints = FloatingPoints.ToArray();
        f.BadDrop = new Drop(BadDrop.ToInt32());
        f.GoodDrop = new Drop(BadDrop.ToInt32());
        f.CreateScript = CreateScript;
        f.UpdateScript = UpdateScript;
        f.RemoveScript = RemoveScript;
        f.DieScript = DieScript;
        f.IsBullet = IsBullet;
        f.IsGroupChild = IsGroupChild;
        f.IsGroupParent = IsGroupParent;
        f.IsFinalChapterBoss = IsFinalChapterBoss;
        f.UseCreateScript = UseCreateScript;
        f.UseRemoveScript = UseRemoveScript;
        f.ClearProtected = ClearProtected;
        f.DangerousForPlayer = DangerousForPlayer;
        f.IsBoss = IsBoss;
        f.UseBadDropScenario =  UseBadDropScenario;
        f.DropWhenCleared = DropWhenCleared;
        f.UseDieScript = UseDieScript;
        f.OverrideDeathColor = OverrideDeathColor;
        f.Visual = Visual;
        return f;
    }
    
    public static FileEntityInfo Load(ref BitPackage bitPackage)
    {
        FileEntityInfo fileEntityInfo = new FileEntityInfo();
        for (int i = 0; i < 16; i++)
            fileEntityInfo.Header[i] = (int)bitPackage.ReadVarLong();
        for(int i = 0; i < 16; i++)
            fileEntityInfo.FloatingPoints[i] = bitPackage.ReadFloat();
        fileEntityInfo.Visual = bitPackage.ReadString();
        fileEntityInfo.UpdateScript = bitPackage.ReadString();
        
        fileEntityInfo.IsBullet = (fileEntityInfo.Header[0] & 0x001) == 0x001;
        fileEntityInfo.IsGroupChild = (fileEntityInfo.Header[0] & 0x002) == 0x002;
        fileEntityInfo.IsGroupParent = (fileEntityInfo.Header[0] & 0x004) == 0x004;
        fileEntityInfo.UseCreateScript = (fileEntityInfo.Header[0] & 0x008) == 0x008;
        fileEntityInfo.UseRemoveScript = (fileEntityInfo.Header[0] & 0x010) == 0x010;
        fileEntityInfo.ClearProtected = (fileEntityInfo.Header[0] & 0x020) == 0x020;
        fileEntityInfo.DangerousForPlayer = (fileEntityInfo.Header[0] & 0x040) == 0x040;
        if (!fileEntityInfo.IsBullet)
        {
            fileEntityInfo.UseBadDropScenario = (fileEntityInfo.Header[0] & 0x080) == 0x080;
            fileEntityInfo.DropWhenCleared = (fileEntityInfo.Header[0] & 0x100) == 0x100;
            fileEntityInfo.IsBoss = (fileEntityInfo.Header[0] & 0x200) == 0x200;
            fileEntityInfo.UseDieScript = (fileEntityInfo.Header[0] & 0x400) == 0x400;
            fileEntityInfo.OverrideDeathColor = (fileEntityInfo.Header[0] & 0x800) == 0x800;
            fileEntityInfo.BadDrop = new Drop(fileEntityInfo.Header[4]);
            fileEntityInfo.GoodDrop = new Drop(fileEntityInfo.Header[5]);
            if (fileEntityInfo.IsBoss)
                fileEntityInfo.IsFinalChapterBoss = (fileEntityInfo.Header[0] & 0x100000) == 0x100000;
        }
        if(fileEntityInfo.UseCreateScript)
            fileEntityInfo.CreateScript = bitPackage.ReadString();
        if(fileEntityInfo.UseRemoveScript)
            fileEntityInfo.RemoveScript = bitPackage.ReadString();
        if(fileEntityInfo.UseDieScript && !fileEntityInfo.IsBullet)
            fileEntityInfo.DieScript = bitPackage.ReadString();
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
        if (!IsBullet)
        {
            Header[0] |= UseBadDropScenario ? 0x80 : 0;
            Header[0] |= DropWhenCleared ? 0x100 : 0;
            Header[0] |= IsBoss ? 0x200 : 0;
            Header[0] |= UseDieScript ? 0x400 : 0;
            Header[0] |= OverrideDeathColor ? 0x800 : 0;
            Header[4] = BadDrop.ToInt32();
            Header[5] = GoodDrop.ToInt32();
            Header[0] |= IsFinalChapterBoss ? 0x100000 : 0;  
        }
        for(int i = 0; i < 16; i++)
            bitPackage.WriteVarLong(Header[i]);
        for(int i = 0; i < 16; i++)
            bitPackage.WriteFloat(FloatingPoints[i]);
        bitPackage.WriteString(Visual);
        bitPackage.WriteString(UpdateScript);
        if(UseCreateScript)
            bitPackage.WriteString(CreateScript);
        if(UseRemoveScript)
            bitPackage.WriteString(RemoveScript);
        if(UseDieScript && !IsBullet)
            bitPackage.WriteString(DieScript);
    }

}
using DmitryAndDemid.Gameplay;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Data;

public class EditableGameObject
{
    public Rectangle SourceRectangle;
    public Rectangle DestinationRectangle;

    public int[] IntVariables = new int[72];
    public float[] FPVariables = new float[72];
    public int[] Instructions = new int[72];

    public string AppearScript = "";
    public string UpdateScript = "";
    public string DieScript = "";
    public string DisappearScript = "";
    public string Visual = "";
    public string EntityID = "";
    public string BossID = "";
    public bool DangerousForPlayer = false;
    public bool ApplyShader = false;
    public bool UseAppearScript = false;
    public bool ClearProtected = false;
    public bool UseDisappearScript = false;
    public bool DependsOnEntity = false;
    public bool IsBullet = false;

    public bool IsBoss = false;
    public bool UseDieScript = false;
    public bool UseBadDropScenario = false;
    

    public static EditableGameObject ReadFrom(ref BitPackage package)
    {
        EditableGameObject gameObject = new EditableGameObject();
        for(int i = 0; i < 72; i++)
            gameObject.IntVariables[i] = (int)package.ReadVarLong();
        for(int i = 0; i < 72; i++)
            gameObject.FPVariables[i] = package.ReadFloat();
        gameObject.DangerousForPlayer = (gameObject.IntVariables[0] & 0x0001) == 0x0001;
        gameObject.ApplyShader = (gameObject.IntVariables[0] & 0x0004) == 0x0004;
        gameObject.IsBullet = (gameObject.IntVariables[0] & 0x0008) == 0x0008;
        gameObject.ClearProtected = (gameObject.IntVariables[0] & 0x0010) == 0x0010;
        gameObject.UseAppearScript = (gameObject.IntVariables[0] & 0x0020) == 0x0020;
        gameObject.UseDisappearScript = (gameObject.IntVariables[0] & 0x0040) == 0x0040;
        gameObject.DependsOnEntity = (gameObject.IntVariables[0] & 0x0080) == 0x0080;
        gameObject.IsBoss = (gameObject.IntVariables[0] & 0x2000) == 0x2000;
        gameObject.UseBadDropScenario = (gameObject.IntVariables[0] & 0x8000) == 0x8000;
        if(gameObject.UseAppearScript)
            gameObject.AppearScript = package.ReadString();
        gameObject.UpdateScript = package.ReadString();
        if(gameObject.UseDisappearScript)
            gameObject.DisappearScript = package.ReadString();
        if (!gameObject.IsBullet)
        {
            gameObject.UseDieScript = (gameObject.IntVariables[0] & 0x4000) == 0x4000;
            if(gameObject.UseDieScript)
                gameObject.DieScript = package.ReadString();
        }
        else if(gameObject.IsBoss)
            gameObject.BossID = package.ReadString();
        gameObject.Visual = package.ReadString();
        gameObject.EntityID = package.ReadString();
        return gameObject;
    }

    public void WriteTo(ref BitPackage package)
    {
        IntVariables[0] = DangerousForPlayer ? 1 : 0;
        IntVariables[0] |= ApplyShader ? 0x4 : 0;
        IntVariables[0] |= IsBullet ? 0x8 : 0;
        IntVariables[0] |= ClearProtected ? 0x10 : 0;
        IntVariables[0] |= UseAppearScript ? 0x20 : 0;
        IntVariables[0] |= UseDisappearScript ? 0x40 : 0;
        IntVariables[0] |= DependsOnEntity ? 0x80 : 0;
        IntVariables[0] |= IsBoss ? 0x2000 : 0;
        IntVariables[0] |= UseDieScript ? 0x4000 : 0;
        IntVariables[0] |= UseBadDropScenario ? 0x8000 : 0;
        for(int i = 0; i < 72; i++)
            package.WriteVarLong(IntVariables[i]);
        for(int i = 0; i < 72; i++)
            package.WriteFloat(FPVariables[i]);
        if(UseAppearScript)
            package.WriteString(AppearScript);
        package.WriteString(UpdateScript);
        if(UseDisappearScript)
            package.WriteString(DisappearScript);
        if (IsBullet)
        {
            if(UseDieScript)
                package.WriteString(DieScript);
        }
        else if(IsBoss)
            package.WriteString(BossID);
        package.WriteString(Visual);
        package.WriteString(EntityID);
    }

    public GameObject Compile()
    {
        throw new NotImplementedException();
    }
}
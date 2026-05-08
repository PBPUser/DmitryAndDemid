using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Data;

public class EditableGameObject
{
    public Rectangle SourceRectangle;
    public Rectangle DestinationRectangle;

    public int[] IntVariables = new int[48];
    public float[] FPVariables = new float[48];

    public string AppearScript = "";
    public string UpdateScript = "";
    public string DisappearScript = "";
    public string Texture = "";
    public bool DangerousForPlayer = false;
    public bool ApplyShader = false;
    public bool UseCreateScript = false;
    public bool UseRemoveScript = false;
    public bool DependsOnEntity = false;
    public bool TextureGenerated = false;
    public bool IsBullet = false;
    

    public static EditableGameObject ReadFrom(BitPackage package)
    {
        EditableGameObject gameObject = new EditableGameObject();
        for(int i = 0; i < 48; i++)
            gameObject.IntVariables[i] = (int)package.ReadVarLong();
        for(int i = 0; i < 48; i++)
            gameObject.FPVariables[i] = package.ReadFloat();
        return new EditableGameObject();
    }

    public void WriteTo(ref BitPackage package)
    {
        for(int i = 0; i < 48; i++)
            package.WriteVarLong(IntVariables[i]);
        for(int i = 0; i < 48; i++)
            package.WriteFloat(FPVariables[i]);
    }
}
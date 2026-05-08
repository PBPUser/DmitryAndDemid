using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data;

public class EditableChapterInfo
{
    public int[] Header = new int[8];
    public List<EditableGameObject> GameObjects = new();
    public string Background = "";

    public int Type = 0;
    public bool Easy = false;
    public bool Normal = false;
    public bool Hard = false;
    public bool Max = false;
    public bool Extra = false;
    public bool TimeoutCard = false;
    public int Id = 0;
    public int Length = 0;
    public int CardNumber = 0;

    public static EditableChapterInfo Load(string filename)
    {
        using var stream = File.OpenRead(filename);
        EditableChapterInfo eci = new();
        var package = BitPackage.GetStreamReadPackage(stream);
        for (ulong i = 0; i < 8; i++)
            eci.Header[i] = (int)package.ReadVarLong();
        eci.Background = package.ReadString();
        int length = (int)package.ReadVarLong();
        for (int i = 0; i < length; i++)
        {
            eci.GameObjects.Add(EditableGameObject.ReadFrom(package));
        }
        stream.Close();
        return eci;
    }
    
    public ChapterType WChapterType
    {
        get => 
            (Header[0] & 0x1) == 0x1 ?
                (Header[0] & 0x2) == 0x2 ? ChapterType.Spell : ChapterType.NonSpell :
                (Header[0] & 0x2) == 0x2 ?  ChapterType.Continue : ChapterType.Default;
        set
        {
            switch (value)
            {
                case ChapterType.Default:  Header[0] &= ~0x1; Header[0] &= ~0x2; break;
                case ChapterType.Continue:  Header[0] &= ~0x1; Header[0] |= 0x2;  break;
                case ChapterType.NonSpell: Header[0] |= 0x1; Header[0] |= 0x2;  break;
                case ChapterType.Spell:  Header[0] |= 0x3; break;
            }
        }
    }

    bool WIsEasy
    {
        get => (Header[0] & 0x4) == 0x4;
        set => Header[0] = value ? Header[0] | 0x4 : Header[0] & ~0x4;
    }
    
    bool WIsNormal
    {
        get => (Header[0] & 0x8) == 0x8;
        set => Header[0] = value ? Header[0] | 0x8 : Header[0] & ~0x8;
    }

    bool WIsHard
    {
        get => (Header[0] & 0x10) == 0x10;
        set => Header[0] = value ? Header[0] | 0x10 : Header[0] & ~0x10;
    }

    bool WIsMax
    {
        get => (Header[0] & 0x20) == 0x20;
        set => Header[0] = value ? Header[0] | 0x20 : Header[0] & ~0x20;
    }

    bool WIsExtra
    {
        get => (Header[0] & 0x40) == 0x40;
        set => Header[0] = value ? Header[0] | 0x40 : Header[0] & ~0x40;
    }

    bool WIsTimeoutCard
    {
        get => (Header[0] & 0x100) == 0x100;
        set => Header[0] = value ? Header[0] | 0x100 : Header[0] & ~0x100;
    }

    int WID
    {
        get => Header[1];
        set =>  Header[1] = value;
    }

    int WLength
    {
        get => Header[2];
        set =>  Header[2] = value;
    }

    int WSpellCardNumber
    {
        get => Header[3];
        set =>  Header[3] = value;
    }
}
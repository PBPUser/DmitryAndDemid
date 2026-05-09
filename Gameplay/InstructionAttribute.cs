namespace DmitryAndDemid.Gameplay;

public class InstructionArgumentAttribute(string name, int index)
{
    public int Index = index;
    public string ArgumentName = name;
}
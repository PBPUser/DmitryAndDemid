using System.Runtime.InteropServices;

namespace DmitryAndDemid.Gameplay;

public static class Commands
{
    public static void Execute(int[] commands, ref GameObject gameObject)
    {
        int position = 0;
        while(position < commands.Length)
        {
            switch (commands[position])
            {
                case 1:
                    
                break;
                case 0:
                default:
                    
                    break;
            }
        }
    }
}

public class InstructionDefinition(int id, string name, params InstructionValue[] instructions)
{
    public int Id = id;
    public string Name = name;
    public InstructionValue[] Instructions = instructions;
}

public class InstructionValue(Type type)
{
    public Type Type = type == typeof(float) || type == typeof(int) || type == typeof(bool) ? type : throw new Exception();
}
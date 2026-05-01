using System.Reflection;
using DmitryAndDemid.Gameplay;

namespace DmitryAndDemid.Common;

public abstract class BulletAction
{
    public static Dictionary<string, Type> Actions = new();

    static BulletAction()
    {
        LoadRuntimeActions();
    }

    static void LoadRuntimeActions()
    {
        var list = Assembly.GetExecutingAssembly().GetTypes().Where(x => x.IsAssignableTo(typeof(BulletAction)) && !x.IsAbstract);
        foreach (var VARIABLE in list)
            Actions.Add(VARIABLE.Name, VARIABLE);
    }
    
    public virtual void Act(Bullet bullet)
    {
         
    }

    public virtual void Init(string[] values, Game game, Bullet bullet)
    {
        
    }
}
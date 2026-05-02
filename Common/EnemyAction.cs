using System.Reflection;
using DmitryAndDemid.Gameplay;

namespace DmitryAndDemid.Common;

public abstract class EnemyAction
{
    public bool LimitTicks = false;
    public int FromTick = 0;
    public int ToTick = 0;
    
    public static Dictionary<string, Type> Actions = new();
    
    static EnemyAction()
    {
        LoadRuntimeActions();
    }

    static void LoadRuntimeActions()
    {
        var list = Assembly.GetExecutingAssembly().GetTypes().Where(x => x.IsAssignableTo(typeof(EnemyAction)) && !x.IsAbstract);
        foreach (var VARIABLE in list)
            Actions.Add(VARIABLE.Name, VARIABLE);
    }
    
    public virtual void Act(Enemy enemy)
    {
         
    }

    public void InvokeAct(Enemy enemy)
    {
        if (LimitTicks)
        {
            var x = enemy.Game.CurrentTick - enemy.SpawnTick;
            if (x < FromTick || x > ToTick)
                return;
        }
        Act(enemy);
    }

    public virtual void Init(string[] values, Game game, Enemy enemy)
    {
        
    }
}
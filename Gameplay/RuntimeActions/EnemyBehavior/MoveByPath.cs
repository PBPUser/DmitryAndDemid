using System.Numerics;
using DmitryAndDemid.Common;

namespace DmitryAndDemid.Gameplay.RuntimeActions.EnemyBehavior;

public class MoveByPath : EnemyAction
{
    private Dictionary<int, Vector2> Path = new(); 

    public override void Init(string[] values, Game game, Enemy enemy)
    {
        if (values.Length % 2 == 1)
            return;
        string[] str;
        for (int i = 0; i < values.Length; i += 2)
        {
            str = values[i + 1].Split(",");
            Path.Add(short.Parse(values[i]), new Vector2(short.Parse(str[0]), short.Parse(str[1])));
        }
    }

    public override void Act(Enemy enemy)
    {
        var tick = enemy.Game.CurrentTick - enemy.SpawnTick;
        var elements = Path.OrderBy(x => MathF.Abs(x.Key-tick)).Take(2);
        elements.OrderBy(x => x.Key);
        enemy.UpdateCollisionRender(enemy.Final, enemy.RotateTo);
    }
}
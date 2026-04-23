using System.Numerics;

namespace DmitryAndDemid.Gameplay;

public struct RemovedBullet
{
    public Vector2 Position;
    public int Tick;
    
    public RemovedBullet(Vector2 position, int tick)
    {
        Position = position;
        Tick = tick;
    }
}
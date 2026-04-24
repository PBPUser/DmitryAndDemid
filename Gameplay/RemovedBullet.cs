using System.Numerics;

namespace DmitryAndDemid.Gameplay;

public struct RemovedBullet
{
    public Vector2 Position;
    public float Time;
    
    public RemovedBullet(Vector2 position, float time)
    {
        Position = position;
        Time = time;
    }
}
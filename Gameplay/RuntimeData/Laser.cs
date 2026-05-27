using System.Numerics;

namespace DmitryAndDemid.Gameplay.RuntimeData;

public class Laser
{
    public int CollisionWidth = 0;
    public bool IsDangerous = false;
    public Vector2 Position1 = Vector2.Zero;
    public Vector2 Position2 = Vector2.Zero;
    public bool IsCurve = false;
}
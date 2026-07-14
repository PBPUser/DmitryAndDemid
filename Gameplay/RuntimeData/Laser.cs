using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;

namespace DmitryAndDemid.Gameplay.RuntimeData;

public class Laser
{
    public Vector2 Position1 = Vector2.Zero;
    public Vector2 Position2 = Vector2.Zero;
    public bool IsCurve = false;
    public bool IsDangerous = false;
    public int CollisionWidth = 0;
}
using System.Numerics;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// The handful of Raymath helpers the game used. Pure math — there is no reason for these to come from a
/// graphics library, and keeping them here removes the last non-rendering reason to reference Raylib.
/// Behaviour matches Raymath exactly.
/// </summary>
public static class MathUtil
{
    public static float Clamp(float value, float min, float max) => MathF.Min(MathF.Max(value, min), max);

    public static float Clamp01(float value) => Clamp(value, 0f, 1f);

    public static float Lerp(float start, float end, float amount) => start + amount * (end - start);

    public static float Sign(float value) => value < 0 ? -1 : value > 0 ? 1 : 0;

    public static float MoveTowards(float from, float to, float delta)
    {
        float difference = to - from;
        return MathF.Abs(difference) <= delta ? to : from + MathF.Sign(difference) * delta;
    }

    public static float Vector2Distance(Vector2 a, Vector2 b) => Vector2.Distance(a, b);

    public static Vector2 Vector2MoveTowards(Vector2 from, Vector2 to, float maxDistance)
    {
        Vector2 delta = to - from;
        float squared = delta.LengthSquared();
        if (squared == 0 || (maxDistance >= 0 && squared <= maxDistance * maxDistance))
            return to;
        float distance = MathF.Sqrt(squared);
        return from + delta / distance * maxDistance;
    }
}

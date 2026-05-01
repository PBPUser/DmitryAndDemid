using Raylib_cs;

namespace DmitryAndDemid.Utils;

public static class Controller
{
    public static bool IsButtonDown(GamepadButton button)
    {
        for(int i =0; i < Runtime.CurrentRuntime.GamepadCount; i++)
            if (Raylib.IsGamepadButtonDown(1, button))
                return true;
        return false;
    }

    public static float GetGamepadAxisValue(GamepadAxis axis)
    {
        float tmp;
        float value = 0;
        for (int i = 0; i < Runtime.CurrentRuntime.GamepadCount; i++)
        {
            tmp = Raylib.GetGamepadAxisMovement(i, axis);
            if(MathF.Abs(tmp) > MathF.Abs(value))
                value = tmp;
        }
        return value;
    }
}
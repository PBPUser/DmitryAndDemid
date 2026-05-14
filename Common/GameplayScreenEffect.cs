using System.Numerics;
using System.Security.Principal;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace DmitryAndDemid.Common;

public class GameplayScreenEffect
{
    public GameplayScreenEffect(GameBox box, Vector2 position, int index, string shader, float timeAppear, float timeDisappear)
    {
        Shader = Runtime.CurrentRuntime.Shaders[shader];
        LocationPosition = GetShaderLocation(Shader, "position");
        LocationTime = GetShaderLocation(Shader, "time");
        TimeAppear = timeAppear;
        TimeDisappear = timeDisappear;
        Box = box;
        Position = position;
        ZIndex = index;
    }

    public Vector2 Position;
    public int LocationTime;
    public int LocationPosition;
    public Shader Shader;
    public GameBox Box;
    public float TimeAppear = 0;
    public float TimeDisappear = 0;
    public int ZIndex = 0;
    
    public virtual float State(float time) => ((time - TimeAppear) / (TimeDisappear - TimeAppear));

    public void ApplyShading(float time)
    {
        if (time > TimeDisappear)
        {
            Box.RemoveScreenEffect(this);
        }
        SetShaderValue(Shader, LocationTime, State(time), ShaderUniformDataType.Float);
        SetShaderValue(Shader, LocationPosition, Position, ShaderUniformDataType.Vec2);
        BeginShaderMode(Shader);
    }
}
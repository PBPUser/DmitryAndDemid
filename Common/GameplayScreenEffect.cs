using DmitryAndDemid.Rendering;
using System.Numerics;
using System.Security.Principal;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Common;

public class GameplayScreenEffect
{
    public GameplayScreenEffect(GameBox box, Vector2 position, int index, string shader, float timeAppear, float timeDisappear)
    {
        Box = box;
        Shader = Runtime.CurrentRuntime.Shaders[shader];
        LocationPosition = GetShaderLocation(Shader, "position");
        LocationRealTime = GetShaderLocation(Shader, "realTime");
        LocationTime = GetShaderLocation(Shader, "time");
        TimeAppear = timeAppear;
        TimeDisappear = timeDisappear;
        Position = position;
        ZIndex = index;
    }

    public Vector2 Position;
    public int LocationRealTime;
    public int LocationTime;
    public int LocationPosition;
    public ShaderHandle Shader;
    public GameBox Box;
    public float TimeAppear = 0;
    public float TimeDisappear = 0;
    public int ZIndex = 0;
    public bool UseRealTime = false;
    public float StepLength = 0f;
    public bool UseSteps = false;
    public Guid Guid = Guid.NewGuid();
    public EffectLayer Layer = EffectLayer.BackgroundAndGameplay;

    public override bool Equals(object? obj) => obj is GameplayScreenEffect other && Guid.Equals(other.Guid);

    public virtual float State(float time) => ((time - TimeAppear) / (TimeDisappear - TimeAppear));

    public virtual float State2(float time)
    {
        float a = MathUtil.Clamp01((time - TimeAppear) / StepLength);
        float b = MathUtil.Clamp01((TimeDisappear - time) / StepLength);
        return a * b;
    }
    
    public virtual void ApplyShading(float gameTime)
    {
        float time = UseRealTime ? (float)GetTime() : gameTime;
        if (time > TimeDisappear)
        {
            Box.RemoveScreenEffect(this);
            return;
        }

        float state = UseSteps ? State2(time) : State(time);
#if DEBUG
        Box.DebugStrings.Add($"Shader: {Shader}");
        Box.DebugStrings.Add($"State: {state}");
#endif
        SetShaderValue(Shader, LocationTime, state, UniformType.Float);
        SetShaderValue(Shader, LocationRealTime, gameTime, UniformType.Float);
        SetShaderValue(Shader, LocationPosition, Position, UniformType.Vec2);
        BeginShaderMode(Shader);
    }

    public enum EffectLayer
    {
        BackgroundOnly,
        BackgroundAndGameplay,
        GameplayOnly,
        All
    }
}
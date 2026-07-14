using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class BossHealthOverlay : GameplayOverlay
{
    public BossHealthOverlay(GameBox box, RuntimeObject boss) : base(box, 0.5f, 99999)
    {
        Boss = boss;
        LocationPosition = GetShaderLocation(BossHealthOverlayShader, "position");
        LocationRealTime = GetShaderLocation(BossHealthOverlayShader, "realTime");
        LocationPoints = GetShaderLocation(BossHealthOverlayShader, "points");
        LocationPointsCount = GetShaderLocation(BossHealthOverlayShader, "pointsCount");
        LocationProgress = GetShaderLocation(BossHealthOverlayShader, "progress");
    }

    private RuntimeObject Boss;
    ShaderHandle BossHealthOverlayShader = Runtime.CurrentRuntime.Shaders["boss_health_bar"];
    private int LocationPosition, LocationRealTime, LocationPoints, LocationPointsCount, LocationProgress;

    protected override void Draw()
    {
        if((Boss.Header[0] & RuntimeObject.FlagIsDied) == RuntimeObject.FlagIsDied)
            Box.RemoveOverlay(this);
        SetShaderValue(BossHealthOverlayShader, LocationPosition, new Vector2(Boss.X, Boss.Y), UniformType.Vec2);
        SetShaderValue(BossHealthOverlayShader, LocationProgress, (Boss.FloatingPoints[0] / Boss.FloatingPoints[0xa]) * State, UniformType.Float);
        BeginShaderMode(BossHealthOverlayShader);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["384x448"], new Rect(0,0,384,-448), new Rect(0,0,new Vector2(384,448) * Runtime.CurrentRuntime.ScaleF), Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        base.Draw();
    }
}
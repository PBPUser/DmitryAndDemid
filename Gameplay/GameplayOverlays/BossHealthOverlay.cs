using System.Numerics;
using DmitryAndDemid.Common;
using Raylib_cs;
using static Raylib_cs.Raylib;

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
    Shader BossHealthOverlayShader = Runtime.CurrentRuntime.Shaders["boss_health_bar"];
    private int LocationPosition, LocationRealTime, LocationPoints, LocationPointsCount, LocationProgress;

    protected override void Draw()
    {
        SetShaderValue(BossHealthOverlayShader, LocationPosition, new Vector2(Boss.X, Boss.Y), ShaderUniformDataType.Vec2);
        SetShaderValue(BossHealthOverlayShader, LocationProgress, (Boss.FloatingPoints[0] / Boss.FloatingPoints[0xa]) * State, ShaderUniformDataType.Float);
        BeginShaderMode(BossHealthOverlayShader);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["384x448"], new Rectangle(0,0,384,-448), new Rectangle(0,0,new Vector2(384,448) * Runtime.CurrentRuntime.ScaleF), Vector2.Zero, 0, Color.White);
        EndShaderMode();
        base.Draw();
    }
}
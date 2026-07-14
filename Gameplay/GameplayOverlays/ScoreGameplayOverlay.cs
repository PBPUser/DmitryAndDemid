using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class ScoreGameplayOverlay : GameplayOverlay
{
    public ScoreGameplayOverlay(GameBox box, int score, int ticks, double time, float animationLength, float length) : base(box, animationLength, length)
    {
        TimersTexture = LoadRenderTexture(
            (int)(Runtime.CurrentRuntime.ScaleF * 128),
            (int)(Runtime.CurrentRuntime.ScaleF * 96)
        );
        Texture = Runtime.CurrentRuntime.Textures["get-spell-card.png"];
        SourceRectangle = Helper.GetFullSource(Texture);
        DestinationRectangle = new Rect(0, 64 * Runtime.CurrentRuntime.ScaleF, SourceRectangle.Size / 4  * Runtime.CurrentRuntime.ScaleF);
        SourceRectangle2 = Helper.GetFullSourceRenderTexture(TimersTexture);
        DestinationRectangle2 =
            new Rect(
                TimersTexture.Texture.Width,
                176 * Runtime.CurrentRuntime.ScaleF,
                TimersTexture.Texture.Width,
                TimersTexture.Texture.Height
            );
        Helper.DrawTimerSplash(TimersTexture, ticks, time);
        ScoreTexture = LoadRenderTexture((int)(384 * Runtime.CurrentRuntime.ScaleF), (int)(448 * Runtime.CurrentRuntime.ScaleF));
        Helper.DrawSpellScore(score.ToString(), ref ScoreTexture, out LetterWidth, out TextWidth);
        SourceRectangle3 = Helper.GetFullSourceRenderTexture(ScoreTexture);
        DestinationRectangle3 = Helper.GetFullSource(ScoreTexture.Texture);
    }
    
    static ShaderHandle JumpingShader = Runtime.CurrentRuntime.Shaders["score_jump"];
    private static int LocationJumpingRes = GetShaderLocation(JumpingShader, "resolution");
    private static int LocationJumpingLetterWidth = GetShaderLocation(JumpingShader, "letterWidth");
    private static int LocationJumpingTime = GetShaderLocation(JumpingShader, "time");
    static int LocationJumpingTextWidth  = GetShaderLocation(JumpingShader, "textWidth");
    private float TextWidth;
    private float LetterWidth;
    private TargetHandle TimersTexture;
    private TargetHandle ScoreTexture;
    private TextureHandle Texture;
    private Rect SourceRectangle;
    private Rect DestinationRectangle;
    private Rect SourceRectangle2;
    private Rect DestinationRectangle2;
    private Rect SourceRectangle3;
    private Rect DestinationRectangle3;

    protected override void Unload()
    {
        UnloadRenderTexture(TimersTexture);
        UnloadRenderTexture(ScoreTexture);
    }

    protected override void Draw()
    {
        float state = State;
        Rect rectangle = DestinationRectangle with { Height = DestinationRectangle.Height * state };
        DrawTexturePro(Texture, SourceRectangle, rectangle, Vector2.Zero, 0, Rgba.White);
        DrawTexturePro(TimersTexture.Texture, SourceRectangle2, DestinationRectangle2, Vector2.Zero, 0, Rgba.White with {A = Helper.TimeToTransparency(state)});
        SetShaderValue(JumpingShader, LocationJumpingRes, DestinationRectangle3.Size, UniformType.Vec2);
        SetShaderValue(JumpingShader, LocationJumpingTime, Box.GetTime() - TimeAppear, UniformType.Float);
        SetShaderValue(JumpingShader, LocationJumpingLetterWidth, LetterWidth, UniformType.Float);
        SetShaderValue(JumpingShader, LocationJumpingTextWidth, TextWidth, UniformType.Float);
        BeginShaderMode(JumpingShader);
        DrawTexturePro(ScoreTexture.Texture, SourceRectangle3, DestinationRectangle3, Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
    }
}
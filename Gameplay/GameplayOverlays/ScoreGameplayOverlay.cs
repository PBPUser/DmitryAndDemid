using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;

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
        DestinationRectangle = new Rectangle(0, 64 * Runtime.CurrentRuntime.ScaleF, SourceRectangle.Size / 4  * Runtime.CurrentRuntime.ScaleF);
        SourceRectangle2 = Helper.GetFullSourceRenderTexture(TimersTexture);
        DestinationRectangle2 =
            new Rectangle(
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
    
    static Shader JumpingShader = Runtime.CurrentRuntime.Shaders["score_jump"];
    private static int LocationJumpingRes = GetShaderLocation(JumpingShader, "resolution");
    private static int LocationJumpingLetterWidth = GetShaderLocation(JumpingShader, "letterWidth");
    private static int LocationJumpingTime = GetShaderLocation(JumpingShader, "time");
    static int LocationJumpingTextWidth  = GetShaderLocation(JumpingShader, "textWidth");
    private float TextWidth;
    private float LetterWidth;
    private RenderTexture2D TimersTexture;
    private RenderTexture2D ScoreTexture;
    private Texture2D Texture;
    private Rectangle SourceRectangle;
    private Rectangle DestinationRectangle;
    private Rectangle SourceRectangle2;
    private Rectangle DestinationRectangle2;
    private Rectangle SourceRectangle3;
    private Rectangle DestinationRectangle3;

    protected override void Unload()
    {
        UnloadRenderTexture(TimersTexture);
        UnloadRenderTexture(ScoreTexture);
    }

    protected override void Draw()
    {
        float state = State;
        Rectangle rectangle = DestinationRectangle with { Height = DestinationRectangle.Height * state };
        DrawTexturePro(Texture, SourceRectangle, rectangle, Vector2.Zero, 0, Color.White);
        DrawTexturePro(TimersTexture.Texture, SourceRectangle2, DestinationRectangle2, Vector2.Zero, 0, Color.White with {A = Helper.TimeToTransparency(state)});
        SetShaderValue(JumpingShader, LocationJumpingRes, DestinationRectangle3.Size, ShaderUniformDataType.Vec2);
        SetShaderValue(JumpingShader, LocationJumpingTime, Box.GetTime() - TimeAppear, ShaderUniformDataType.Float);
        SetShaderValue(JumpingShader, LocationJumpingLetterWidth, LetterWidth, ShaderUniformDataType.Float);
        SetShaderValue(JumpingShader, LocationJumpingTextWidth, TextWidth, ShaderUniformDataType.Float);
        BeginShaderMode(JumpingShader);
        DrawTexturePro(ScoreTexture.Texture, SourceRectangle3, DestinationRectangle3, Vector2.Zero, 0, Color.White);
        EndShaderMode();
    }
}
using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Backgrounds;

public class SwapStageBackground(StageBackground top, StageBackground bottom) : StageBackground
{
    public byte OpacityTop = 255;
    private TargetHandle TextureTemporary = LoadRenderTexture(384, 448);

    // Crossfade state, driven by the "swap" event (an example of a background reacting to gameplay events).
    private const int SwapTicks = 60;
    private int Tick;
    private int SwapStartTick = int.MinValue;
    private bool TargetTop = true;

    protected override void Update(int tick, float delta)
    {
        Tick = tick;
        if (SwapStartTick != int.MinValue)
        {
            float p = Math.Clamp((tick - SwapStartTick) / (float)SwapTicks, 0f, 1f);
            float from = TargetTop ? 0f : 255f, to = TargetTop ? 255f : 0f;
            OpacityTop = (byte)(from + (to - from) * p);
        }
    }

    /// <summary>A "swap" event crossfades between the two layers; anything else is ignored.</summary>
    public override void OnEvent(string name, float value = 0f)
    {
        if (name != "swap")
            return;
        TargetTop = !TargetTop;
        SwapStartTick = Tick;
    }

    protected override void Render(TargetHandle texture, int tick, float delta)
    {
        top.Draw(TextureTemporary, tick, delta);
        bottom.Draw(texture, tick, delta);
        BeginTextureMode(TextureTemporary);
        DrawTexturePro(TextureTemporary.Texture, 
            new Rect(0, 0, 384, -448),
            new Rect(0, 0, 384, 448),
            Vector2.Zero, 0, Rgba.White with { A = OpacityTop });
        EndTextureMode();
    }

    protected override void Unload()
    {
        UnloadRenderTexture(TextureTemporary);
        base.Unload();
    }
}
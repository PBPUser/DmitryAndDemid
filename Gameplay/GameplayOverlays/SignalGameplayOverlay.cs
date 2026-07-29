using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.GameplayOverlays;

public class SignalGameplayOverlay : GameplayOverlay
{
    public SignalGameplayOverlay(GameBox box) : base(box, 0, float.MaxValue)
    {
        NinePatch = new NinePatch()
        {
            Layout = NinePatchLayout.ThreePatchVertical,
            Top = 7,
            Bottom = 7
        };
        GrayedSource = WhiteSource = NetworkSource = new Rect(0, 188, 27, 47);
        WhiteSource.X += 32;
        NetworkSource.X += 60;
        Width = 7 * Runtime.CurrentRuntime.ScaleF;
        Height = 12 * Runtime.CurrentRuntime.ScaleF;
        Padding = 2 * Runtime.CurrentRuntime.ScaleF;
        // The bars run from x = Padding to the right edge of the seventh one; that span is what the provider
        // line below is fitted to.
        BlockWidth = (Padding + Width) * 6 + Width;
        PrecacheProviderLines();
    }

    /// <summary>
    /// Bakes all three provider lines up front — this runs from the GameBox constructor, i.e. while the run is
    /// still loading, alongside the render targets built there. They are freed together in <see cref="Unload"/>,
    /// which the box calls on every overlay when gameplay or a replay closes.
    ///
    /// Baking here rather than on demand also pins the wording: <see cref="Helper.Translate"/> picks at random
    /// between a key's ';' variants, so resolving a line mid-run could hand back a different variant each time
    /// the signal crossed a threshold.
    /// </summary>
    private void PrecacheProviderLines()
    {
        float sf = Runtime.CurrentRuntime.ScaleF;
        string provider = Helper.Translate($"celluar.{Box.ProtogonistId}");
        string[] lines =
        [
            Helper.Translate("celluar.no_network"),
            Helper.Translate("celluar.emergency_only").Replace("%s", provider),
            provider,
        ];
        for (int i = 0; i < lines.Length; i++)
            Helper.DrawTextGradient(out ProviderTextures[i], Runtime.CurrentRuntime.Fonts["kodemono"], 8 * sf,
                lines[i], Rgba.White, 1 * sf, 1.5f * sf);
    }

    private NinePatch NinePatch;
    private Rect GrayedSource;
    private Rect WhiteSource;
    private Rect NetworkSource;
    private float Height;
    private float Width;
    private float Padding;
    private float BlockWidth;

    // The mobile-services provider, drawn under the signal icon. Each protagonist has their own carrier, keyed
    // "celluar.<person id>" in translation.json. All three lines — no service, emergency only, the carrier —
    // are baked at load and indexed by ProviderState.
    private readonly TargetHandle[] ProviderTextures = new TargetHandle[3];
    private int ProviderState = 2;
    private double MarqueeStart;

    /// <summary>How far the icon is lifted to make room for the provider line: exactly that line's height, so
    /// the text starts where the bars stop and the pair still ends on the playfield's bottom padding.</summary>
    private float Lift => ProviderTextures[ProviderState].IsValid
        ? ProviderTextures[ProviderState].Texture.Height
        : 0;

    /// <summary>Bars lit for the current signal, matching the test the draw loop uses.</summary>
    private int LitBars => Math.Min(7, (int)MathF.Ceiling(MathF.Sqrt(Box.Player.Signal)));

    /// <summary>At or below this many lit bars (of seven) the connection counts as too poor to use, and the
    /// line reads "emergency only" instead of the carrier's name.</summary>
    private const int PoorSignalBars = 1;

    public override void Update()
    {
        // No baking here — only picking which precached line is current, and restarting the scroll when that
        // changes so a newly shown name is read from its start rather than mid-slide.
        int state = LitBars == 0 ? 0 : LitBars <= PoorSignalBars ? 1 : 2;
        if (state != ProviderState)
        {
            ProviderState = state;
            MarqueeStart = Box.GetTime();
        }
        base.Update();
    }

    protected override void Unload()
    {
        for (int i = 0; i < ProviderTextures.Length; i++)
        {
            if (ProviderTextures[i].IsValid)
                UnloadRenderTexture(ProviderTextures[i]);
            ProviderTextures[i] = TargetHandle.None;
        }
        base.Unload();
    }

    protected override void Draw()
    {
        float sf = Runtime.CurrentRuntime.ScaleF;
        float bottom = 448 * sf - Padding - Lift;      // where the bars now end, the provider line picking up there
        float h, y;
        for (int i = 0; i < 7; i++)
        {
            h = (float)(Height * (0.3 + 0.1 * i));
            y = Height - h;
            DrawTextureNPatch(Runtime.CurrentRuntime.Textures["ingame-stuff.png"],
                NinePatch with { Source = (MathF.Sqrt(Box.Player.Signal) > i ? WhiteSource : GrayedSource) },
                new Rect(Padding + (Padding + Width) * i, bottom - Height + y, Width, h),
                Vector2.Zero, 0, Rgba.White);
        }
        DrawTexturePro(Runtime.CurrentRuntime.Textures["ingame-stuff.png"], NetworkSource,
            new Rect(Padding, 432 * sf - Lift, NetworkSource.Size / 4 * sf), Vector2.Zero, 0, Rgba.White);
        DrawProvider(bottom);
        base.Draw();
    }

    /// <summary>
    /// The carrier name under the icon, clipped to the icon's width. A name too long for that scrolls the way
    /// an over-wide settings entry does — hold at the start, slide to reveal the end, hold, slide back — just
    /// slower, since this line is a fraction of a menu item's size.
    /// </summary>
    private void DrawProvider(float top)
    {
        if (!ProviderTextures[ProviderState].IsValid)
            return;
        TextureHandle tex = ProviderTextures[ProviderState].Texture;
        float srcW = MathF.Min(tex.Width, BlockWidth);
        float overflow = tex.Width - srcW;
        float srcX = 0f;
        if (overflow > 0.5f)
        {
            float hold = 0.9f, speed = 18f * Runtime.CurrentRuntime.ScaleF;   // seconds paused; texels/second
            float slide = overflow / speed;
            float total = 2f * (hold + slide);
            float p = (float)((Box.GetTime() - MarqueeStart) % total);
            if (p < hold) srcX = 0f;
            else if (p < hold + slide) srcX = (p - hold) / slide * overflow;
            else if (p < 2f * hold + slide) srcX = overflow;
            else srcX = overflow * (1f - (p - 2f * hold - slide) / slide);
        }
        DrawTexturePro(tex, new Rect(srcX, 0, srcW, tex.Height),
            new Rect(Padding, top, srcW, tex.Height), Vector2.Zero, 0, Rgba.White);
    }
}

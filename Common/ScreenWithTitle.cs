using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;
using static DmitryAndDemid.Utils.Helper;

namespace DmitryAndDemid.Common;

public abstract class ScreenWithTitle : Screen
{
    static ScreenWithTitle()
    {
        MenuTextureTarget = Scale(new(0, 0, 640, 135), Runtime.CurrentRuntime.Scale);
    }
    
    protected float TimeDisappearTitle = float.MaxValue;
    protected float TimeAppearTitle = float.MinValue;
    private TextureHandle MenuTitleTexture;
    private static Rect MenuTextureSource = new Rect(0, 0, 1920, 270);
    private static Rect MenuTextureTarget;
    protected float AppearingTime = .5f;
    protected float DisappearingTime = .5f;
    protected void SetTitle(TextureHandle title)
    {
        MenuTitleTexture = title;
    }
    
    
    public override void Activated()
    {
        TimeAppearTitle = (float)GetTime();
        TimeDisappearTitle = float.MaxValue;
        base.Activated();
    }

    public override void Deactivated()
    {
        TimeDisappearTitle = (float)GetTime() + DisappearingTime;
        base.Deactivated();
    }

    /// <summary>Progress of the title banner's slide-in / slide-out, 0 → 1.</summary>
    protected float TitleAppearProgress =>
        (float)ComputeObjectTime(GetTime(), TimeAppearTitle, AppearingTime, TimeDisappearTitle, DisappearingTime);

    /// <summary>Vertical offset the banner is currently drawn at (negative while it is still sliding in).
    /// Screens that decorate the banner — the music room's notes — add it so the decoration travels with it
    /// instead of hanging in place while the banner slides.
    ///
    /// Once it has landed the banner keeps breathing: a slow bob of a couple of pixels, faded in with the slide
    /// so it never fights the entrance. It lives here rather than at the draw so anything pinned to the banner
    /// rides along with it.
    ///
    /// The bob only ever goes UP (its range is [-amplitude, 0]). The banner is flush with the top of the screen
    /// and spans its full width, so any downward travel would open a strip of bare background above it; riding
    /// up into the offscreen area instead costs a couple of pixels off its bottom edge and shows nothing.</summary>
    protected float TitleOffsetY
    {
        get
        {
            float progress = TitleAppearProgress;
            float bob = -(0.5f + 0.5f * MathF.Sin((float)GetTime() * 1.1f)) * 3f * Runtime.CurrentRuntime.ScaleF * progress;
            return (1 - Pow2F(progress)) * MenuTextureTarget.Height * -1 + bob;
        }
    }

    /// <summary>The banner's on-screen rectangle, for positioning decorations against it.</summary>
    protected static Rect TitleArea => MenuTextureTarget;

    protected void DrawTitle()
    {
        // A slow lateral drift, done by sliding the SOURCE window rather than the drawn rectangle: the banner is
        // flush to both edges of the screen, so nudging where it is DRAWN would open a bare strip down one side.
        // The crop is narrowed by the pan distance so the window can travel its whole range without ever
        // sampling past the art, and the destination stays exactly full-width — the cost is stretching the
        // banner by TitlePanTexels/1920 (about a third of a percent), which is not visible. Runs on a different
        // period from the vertical bob so the two never settle into an obvious loop.
        float pan = (MathF.Sin((float)GetTime() * 0.7f + 1.3f) + 1f) * 0.5f * TitlePanTexels * TitleAppearProgress;
        Rect source = MenuTextureSource with { X = MenuTextureSource.X + pan, Width = MenuTextureSource.Width - TitlePanTexels };
        DrawTexturePro(MenuTitleTexture, source, MenuTextureTarget with { Y = TitleOffsetY }, Vector2.Zero, 0, Rgba.White);
    }

    /// <summary>How far the banner's source window drifts sideways, in texels of the 1920-wide banner art. Kept
    /// small: it is a slow breath across the art, not a scroll.</summary>
    private const float TitlePanTexels = 6f;
    
    public virtual void Exiting()
    {
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
        TimeDisappear = (float)GetTime() + DisappearingTime;
        TimeDisappearTitle = (float)GetTime() + DisappearingTime;
    }
}
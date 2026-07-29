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
    /// instead of hanging in place while the banner slides.</summary>
    protected float TitleOffsetY => (1 - Pow2F(TitleAppearProgress)) * MenuTextureTarget.Height * -1;

    /// <summary>The banner's on-screen rectangle, for positioning decorations against it.</summary>
    protected static Rect TitleArea => MenuTextureTarget;

    protected void DrawTitle()
    {
        DrawTexturePro(MenuTitleTexture, MenuTextureSource, MenuTextureTarget with { Y = TitleOffsetY }, Vector2.Zero, 0, Rgba.White);
    }
    
    public virtual void Exiting()
    {
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
        TimeDisappear = (float)GetTime() + DisappearingTime;
        TimeDisappearTitle = (float)GetTime() + DisappearingTime;
    }
}
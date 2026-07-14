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

    protected void DrawTitle()
    {
        float appear = (float)ComputeObjectTime(GetTime(), TimeAppearTitle, AppearingTime, TimeDisappearTitle, DisappearingTime);
        DrawTexturePro(MenuTitleTexture, MenuTextureSource, MenuTextureTarget with { Y = (1-Pow2F(appear)) * MenuTextureTarget.Height * -1 }, Vector2.Zero, 0, Rgba.White);
    }
    
    public virtual void Exiting()
    {
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
        TimeDisappear = (float)GetTime() + DisappearingTime;
        TimeDisappearTitle = (float)GetTime() + DisappearingTime;
    }
}
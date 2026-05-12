using System.Numerics;
using DmitryAndDemid.Utils;
using Raylib_cs;
using static Raylib_cs.Raylib;
using static DmitryAndDemid.Utils.Helper;

namespace DmitryAndDemid.Common;

public abstract class ScreenWithTitle : Screen
{
    static ScreenWithTitle()
    {
        MenuTextureTarget = Scale(
            new Rectangle(0, 0, 640, 135), 
            Runtime.CurrentRuntime.Scale);
    }
    
    protected void SetTitle(Texture2D title)
    {
        MenuTitleTexture = title;
    }
    
    protected float TimeDisappearTitle = float.MaxValue;
    protected float TimeAppearTitle = float.MinValue;
    private Texture2D MenuTitleTexture;
    private static Rectangle MenuTextureSource = new Rectangle(0, 0, 1920, 270);
    private static Rectangle MenuTextureTarget;
    protected float AppearingTime = .5f;
    protected float DisappearingTime = .5f;
    
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
        float appear = (float)ComputeObjectTime(Raylib.GetTime(), TimeAppearTitle, AppearingTime, TimeDisappearTitle, DisappearingTime);
        DrawTexturePro(MenuTitleTexture, MenuTextureSource, MenuTextureTarget with { Y = (1-Pow2F(appear)) * MenuTextureTarget.Height * -1 }, Vector2.Zero, 0, Color.White);
    }
    
    public virtual void Exiting()
    {
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
        TimeDisappear = (float)GetTime() + DisappearingTime;
        TimeDisappearTitle = (float)GetTime() + DisappearingTime;
    }
}
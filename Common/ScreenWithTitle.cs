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

    public override void Activated()
    {
        TimeAppearTitle = (float)GetTime();
        TimeDisappearTitle = float.MaxValue;
        base.Activated();
    }

    public override void Deactivated()
    {
        TimeDisappearTitle = (float)GetTime() + 0.5f;
        base.Deactivated();
    }

    protected void DrawTitle()
    {
        float appear = (float)ComputeObjectTime(Raylib.GetTime(), TimeAppearTitle, .5f, TimeDisappearTitle, .5f);
        DrawTexturePro(MenuTitleTexture, MenuTextureSource, MenuTextureTarget with { Y = (1-Pow2F(appear)) * MenuTextureTarget.Height * -1 }, Vector2.Zero, 0, Color.White);
    }
    
    public virtual void Exiting()
    {
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
        TimeDisappear = (float)GetTime() + 0.5f;
        TimeDisappearTitle = (float)GetTime() + 0.5f;
    }
}
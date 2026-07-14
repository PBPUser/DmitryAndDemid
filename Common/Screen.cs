using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Common;

public abstract class Screen : IDisposable
{
    public static int LastIndex = 0;
    public int Index = 0;
    
    public Screen()
    {
    }

    public bool IsInitialized = false;

    protected virtual void Created()
    {
    }

    public void TargetCreate()
    {
        if(!IsInitialized)
            Created();
        IsInitialized = true;
    }

    public virtual void Render()
    {
    }

    public virtual void TopUpdate()
    {
    }

    public virtual void PreRender(double delta)
    {
    }

    public virtual void Unload()
    {
    }

    public virtual void Activated()
    {
        TimeAppear = (float)GetTime();
        TimeDisappear = 99999999999f;
    }

    public virtual void Deactivated()
    {
    }

    public void TargetOpen()
    {
        Index = LastIndex;
        LastIndex++;
        Openned();
    } 
    
    protected virtual void Openned()
    {
        
    }

    public void Dispose()
    {
        Unload();
    }
#if DEBUG
    public virtual void DrawImgui()
    {
        
    }
#endif

    public float TimeAppear = 0f;
    public float TimeDisappear = 99999999f;

    TextureHandle Background;
    Rect BGRectSource;
    Rect BGRectDest;

    public void SetBackground(TextureHandle bg)
    {
        BGRectSource = Helper.GetFullSource(bg);
        BGRectDest = Helper.GetFullscreenSource();
        Background = bg;
    }

    public void DrawBackground()
    {
        DrawTexturePro(Background, BGRectSource, BGRectDest, Vector2.Zero, 0,
            Rgba.White with
            {
                A = (byte)(255 * Helper.ComputeObjectTime(GetTime(), TimeAppear, .5f, TimeDisappear, .5f))
            });
    }
}
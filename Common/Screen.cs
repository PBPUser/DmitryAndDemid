using System.Numerics;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Common;

public abstract class Screen : IDisposable
{
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
        TimeAppear = (float)Raylib.GetTime();
        TimeDisappear = 99999999999f;
    }

    public virtual void Deactivated()
    {
    }

    public void Dispose()
    {
        Unload();
    }

    public float TimeAppear = 0f;
    public float TimeDisappear = 99999999f;

    Texture2D Background;
    Rectangle BGRectSource;
    Rectangle BGRectDest;

    public void SetBackground(Texture2D bg)
    {
        BGRectSource = Helper.GetFullSource(bg);
        BGRectDest = Helper.GetFullscreenSource();
        Background = bg;
    }

    public void DrawBackground()
    {
        Raylib.DrawTexturePro(Background, BGRectSource, BGRectDest, Vector2.Zero, 0,
            Color.White with
            {
                A = (byte)(255 * Helper.ComputeObjectTime(Raylib.GetTime(), TimeAppear, .5f, TimeDisappear, .5f))
            });
    }
}
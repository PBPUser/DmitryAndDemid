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
        float appear = (float)Helper.ComputeObjectTime(GetTime(), TimeAppear, .5f, TimeDisappear, .5f);
        DrawTexturePro(Background, BGRectSource, DriftedBackgroundRect(), Vector2.Zero, 0,
            Rgba.White with { A = (byte)(255 * appear) });
        DrawFallingForks(appear);
    }

    /// <summary>How far past the screen the backdrop is blown up, as a fraction of its size.</summary>
    private const float BackdropOverscan = 0.05f;

    /// <summary>
    /// The backdrop drawn slightly larger than the screen and drifting inside that margin — a slow pan and a
    /// slower zoom, on periods long enough (7 s and 11 s, and not multiples of each other) that the loop never
    /// announces itself. It turns every menu's wallpaper from a flat still into something that breathes, and
    /// every screen that calls <see cref="DrawBackground"/> gets it at once.
    ///
    /// The overscan is the whole trick: the drawn rect is never smaller than the screen and the pan is bounded
    /// by exactly the spare margin the zoom created, so no amount of drift can pull an edge into view.
    /// </summary>
    private Rect DriftedBackgroundRect()
    {
        float time = (float)GetTime();
        float zoom = 1f + BackdropOverscan * (0.5f + 0.5f * MathF.Sin(time / 11f));
        float width = BGRectDest.Width * zoom, height = BGRectDest.Height * zoom;
        float spareX = width - BGRectDest.Width, spareY = height - BGRectDest.Height;
        // pan ∈ [-1, 1] places the rect anywhere from "flush right" to "flush left"; at the bottom of the zoom
        // there is no spare at all and both terms collapse to a plain full-screen blit.
        float panX = MathF.Sin(time / 7f);
        float panY = MathF.Cos(time / 9f);
        return new Rect(
            BGRectDest.X - spareX / 2f + panX * spareX / 2f,
            BGRectDest.Y - spareY / 2f + panY * spareY / 2f,
            width, height);
    }

    private const int ForkCount = 16;
    private static readonly Rgba ForkTop = new(120, 20, 30, 255);      // deep red at the top of the fall
    private static readonly Rgba ForkBottom = new(230, 90, 40, 255);   // warmer, brighter near the floor

    /// <summary>
    /// Forks drifting down like leaves behind the menu, shaded with a top-to-bottom reddish gradient. Only the
    /// screens that call <see cref="DrawBackground"/> get them — which excludes the main menu and the in-game
    /// pause, both of which paint their own background. Purely a function of time and index, so no per-screen
    /// state to carry.
    /// </summary>
    private void DrawFallingForks(float appear)
    {
        if (!Runtime.CurrentRuntime.Textures.TryGetValue("forkCut.png", out TextureHandle fork))
            return;

        float time = (float)GetTime();
        float width = Runtime.CurrentRuntime.Width;
        float height = Runtime.CurrentRuntime.Height;
        float scale = Runtime.CurrentRuntime.ScaleF;
        float size = 26 * scale;
        float drawScale = size / fork.Height;
        Rect source = new(0, 0, fork.Width, fork.Height);

        for (int i = 0; i < ForkCount; i++)
        {
            // A stable per-fork pseudo-random spread, no RNG state.
            float r1 = Frac(MathF.Sin(i * 12.9898f) * 43758.55f);
            float r2 = Frac(MathF.Sin(i * 78.233f) * 12543.13f);
            float r3 = Frac(MathF.Sin(i * 39.425f) * 21783.19f);

            float fallSpeed = (30f + r1 * 45f) * scale;
            float span = height + size * 2;
            float progress = (time * fallSpeed + r2 * span) % span;
            float y = progress - size;
            float x = r3 * width + MathF.Sin(time * (0.6f + r1) + i) * 40f * scale;
            float rotation = time * (30f + r2 * 60f) + i * 47f;

            float t = Math.Clamp(y / height, 0f, 1f);
            Rgba tint = Helper.Mix(ForkTop, ForkBottom, t) with { A = (byte)(180 * appear) };

            DrawTexturePro(fork, source,
                new Rect(x, y, fork.Width * drawScale, fork.Height * drawScale),
                new Vector2(fork.Width * drawScale / 2, fork.Height * drawScale / 2), rotation, tint);
        }
    }

    private static float Frac(float value) => value - MathF.Floor(value);
}
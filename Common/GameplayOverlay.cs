using Raylib_cs;

namespace DmitryAndDemid.Common;

public abstract class GameplayOverlay(GameBox box, float animationLength, float length)
{
    protected float State => 
        Raymath.Clamp((Box.GetTime() - TimeAppear) / AnimationLength, 0, 1) * Raymath.Clamp((TimeAppear + Length - Box.GetTime()) / AnimationLength, 0, 1);

    private float Length = length;
    private float AnimationLength = animationLength;
    public float TimeAppear = box.GetTime();
    protected GameBox Box = box;
    
    protected virtual void Draw()
    {
        
    }

    protected virtual void Unload()
    {
        
    }

    public virtual void Update()
    {
        
    }

    public void DrawOverlay()
    {
        if (TimeAppear + Length < Box.GetTime())
        {
            Box.RemoveOverlay(this);
            Unload();
            return;
        }
        Draw();
    }
}
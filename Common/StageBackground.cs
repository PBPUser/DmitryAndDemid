using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Common;

public abstract class StageBackground
{
    public StageBackground()
    {
        
    }

    protected virtual void Render(TargetHandle texture, int tick, float delta)
    {
        
    }

    protected virtual void Update(int tick, float delta)
    {
        
    }

    protected virtual void Unload()
    {
        
    }
    
    public void Draw(TargetHandle texture, int tick, float delta)
    {
        Update(tick, delta);
        BeginTextureMode(texture);
        Render(texture, tick, delta);
        EndTextureMode();
    }
}
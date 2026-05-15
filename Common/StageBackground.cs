using Raylib_cs;

namespace DmitryAndDemid.Common;

public abstract class StageBackground
{
    public StageBackground()
    {
        
    }

    protected virtual void Render(RenderTexture2D texture, int tick, float delta)
    {
        
    }
    
    public void Draw(RenderTexture2D texture, int tick, float delta)
    {
        Raylib.BeginTextureMode(texture);
        Render(texture, tick, delta);
        Raylib.EndTextureMode();
    }
}
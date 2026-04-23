using Raylib_cs;

namespace DmitryAndDemid.Common;

public abstract class StageBackground
{
    public StageBackground()
    {
        
    }

    protected virtual void Render(RenderTexture2D texture, int tick)
    {
        
    }
    
    public void Draw(RenderTexture2D texture, int tick)
    {
        Raylib.BeginTextureMode(texture);
        Render(texture, tick);
        Raylib.EndTextureMode();
    }
}
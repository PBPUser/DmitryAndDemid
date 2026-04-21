namespace DmitryAndDemid.Common;

public abstract class Screen : IDisposable
{
    public virtual void Render()
    {

    }

    public virtual void PreRender(double delta)
    {

    }

    public virtual void Unload()
    {

    }

    public void Dispose()
    {
        Unload();
    }
}

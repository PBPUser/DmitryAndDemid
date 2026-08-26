using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Common;

public abstract class StageBackground
{
    public StageBackground()
    {
        
    }

    protected virtual void Render(RenderedTexture texture, int tick, float delta)
    {
        
    }

    protected virtual void Update(int tick, float delta)
    {

    }

    /// <summary>
    /// Reacts to a named gameplay event (a chapter starting, a spell card, a script-fired cue…). The base
    /// ignores every event; a concrete background overrides this to change state — swap layers, start a
    /// transition, flash, and so on. <paramref name="value"/> is an optional scalar payload (0 when unused).
    /// Raised from <see cref="GameBox.FireBackgroundEvent"/>, on the simulation tick.
    /// </summary>
    public virtual void OnEvent(string name, float value = 0f)
    {

    }

    protected virtual void Unload()
    {
        
    }
    
    public void Draw(RenderedTexture texture, int tick, float delta)
    {
        Update(tick, delta);
        BeginTextureMode(texture);
        Render(texture, tick, delta);
        EndTextureMode();
    }

    /// <summary>Frees any GPU resources this background owns. Public entry point to <see cref="Unload"/>
    /// so tooling (the DEBUG background tester) can discard a background when switching to another.</summary>
    public void Dispose() => Unload();
}
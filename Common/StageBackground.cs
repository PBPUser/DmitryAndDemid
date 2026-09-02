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

    /// <summary>
    /// Draws <paramref name="destination"/> as one quad through whatever shader mode is active, for a
    /// background that is entirely procedural — the fragment shader paints from <c>fragTexCoord</c> alone.
    /// The quad is fed a plain, static texture with a positive full-size source, so the coordinates the
    /// shader sees run 0 at the top to 1 at the bottom on every backend. The old way was an empty render
    /// target drawn through the (0, H, W, -H) flip form, which hands the shader coordinates in [1, 2] and
    /// leaves the picture to the driver's wrap arithmetic: on Raylib that put the whole view below the
    /// horizon (an upside-down, sky-less field), while Vulkan happened to fold it back.
    /// </summary>
    protected static void DrawProceduralQuad(Rect destination) =>
        DrawProceduralQuad(destination, Runtime.CurrentRuntime.Textures["star.png"]);   // content ignored by the shaders

    /// <summary>The same quad, but fed <paramref name="data"/> as <c>texture0</c> — for a procedural shader that
    /// reads a data texture (the stage-1 flyover's map of Drogichin). Still a positive full-size source, so
    /// <c>fragTexCoord</c> keeps the same 0-at-the-top convention.</summary>
    protected static void DrawProceduralQuad(Rect destination, BasicTexture data)
    {
        DrawTexturePro(data, new Rect(0, 0, data.Width, data.Height), destination, System.Numerics.Vector2.Zero, 0,
            Rgba.White);
    }
}
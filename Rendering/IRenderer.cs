using DmitryAndDemid.Data.Archive;
using System.Numerics;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// Everything Likhanov32D — the Nikitos Engine's graphics half — needs from a graphics backend, and the
/// contract a backend signs to provide it. This is the reason the engine's name and the backend's are
/// separate things. Contains no backend type —
/// resources are handles, geometry is Rect/Rgba/Vector2 — so a second implementation (Silk.NET/OpenGL, later
/// Vulkan) can be dropped in without touching gameplay code.
///
/// Ordering contract a backend may rely on:
///   BeginFrame … EndFrame wraps every frame.
///   Target and shader scopes nest and must be balanced.
/// </summary>
public interface IRenderer : IDisposable
{
    // ---- textures -------------------------------------------------------------------------

    TextureHandle LoadTexture(string path);

    /// <summary>
    /// Uploads raw pixels to a GPU texture — the entry point for images the game builds or edits on the CPU
    /// (see <see cref="CpuImage"/>) instead of reading from a file. <paramref name="rgba"/> is tightly packed
    /// RGBA, top row first: 4 bytes per pixel, at least <c>width * height * 4</c> long. The bytes are consumed
    /// during the call, so the caller may mutate or drop the array afterwards.
    ///
    /// The result is backend-owned exactly like a <see cref="LoadTexture"/> one — free it with
    /// <see cref="UnloadTexture"/>. Returns <see cref="TextureHandle.None"/> on degenerate arguments, or on a
    /// backend with no upload path (the Switch deko3d backend, whose LoadTexture is a stub too).
    /// </summary>
    TextureHandle LoadTextureFromPixels(byte[] rgba, int width, int height);

    /// <summary>The shared precondition for <see cref="LoadTextureFromPixels"/>, so every backend rejects the
    /// same inputs rather than each inventing its own guard (and reading past the array on a short one).</summary>
    static bool AreLoadablePixels(byte[] rgba, int width, int height) =>
        width > 0 && height > 0 && (long)width * height * 4 <= rgba.Length;

    void UnloadTexture(TextureHandle texture);
    bool IsValid(TextureHandle texture);
    Vector2 GetTextureSize(TextureHandle texture);
    void SetTextureFilter(TextureHandle texture, FilterMode filter);

    // ---- render targets -------------------------------------------------------------------

    TargetHandle CreateTarget(int width, int height);
    void DestroyTarget(TargetHandle target);
    bool IsValid(TargetHandle target);
    TextureHandle GetTargetTexture(TargetHandle target);

    /// <summary>
    /// Targets NEST: EndTarget re-binds the enclosing target rather than the window. Neither Raylib nor raw
    /// GL does this natively (both just unbind to framebuffer 0), but the frame itself is composited into a
    /// target, so an inner EndTarget must not drop the frame's own target.
    /// </summary>
    void BeginTarget(TargetHandle target);

    void EndTarget();

    /// <summary>
    /// Depth that EndTarget must not pop below. Runtime sets this to 1 while compositing the frame, so a
    /// stray unbalanced EndTarget in drawing code cannot unbind the frame's target and silently redirect the
    /// rest of the frame to the window. (Helper.DrawSpellSubtitle shipped with exactly such a stray call.)
    /// </summary>
    int TargetFloor { get; set; }

    /// <summary>Unwinds the target stack to nothing. Called at the end of a frame so a leaked BeginTarget
    /// cannot corrupt the next one.</summary>
    void ResetTargets();

    // ---- shaders --------------------------------------------------------------------------

    /// <summary>Null vertex path/source means "use the shared base vertex shader".</summary>
    ShaderHandle LoadShader(string? vertexPath, string fragmentPath);

    ShaderHandle LoadShaderFromSource(string? vertexSource, string fragmentSource);
    void UnloadShader(ShaderHandle shader);
    bool IsValid(ShaderHandle shader);
    void BeginShader(ShaderHandle shader);
    void EndShader();

    /// <summary>
    /// Resolves a uniform name to an opaque location. Backends cache this; the game caches it too (it
    /// stores locations in RuntimeObject headers), so both paths must be cheap.
    /// </summary>
    int GetUniformLocation(ShaderHandle shader, string name);

    void SetUniform<T>(ShaderHandle shader, int location, T value, UniformType type) where T : unmanaged;
    void SetUniformTexture(ShaderHandle shader, int location, TextureHandle texture);

    /// <summary>Array uniform (e.g. a float[2] passed for a vec2).</summary>
    void SetUniformArray(ShaderHandle shader, int location, float[] values, UniformType type);

    /// <summary>Convenience: resolve-and-set by name (location cached internally).</summary>
    void SetUniform<T>(ShaderHandle shader, string name, T value, UniformType type) where T : unmanaged;

    /// <summary>Uniform names declared by a shader — used by the debug shader previewer.</summary>
    IReadOnlyList<string> GetUniformNames(ShaderHandle shader);

    // ---- fonts and text -------------------------------------------------------------------

    FontHandle LoadFont(string path, int size);
    void UnloadFont(FontHandle font);
    FontHandle GetDefaultFont();
    Vector2 MeasureText(FontHandle font, string text, float fontSize, float spacing);
    void DrawText(FontHandle font, string text, Vector2 position, float fontSize, float spacing, Rgba tint);

    void DrawTextPro(FontHandle font, string text, Vector2 position, Vector2 origin, float rotation,
        float fontSize, float spacing, Rgba tint);

    // ---- drawing --------------------------------------------------------------------------

    void Clear(Rgba color);
    void DrawTexture(TextureHandle texture, Vector2 position, Rgba tint);
    void DrawTexture(TextureHandle texture, Vector2 position, float rotation, float scale, Rgba tint);
    void DrawTexture(TextureHandle texture, Rect source, Rect destination, Vector2 origin, float rotation, Rgba tint);
    void DrawNinePatch(TextureHandle texture, NinePatch patch, Rect destination, Vector2 origin, float rotation, Rgba tint);
    void DrawRect(Rect rect, Rgba color);
    void DrawRect(Rect rect, Vector2 origin, float rotation, Rgba color);
    void DrawLine(Vector2 from, Vector2 to, Rgba color);

    void BeginBlend(BlendMode mode);
    void EndBlend();

    // ---- diagnostics ----------------------------------------------------------------------

    /// <summary>
    /// Best-effort GPU description for the benchmark / system-info panel: adapter name, graphics API + version,
    /// dedicated VRAM if the backend can tell, and any notable extensions. Returns null when the backend cannot
    /// answer — the system-info collector then falls back to OS-level probing. A default implementation so a
    /// backend that does not implement it still satisfies the interface.
    /// </summary>
    GpuInfo? QueryGpuInfo() => null;

    // ---- frame ----------------------------------------------------------------------------

    void BeginFrame();
    void EndFrame();
}

/// <summary>
/// using-block wrappers over the Begin*/End* pairs — extension methods, so they allocate nothing and every
/// backend gets them for free.
/// </summary>
public static class RendererScopes
{
    public static TargetScope Target(this IRenderer renderer, TargetHandle target) => new(renderer, target);
    public static ShaderScope Shader(this IRenderer renderer, ShaderHandle shader) => new(renderer, shader);
    public static BlendScope Blend(this IRenderer renderer, BlendMode mode) => new(renderer, mode);

    public readonly struct TargetScope : IDisposable
    {
        private readonly IRenderer Renderer;
        public TargetScope(IRenderer renderer, TargetHandle target)
        {
            Renderer = renderer;
            renderer.BeginTarget(target);
        }
        public void Dispose() => Renderer.EndTarget();
    }

    public readonly struct ShaderScope : IDisposable
    {
        private readonly IRenderer Renderer;
        public ShaderScope(IRenderer renderer, ShaderHandle shader)
        {
            Renderer = renderer;
            renderer.BeginShader(shader);
        }
        public void Dispose() => Renderer.EndShader();
    }

    public readonly struct BlendScope : IDisposable
    {
        private readonly IRenderer Renderer;
        public BlendScope(IRenderer renderer, BlendMode mode)
        {
            Renderer = renderer;
            renderer.BeginBlend(mode);
        }
        public void Dispose() => Renderer.EndBlend();
    }
}

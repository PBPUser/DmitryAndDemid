namespace DmitryAndDemid.Rendering;

/// <summary>
/// The single place the backend is chosen. Everything else in the game talks to
/// <see cref="Renderer"/>/<see cref="Platform"/>/<see cref="Input"/>/<see cref="Audio"/> and never
/// names a concrete backend, so swapping Raylib for another implementation is a one-line change here.
///
/// This is a service locator rather than constructor injection on purpose: the codebase already reaches
/// for the global <c>Runtime.CurrentRuntime</c> from everywhere, and threading a renderer through every
/// Screen/RuntimeObject would be a much larger change for no extra decoupling.
/// </summary>
public static class Engine
{
    private static IBackend? Active;

    public static IRenderer Renderer => Require();
    public static IPlatform Platform => Require();
    public static IInput Input => Require();
    public static IAudio Audio => Require();

    public static IBackend Backend => Require();

    public static string BackendName => Require().Name;
    public static bool IsInitialized => Active != null;

    /// <summary>The renderers that exist. Lives in RendererRegistry so the configurator can share it.</summary>
    public static (string Key, string Name)[] Available => RendererRegistry.Available;

    /// <summary>Adding a renderer means implementing IBackend, adding a line here, and one to Available.</summary>
    public static IBackend Create(string name) => name.Trim().ToLowerInvariant() switch
    {
#if SWITCH
        // Nintendo Switch homebrew (mono-nx runtime). SDL2 is what mono-nx actually exports, so it is the
        // working-video default; deko3d stays selectable for a future native fork (it draws nothing on stock
        // mono-nx, whose dl_shim has no deko3d symbols). The desktop backends rely on dynamic native loading,
        // which mono-nx's static-only P/Invoke cannot do. See docs/switch-port.md.
        // The shader-capable GLES path (needs a mono-nx interpreter built with OpenGL). Opt in with renderer "gl"
        // until it is proven on-device, then it can become the default. SdlBackend (2D, no shaders) is the safe
        // default that works on the stock interpreter.
        "gl" or "gles" or "opengl" => new Switch.SdlGlBackend(),
        "deko3d" or "deko" => new Switch.Deko3dBackend(),
        _ => new Switch.SdlBackend(),
#elif ANDROID
        // Android has exactly one backend: GL ES through Silk, on the context the Activity owns. Raylib ships
        // no Android native and the Vulkan backend is bound to a desktop surface, so neither can be built.
        _ => new SilkGLBackend(),
#else
        "silk" or "silk.net" or "opengl" or "gl" => new SilkGLBackend(),
        "vulkan" or "vk" => new VulkanBackend(),
        // On linux-arm64 (Tegra/L4T) there is no native libraylib.so, so raylib — and the empty/default key —
        // fall back to Silk instead of faulting with DllNotFound. See RendererRegistry.RaylibSupported.
        "raylib" or "" => RendererRegistry.RaylibSupported ? new RaylibBackend() : new SilkGLBackend(),
        _ => throw new ArgumentException(
            $"Unknown renderer '{name}'. Known: {string.Join(", ", Available.Select(r => r.Key))}."),
#endif
    };

    public static void Use(IBackend backend)
    {
        if (Active != null)
            throw new InvalidOperationException(
                $"Backend already set to '{Active.Name}'. Call Engine.Shutdown() first.");
        Active = backend;
    }

    public static void Shutdown()
    {
        Active?.Dispose();
        Active = null;
    }

    private static IBackend Require() =>
        Active ?? throw new InvalidOperationException(
            "No backend. Call Engine.Use(new RaylibBackend()) before touching the renderer.");
}

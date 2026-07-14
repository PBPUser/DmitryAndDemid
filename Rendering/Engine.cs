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
        "silk" or "silk.net" or "opengl" or "gl" => new SilkGLBackend(),
        "vulkan" or "vk" => new VulkanBackend(),
        "raylib" or "" => new RaylibBackend(),
        _ => throw new ArgumentException(
            $"Unknown renderer '{name}'. Known: {string.Join(", ", Available.Select(r => r.Key))}."),
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

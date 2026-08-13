namespace DmitryAndDemid.Rendering;

/// <summary>
/// The Nikitos Engine — the thing this class is the front door of, and the name the whole rendering /
/// platform / input / audio seam goes by. It answers to two names, used interchangeably and with no
/// difference in meaning: the <b>Nikitos Engine</b> and the <b>Lihanov Engine</b>, both after Никита
/// Лиханов, who is stage 1's boss and whose nickname the engine took. See <see cref="Name"/> and
/// <see cref="AlternateName"/> — anything that puts a name on screen or in a log reads it from there
/// rather than spelling it out, so the two never drift apart.
///
/// Its parts have names of their own, and they are the names to use when you mean the part rather than the
/// whole: <b>Likhanov32D</b> is the graphics (<see cref="GraphicsName"/> — the "32D" is 3D and 2D),
/// <b>Demidonic</b> is the sound (<see cref="AudioName"/>), and <b>Pizzics</b> is the physics
/// (<see cref="PhysicsName"/>, pizza + physics). <see cref="Renderer"/> and <see cref="Audio"/> below are the
/// first two; the third has no property here because it is not a backend service — it is the collision pass in
/// <c>GameBox</c>.
///
/// Mechanically this is the single place the backend is chosen. Everything else in the game talks to
/// <see cref="Renderer"/>/<see cref="Platform"/>/<see cref="Input"/>/<see cref="Audio"/> and never
/// names a concrete backend, so swapping Raylib for another implementation is a one-line change here.
/// The engine is the seam, not the backend behind it: Raylib, Silk/GL, Vulkan, Metal and the Switch
/// backends are all things the Nikitos Engine runs ON, which is why <see cref="Name"/> and
/// <see cref="BackendName"/> are two different strings and both get printed.
///
/// This is a service locator rather than constructor injection on purpose: the codebase already reaches
/// for the global <c>Runtime.CurrentRuntime</c> from everywhere, and threading a renderer through every
/// Screen/RuntimeObject would be a much larger change for no extra decoupling.
/// </summary>
public static class Engine
{
    /// <summary>
    /// The engine's name. NOT the backend's — that is <see cref="BackendName"/>, and the two are printed
    /// side by side (window title, splash, debug overlay) precisely so nobody reads "Vulkan" as the name of
    /// the engine.
    /// </summary>
    public const string Name = "Nikitos Engine";

    /// <summary>
    /// The engine's other name, equally correct and equally used — the Lihanov Engine. Same engine, same
    /// Никита Лиханов; the project has simply never settled on one and does not intend to. Callers pick
    /// whichever fits the surface (the splash credits both, the debug overlay uses this one), but they pick
    /// from here — a hand-typed "Lihanov engine" somewhere is how the two names start disagreeing about
    /// capitalisation and then about spelling.
    /// </summary>
    public const string AlternateName = "Lihanov Engine";

    /// <summary>
    /// The graphics half of the engine, named in its own right: <b>Likhanov32D</b> — everything reached through
    /// <see cref="Renderer"/>, i.e. <see cref="Gfx"/>, <see cref="IRenderer"/> and whichever backend is behind
    /// them. The "32D" is 3D and 2D. Note the spelling: Likhanov here, Lihanov in <see cref="AlternateName"/> —
    /// they are separate names that grew separately, and neither is a typo of the other.
    /// </summary>
    public const string GraphicsName = "Likhanov32D";

    /// <summary>
    /// The sound engine: <b>Demidonic</b> — <see cref="Audio"/> / <c>IAudio</c>, its backend implementations,
    /// and the <c>Sounds</c> / music side of <c>Runtime</c> that drives them.
    /// </summary>
    public const string AudioName = "Demidonic";

    /// <summary>
    /// The physics engine: <b>Pizzics</b> (pizza + physics) — the collision and movement pass, which lives in
    /// <c>GameBox</c>'s per-tick sweep and <c>Helper.IsCollied</c> / <c>MathUtil</c> rather than in a subsystem
    /// folder of its own. Naming it does not move it; it gives the danmaku's one genuinely physical job a name
    /// to be discussed by.
    /// </summary>
    public const string PhysicsName = "Pizzics";

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
#if METAL
        // Apple (macOS / iOS): one native backend, Metal. Host-constructed and host-driven — the view
        // controller calls MetalBackend.StartMetal(layer, w, h, audio) and pumps frames from a CADisplayLink,
        // exactly as the Android host drives SilkGLBackend. See docs/metal-backend.md.
        _ => new Metal.MetalBackend(),
#elif SWITCH
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

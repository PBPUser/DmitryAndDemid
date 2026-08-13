using System.Runtime.InteropServices;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// The list of renderers the Nikitos Engine can run on. Deliberately free of any dependency on the backends
/// themselves, so the
/// standalone configurator can link this file without dragging in Raylib/Silk/Vulkan — a config tool has no
/// business loading a graphics stack.
///
/// <see cref="Engine.Create"/> maps these keys onto real backends; the launcher and the in-game settings row
/// both build their pickers from here, so they cannot drift apart.
/// </summary>
public static class RendererRegistry
{
    /// <summary>
    /// The Raylib-cs NuGet package ships a native <c>libraylib.so</c> for linux-x64 only — there is no
    /// linux-arm64 build in the package. On ARMv8 targets (Tegra X1 / L4T, Jetson, Shield) the Raylib backend
    /// would fault with DllNotFound the moment it is touched, so it is hidden there and Silk.NET/OpenGL — which
    /// binds the system libGL and needs no bundled native — becomes the default. Vulkan stays offered because
    /// L4T's mesa exposes it.
    /// </summary>
    public static bool RaylibSupported =>
        !(RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
          RuntimeInformation.ProcessArchitecture == Architecture.Arm64);

    /// <summary>Key is what goes into config.json / --renderer=; Name matches IBackend.Name.</summary>
#if METAL
    // Apple builds ship exactly one backend (Metal); no picker choice to offer. See docs/metal-backend.md.
    public static (string Key, string Name)[] Available => [("metal", "Metal")];
#else
    public static (string Key, string Name)[] Available => RaylibSupported
        ?
        [
            ("raylib", "Raylib"),
            ("silk", "Silk.NET/OpenGL"),
            ("vulkan", "Vulkan"),
        ]
        :
        [
            ("silk", "Silk.NET/OpenGL"),
            ("vulkan", "Vulkan"),
        ];
#endif

    public static string NameOf(string key) =>
        Available.FirstOrDefault(r => r.Key == key).Name ?? key;

    public static int IndexOf(string key) => Array.FindIndex(Available, r => r.Key == key);
}

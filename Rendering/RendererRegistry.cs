namespace DmitryAndDemid.Rendering;

/// <summary>
/// The list of renderers that exist. Deliberately free of any dependency on the backends themselves, so the
/// standalone configurator can link this file without dragging in Raylib/Silk/Vulkan — a config tool has no
/// business loading a graphics stack.
///
/// <see cref="Engine.Create"/> maps these keys onto real backends; the launcher and the in-game settings row
/// both build their pickers from here, so they cannot drift apart.
/// </summary>
public static class RendererRegistry
{
    /// <summary>Key is what goes into config.json / --renderer=; Name matches IBackend.Name.</summary>
    public static readonly (string Key, string Name)[] Available =
    [
        ("raylib", "Raylib"),
        ("silk", "Silk.NET/OpenGL"),
        ("vulkan", "Vulkan"),
    ];

    public static string NameOf(string key) =>
        Available.FirstOrDefault(r => r.Key == key).Name ?? key;

    public static int IndexOf(string key) => Array.FindIndex(Available, r => r.Key == key);
}

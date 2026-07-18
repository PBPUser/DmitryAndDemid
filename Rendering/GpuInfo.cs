namespace DmitryAndDemid.Rendering;

/// <summary>
/// What a graphics backend can report about the adapter it is running on, for the benchmark / system-info
/// panel. Everything is best-effort: a backend fills what its API exposes and leaves the rest at its default
/// (empty name, 0 VRAM, no extensions). <see cref="IRenderer.QueryGpuInfo"/> returns null when nothing at all
/// is known.
/// </summary>
public readonly record struct GpuInfo(
    string Name,
    string Api,
    long VramBytes,
    IReadOnlyList<string> Extensions);

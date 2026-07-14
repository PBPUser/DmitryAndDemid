using System.Numerics;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// Opaque references to backend-owned GPU/audio resources. The backend keeps the real object (a Raylib
/// Texture2D, a GL texture name, …) in a private table and hands out an id, so game code never names a
/// backend type. Id 0 always means "nothing", which preserves the `if (thing.Id != 0)` checks the game
/// already makes against Raylib's own handles.
///
/// The convenience members below (Width, Height, Texture) forward to the active renderer. They exist so the
/// migration off Raylib does not have to rewrite every `texture.Width` / `renderTexture.Texture` expression.
/// </summary>
public readonly record struct TextureHandle(int Id)
{
    public static readonly TextureHandle None = default;
    public bool IsValid => Id != 0;

    public Vector2 Size => Engine.Renderer.GetTextureSize(this);
    public int Width => (int)Engine.Renderer.GetTextureSize(this).X;
    public int Height => (int)Engine.Renderer.GetTextureSize(this).Y;
}

public readonly record struct TargetHandle(int Id)
{
    public static readonly TargetHandle None = default;
    public bool IsValid => Id != 0;

    /// <summary>The target's colour attachment, so it can be drawn like any other texture.</summary>
    public TextureHandle Texture => Engine.Renderer.GetTargetTexture(this);
}

public readonly record struct ShaderHandle(int Id)
{
    public static readonly ShaderHandle None = default;
    public bool IsValid => Id != 0;
}

public readonly record struct FontHandle(int Id)
{
    public static readonly FontHandle None = default;
    public bool IsValid => Id != 0;
}

public readonly record struct SoundHandle(int Id)
{
    public static readonly SoundHandle None = default;
    public bool IsValid => Id != 0;
}

using System.Numerics;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// Backend-independent rectangle. Deliberately a MUTABLE struct with public fields, mirroring the shape of
/// Raylib's Rectangle, so that existing game code keeps working: field assignment (rc.Height *= -1) and
/// with-expressions (rc with { X = 5 }) both compile unchanged. C# allows `with` on plain structs.
/// </summary>
public struct Rect(float x, float y, float width, float height)
{
    public float X = x;
    public float Y = y;
    public float Width = width;
    public float Height = height;

    public Rect(Vector2 position, Vector2 size) : this(position.X, position.Y, size.X, size.Y)
    {
    }

    public Rect(Vector2 position, float width, float height) : this(position.X, position.Y, width, height)
    {
    }

    public Rect(float x, float y, Vector2 size) : this(x, y, size.X, size.Y)
    {
    }

    public Vector2 Position
    {
        get => new(X, Y);
        set { X = value.X; Y = value.Y; }
    }

    public Vector2 Size
    {
        get => new(Width, Height);
        set { Width = value.X; Height = value.Y; }
    }

    public Vector2 Center => new(X + Width / 2, Y + Height / 2);

    public static Rect FromSize(Vector2 size) => new(0, 0, size.X, size.Y);

    /// <summary>Source rect for sampling a render target, which is stored bottom-up (negative height).</summary>
    public static Rect Flipped(Vector2 size) => new(0, size.Y, size.X, -size.Y);

    public static Rect operator *(Rect rc, float f)
    {
        return new Rect(rc.X * f, rc.Y * f, rc.Width * f, rc.Height * f);
    }

    public static Rect operator *(float f, Rect rc)
    {
        return new Rect(rc.X * f, rc.Y * f, rc.Width * f, rc.Height * f);
    }

    public static Rect operator -(Rect rc, Rect rc2)
    {
        return new Rect(rc.X - rc2.X, rc.Y - rc2.Y, rc.Width - rc2.X, rc.Height - rc2.Y);
    }

    public static Rect operator +(Rect rc, Rect rc2)
    {
        return new Rect(rc.X + rc2.X, rc.Y + rc2.Y, rc.Width, rc.Height);
    }

    public override string ToString() => $"({X}, {Y}, {Width}, {Height})";
}

/// <summary>
/// Backend-independent 8-bit RGBA colour. Mutable struct with public fields for the same reason as
/// <see cref="Rect"/> — `Rgba.White with { A = 0 }` and direct field writes both keep working.
/// Named colours match Raylib's palette so the game's visuals are unchanged.
/// </summary>
public struct Rgba(byte r, byte g, byte b, byte a = 255)
{
    public byte R = r;
    public byte G = g;
    public byte B = b;
    public byte A = a;

    public static readonly Rgba White = new(255, 255, 255);
    public static readonly Rgba Black = new(0, 0, 0);
    public static readonly Rgba Blank = new(0, 0, 0, 0);
    public static readonly Rgba Red = new(230, 41, 55);
    public static readonly Rgba Green = new(0, 228, 48);
    public static readonly Rgba Blue = new(0, 121, 241);
    public static readonly Rgba Yellow = new(253, 249, 0);
    public static readonly Rgba Magenta = new(255, 0, 255);
    public static readonly Rgba Purple = new(200, 122, 255);
    public static readonly Rgba Orange = new(255, 161, 0);
    public static readonly Rgba Gray = new(130, 130, 130);
    public static readonly Rgba DarkGray = new(80, 80, 80);
    public static readonly Rgba LightGray = new(200, 200, 200);
    public static readonly Rgba SkyBlue = new(102, 191, 255);
    public static readonly Rgba Gold = new(255, 203, 0);
    public static readonly Rgba Pink = new(255, 109, 194);
    public static readonly Rgba Lime = new(0, 158, 47);
    public static readonly Rgba Violet = new(135, 60, 190);
    public static readonly Rgba Brown = new(127, 106, 79);
    public static readonly Rgba Beige = new(211, 176, 131);
    public static readonly Rgba Maroon = new(190, 33, 55);
    public static readonly Rgba DarkBlue = new(0, 82, 172);
    public static readonly Rgba DarkGreen = new(0, 117, 44);
    public static readonly Rgba DarkPurple = new(112, 31, 126);
    public static readonly Rgba DarkBrown = new(76, 63, 47);
    public static readonly Rgba RayWhite = new(245, 245, 245);
    public static readonly Rgba TransparentBlack = new(0, 0, 0, 0);
    public static readonly Rgba TransparentWhite = new(255, 255, 255, 0);
    public static readonly Rgba DebugSemiTransparentGray = new(64, 64, 64, 128);

    /// <summary>0xRRGGBB — matches the ints the screen effects pass around.</summary>
    public static Rgba FromHex(int rgb, byte alpha = 255) =>
        new((byte)(rgb >> 16 & 0xFF), (byte)(rgb >> 8 & 0xFF), (byte)(rgb & 0xFF), alpha);

    public int ToHex() => R << 16 | G << 8 | B;

    /// <summary>Normalised RGBA — what a shader uniform expects.</summary>
    public Vector4 ToVector4() => new(R / 255f, G / 255f, B / 255f, A / 255f);

    public Vector3 ToVector3() => new(R / 255f, G / 255f, B / 255f);

    public static Rgba Mix(Rgba a, Rgba b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t),
        (byte)(a.A + (b.A - a.A) * t));
}

/// <summary>Nine-slice description.</summary>
public struct NinePatch(Rect source, int left, int top, int right, int bottom, NinePatchLayout layout)
{
    public Rect Source = source;
    public int Left = left;
    public int Top = top;
    public int Right = right;
    public int Bottom = bottom;
    public NinePatchLayout Layout = layout;
}

public enum NinePatchLayout
{
    NinePatch = 0,
    ThreePatchVertical = 1,
    ThreePatchHorizontal = 2,
}

/// <summary>Shader uniform types. Values are the engine's own; backends map them to their API.</summary>
public enum UniformType
{
    Float = 0,
    Vec2 = 1,
    Vec3 = 2,
    Vec4 = 3,
    Int = 4,
    IVec2 = 5,
    IVec3 = 6,
    IVec4 = 7,
    Sampler2D = 8,
}

public enum FilterMode
{
    Point = 0,
    Bilinear = 1,
    Trilinear = 2,
}

public enum BlendMode
{
    Alpha = 0,
    Additive = 1,
    Multiplied = 2,
    AddColors = 3,
    SubtractColors = 4,
    AlphaPremultiply = 5,

    /// <summary>Straight copy of RGB, ignoring the source alpha. Used to blit the frame to the window.</summary>
    CopyRgb = 100,
}

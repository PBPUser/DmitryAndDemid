namespace NEngineFormat.StaticIllustration.Data;

/// <summary>
/// Where one decoded pixel got its colour, which is what a mask decides.
/// </summary>
/// <remarks>
/// The format spends its bytes on the pixels a tile actually stores, so seeing which pixels
/// those are is seeing where the file went. Every other origin here is a pixel that cost
/// nothing beyond the mask bit naming it.
/// </remarks>
public enum PixelOrigin : byte
{
    /// <summary>Nothing was drawn: the tile leaves the image, or no block covers it.</summary>
    None = 0,

    /// <summary>A colour entry stored by the tile - the pixels a file pays for.</summary>
    Entries = 1,

    /// <summary>A colour named directly by the tile's own mask palette.</summary>
    Palette = 2,

    /// <summary>The tile's out-of-mask colour, or the minimum its profile sets.</summary>
    OutOfMask = 3,

    /// <summary>Painted by the image-wide mask, before any tile saw it.</summary>
    ImageMask = 4,

    /// <summary>Copied from the tile a difference points at, and left alone.</summary>
    Inherited = 5,

    /// <summary>Painted by a shape the tile carries, which costs nothing per pixel.</summary>
    Brush = 6,
}

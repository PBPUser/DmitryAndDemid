namespace NEngineFormat.StaticIllustration.Data;

/// <summary>
/// How an illustration says which pixels carry a colour, held in bits 0-1 of
/// <see cref="Blocks.IllustrationBlock.Flags"/>.
/// </summary>
public enum MaskMode : uint
{
    /// <summary>No mask; every pixel carries a colour.</summary>
    Disabled = 0,

    /// <summary>One bit per pixel saying whether that pixel is inside the mask.</summary>
    PerPixel = 1,

    /// <summary>Run lengths alternating between covered and uncovered stretches.</summary>
    Linear = 2,

    /// <summary>
    /// An index per pixel naming a colour in the tile's <see cref="Blocks.MaskPalleteBlock"/>,
    /// or an escape value meaning the pixel falls through to the colour entries.
    /// </summary>
    Palette = 3,
}

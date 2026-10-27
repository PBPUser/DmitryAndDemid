using NEngineFormat.Core.Data;
using NEngineFormat.Core.Utils;

namespace NEngineFormat.StaticIllustration.Data;

/// <summary>What shape a <see cref="Brush"/> paints.</summary>
public enum BrushKind : uint
{
    /// <summary>An axis-aligned box of one colour.</summary>
    Rectangle = 0,

    /// <summary>A stroke of one colour between two points, of a given width.</summary>
    Line = 1,

    /// <summary>A filled triangle of one colour.</summary>
    Triangle = 2,

    /// <summary>A box whose colour moves by a fixed step along each axis.</summary>
    Gradient = 3,
}

/// <summary>
/// A shape painted into a tile before its mask is read, so the pixels it covers cost the tile
/// nothing at all.
/// </summary>
/// <remarks>
/// <para>Payload layout, by kind:</para>
/// <code>
/// Kind                 VarULong
/// Rectangle:  X, Y, Width, Height, Color
/// Line:       X0, Y0, X1, Y1, Stroke, Color
/// Triangle:   X0, Y0, X1, Y1, X2, Y2, Color
/// Gradient:   X, Y, Width, Height, Color, StepX x4, StepY x4   (steps zigzagged)
/// </code>
/// <para>
/// A drawing is full of shapes that a pixel-by-pixel format has to spell out one pixel at a
/// time: the box behind a panel, the stroke of a rule, the wedge of a highlight. A brush says
/// the shape instead. Positions a brush covers are settled - the tile's mask and colour entries
/// advance over what is left, exactly as they do for the pixels the image-wide mask claimed -
/// so a shape costs its handful of numbers and nothing per pixel.
/// </para>
/// <para>
/// Coverage is decided in whole numbers throughout, never in fractions, so the encoder and the
/// decoder agree pixel for pixel: the encoder paints a candidate with this very code and keeps
/// it only where every pixel came out right.
/// </para>
/// <para>
/// Brushes paint in the order they are stored, so a later one covers an earlier one where they
/// overlap.
/// </para>
/// </remarks>
public sealed class Brush
{
    /// <summary>
    /// Largest coordinate or width a brush may name, which also keeps the whole-number distance
    /// test inside a 64-bit multiply.
    /// </summary>
    public const uint MaxCoordinate = 4095;

    /// <summary>Which shape this brush paints.</summary>
    public BrushKind Kind { get; set; }

    /// <summary>Left edge of a <see cref="BrushKind.Rectangle"/> or <see cref="BrushKind.Gradient"/>.</summary>
    public uint X { get; set; }

    /// <summary>Top edge of a <see cref="BrushKind.Rectangle"/> or <see cref="BrushKind.Gradient"/>.</summary>
    public uint Y { get; set; }

    /// <summary>Width of a <see cref="BrushKind.Rectangle"/> or <see cref="BrushKind.Gradient"/>.</summary>
    public uint Width { get; set; }

    /// <summary>Height of a <see cref="BrushKind.Rectangle"/> or <see cref="BrushKind.Gradient"/>.</summary>
    public uint Height { get; set; }

    /// <summary>First point of a <see cref="BrushKind.Line"/> or <see cref="BrushKind.Triangle"/>.</summary>
    public uint X0 { get; set; }

    /// <inheritdoc cref="X0"/>
    public uint Y0 { get; set; }

    /// <summary>Second point of a <see cref="BrushKind.Line"/> or <see cref="BrushKind.Triangle"/>.</summary>
    public uint X1 { get; set; }

    /// <inheritdoc cref="X1"/>
    public uint Y1 { get; set; }

    /// <summary>Third point of a <see cref="BrushKind.Triangle"/>.</summary>
    public uint X2 { get; set; }

    /// <inheritdoc cref="X2"/>
    public uint Y2 { get; set; }

    /// <summary>How wide a <see cref="BrushKind.Line"/> paints, in pixels.</summary>
    /// <remarks>
    /// A pixel belongs to the stroke when it lies within half this of the segment, ends
    /// included - so a stroke of one is the thin line through the two points, and a stroke of
    /// four is that line with two pixels either side of it and rounded ends.
    /// </remarks>
    public uint Stroke { get; set; }

    /// <summary>The colour painted, packed with red in the lowest byte.</summary>
    /// <remarks>For a gradient this is the colour at its own corner, before any step.</remarks>
    public uint Color { get; set; }

    /// <summary>How far each channel of a gradient moves per pixel across, in 256ths.</summary>
    /// <remarks>
    /// 256ths rather than whole numbers because a gradient rarely moves a whole level a pixel:
    /// a ramp that lightens by one every third pixel is a step of 85, and one that would not
    /// divide evenly is not a gradient this format can paint, so the encoder writes it out
    /// pixel by pixel instead.
    /// </remarks>
    public int[] StepX { get; set; } = [0, 0, 0, 0];

    /// <summary>How far each channel of a gradient moves per pixel down, in 256ths.</summary>
    public int[] StepY { get; set; } = [0, 0, 0, 0];

    /// <summary>Whether this brush paints a given position of the tile.</summary>
    public bool Covers(uint x, uint y) => Kind switch
    {
        BrushKind.Rectangle or BrushKind.Gradient =>
            x >= X && x - X < Width && y >= Y && y - Y < Height,
        BrushKind.Line => OnStroke(x, y),
        BrushKind.Triangle => InTriangle(x, y),
        _ => false,
    };

    /// <summary>The colour this brush paints at a position, packed with red in the lowest byte.</summary>
    public uint ColorAt(uint x, uint y)
    {
        if (Kind != BrushKind.Gradient)
        {
            return Color;
        }

        long across = (long)x - X;
        long down = (long)y - Y;
        uint painted = 0;

        for (int channel = 0; channel < Channels; channel++)
        {
            long moved = (long)((Color >> (channel * 8)) & 0xFF)
                + (((StepX[channel] * across) + (StepY[channel] * down)) >> FractionBits);

            // A file could name a step that walks a channel off the end; it is held at the edge
            // rather than wrapped, so a malformed brush paints a flat edge instead of stripes.
            uint held = moved < 0 ? 0 : moved > 255 ? 255 : (uint)moved;
            painted |= held << (channel * 8);
        }

        return painted;
    }

    /// <summary>Whether another brush paints the very same shape in the very same colour.</summary>
    /// <remarks>
    /// Two tiles can only share a block when everything about them matches, and the shapes they
    /// carry are part of that: without this a tile would link to one painting a different shape
    /// and take its pixels.
    /// </remarks>
    public bool Matches(Brush other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Kind == other.Kind
            && X == other.X && Y == other.Y && Width == other.Width && Height == other.Height
            && X0 == other.X0 && Y0 == other.Y0 && X1 == other.X1 && Y1 == other.Y1
            && X2 == other.X2 && Y2 == other.Y2
            && Stroke == other.Stroke && Color == other.Color
            && StepX.AsSpan().SequenceEqual(other.StepX)
            && StepY.AsSpan().SequenceEqual(other.StepY);
    }

    /// <summary>Checks the brush can be written.</summary>
    /// <exception cref="InvalidOperationException">
    /// The properties do not describe a shape that can be painted.
    /// </exception>
    public void Validate()
    {
        if (Kind is not (BrushKind.Rectangle or BrushKind.Line or BrushKind.Triangle or BrushKind.Gradient))
        {
            throw new InvalidOperationException($"Brush kind {(uint)Kind} is not one this format defines.");
        }

        // Fields the kind does not use are kept at zero, so one shape has one spelling and two
        // brushes painting the same thing compare equal.
        bool boxed = Kind is BrushKind.Rectangle or BrushKind.Gradient;
        if (boxed)
        {
            Unused("point", X0 | Y0 | X1 | Y1 | X2 | Y2);
            Unused("stroke", Stroke);

            if (Width == 0 || Height == 0)
            {
                throw new InvalidOperationException(
                    $"A {Kind} brush is {Width}x{Height} and would paint nothing.");
            }

            Bounded(X, nameof(X));
            Bounded(Y, nameof(Y));
            Bounded(Width, nameof(Width));
            Bounded(Height, nameof(Height));
        }
        else
        {
            Unused("box", X | Y | Width | Height);

            Bounded(X0, nameof(X0));
            Bounded(Y0, nameof(Y0));
            Bounded(X1, nameof(X1));
            Bounded(Y1, nameof(Y1));
        }

        if (Kind == BrushKind.Line)
        {
            Unused("third point", X2 | Y2);

            if (Stroke == 0)
            {
                throw new InvalidOperationException("A line brush of no width would paint nothing.");
            }

            Bounded(Stroke, nameof(Stroke));
        }
        else if (Kind == BrushKind.Triangle)
        {
            Unused("stroke", Stroke);
            Bounded(X2, nameof(X2));
            Bounded(Y2, nameof(Y2));

            // Three points on one line enclose nothing; a line brush is how to say that shape.
            if (Cross(X0, Y0, X1, Y1, X2, Y2) == 0)
            {
                throw new InvalidOperationException(
                    "A triangle brush whose three points lie on one line encloses nothing.");
            }
        }

        if (Kind == BrushKind.Gradient)
        {
            if (StepX.Length != Channels || StepY.Length != Channels)
            {
                throw new InvalidOperationException(
                    $"A gradient brush needs a step per channel for {Channels} channels, "
                    + $"not {StepX.Length} across and {StepY.Length} down.");
            }
        }
        else if (StepX.Any(step => step != 0) || StepY.Any(step => step != 0))
        {
            throw new InvalidOperationException(
                $"A {Kind} brush holds gradient steps, which it would not write.");
        }

        static void Bounded(uint value, string name)
        {
            if (value > MaxCoordinate)
            {
                throw new InvalidOperationException(
                    $"A brush names {name} {value}, over the {MaxCoordinate} limit.");
            }
        }

        void Unused(string what, uint bits)
        {
            if (bits != 0)
            {
                throw new InvalidOperationException(
                    $"A {Kind} brush sets a {what} it would not write.");
            }
        }
    }

    /// <summary>Reads one brush.</summary>
    public static Brush Read(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var brush = new Brush { Kind = (BrushKind)package.ReadVarULong() };

        switch (brush.Kind)
        {
            case BrushKind.Rectangle:
            case BrushKind.Gradient:
                brush.X = (uint)package.ReadVarULong();
                brush.Y = (uint)package.ReadVarULong();
                brush.Width = (uint)package.ReadVarULong();
                brush.Height = (uint)package.ReadVarULong();
                brush.Color = (uint)package.ReadVarULong();
                break;

            case BrushKind.Line:
                brush.X0 = (uint)package.ReadVarULong();
                brush.Y0 = (uint)package.ReadVarULong();
                brush.X1 = (uint)package.ReadVarULong();
                brush.Y1 = (uint)package.ReadVarULong();
                brush.Stroke = (uint)package.ReadVarULong();
                brush.Color = (uint)package.ReadVarULong();
                break;

            case BrushKind.Triangle:
                brush.X0 = (uint)package.ReadVarULong();
                brush.Y0 = (uint)package.ReadVarULong();
                brush.X1 = (uint)package.ReadVarULong();
                brush.Y1 = (uint)package.ReadVarULong();
                brush.X2 = (uint)package.ReadVarULong();
                brush.Y2 = (uint)package.ReadVarULong();
                brush.Color = (uint)package.ReadVarULong();
                break;

            default:
                throw new InvalidOperationException(
                    $"Brush kind {(uint)brush.Kind} is not one this format defines.");
        }

        if (brush.Kind == BrushKind.Gradient)
        {
            for (int channel = 0; channel < Channels; channel++)
            {
                brush.StepX[channel] = Zigzag.Decode((uint)package.ReadVarULong());
            }

            for (int channel = 0; channel < Channels; channel++)
            {
                brush.StepY[channel] = Zigzag.Decode((uint)package.ReadVarULong());
            }
        }

        brush.Validate();
        return brush;
    }

    /// <summary>Writes one brush.</summary>
    public void Write(BitPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        Validate();

        package.WriteVarULong((uint)Kind);

        switch (Kind)
        {
            case BrushKind.Rectangle:
            case BrushKind.Gradient:
                package.WriteVarULong(X);
                package.WriteVarULong(Y);
                package.WriteVarULong(Width);
                package.WriteVarULong(Height);
                package.WriteVarULong(Color);
                break;

            case BrushKind.Line:
                package.WriteVarULong(X0);
                package.WriteVarULong(Y0);
                package.WriteVarULong(X1);
                package.WriteVarULong(Y1);
                package.WriteVarULong(Stroke);
                package.WriteVarULong(Color);
                break;

            default:
                package.WriteVarULong(X0);
                package.WriteVarULong(Y0);
                package.WriteVarULong(X1);
                package.WriteVarULong(Y1);
                package.WriteVarULong(X2);
                package.WriteVarULong(Y2);
                package.WriteVarULong(Color);
                break;
        }

        if (Kind == BrushKind.Gradient)
        {
            foreach (int step in StepX)
            {
                package.WriteVarULong(Zigzag.Encode(step));
            }

            foreach (int step in StepY)
            {
                package.WriteVarULong(Zigzag.Encode(step));
            }
        }
    }

    public override string ToString() => Kind switch
    {
        BrushKind.Rectangle => $"rectangle {Width}x{Height} at {X},{Y}",
        BrushKind.Line => $"line {X0},{Y0} to {X1},{Y1} of width {Stroke}",
        BrushKind.Triangle => $"triangle {X0},{Y0} {X1},{Y1} {X2},{Y2}",
        _ => $"gradient {Width}x{Height} at {X},{Y}",
    };

    /// <summary>Channels a colour has, which is how many steps a gradient carries.</summary>
    private const int Channels = 4;

    /// <summary>Bits of a gradient step that are fractions of a level.</summary>
    private const int FractionBits = 8;

    /// <summary>Twice the area of the triangle three points make, sign telling which way round.</summary>
    private static long Cross(long ax, long ay, long bx, long by, long cx, long cy) =>
        ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));

    /// <summary>Whether a position lies inside the triangle, edges included.</summary>
    /// <remarks>
    /// Three cross products, all of one sign. Which sign depends on which way round the points
    /// were given, so the triangle's own winding decides it and either way round paints the
    /// same shape.
    /// </remarks>
    private bool InTriangle(uint x, uint y)
    {
        long winding = Cross(X0, Y0, X1, Y1, X2, Y2);
        if (winding == 0)
        {
            return false;
        }

        long first = Cross(X0, Y0, X1, Y1, x, y);
        long second = Cross(X1, Y1, X2, Y2, x, y);
        long third = Cross(X2, Y2, X0, Y0, x, y);

        return winding > 0
            ? first >= 0 && second >= 0 && third >= 0
            : first <= 0 && second <= 0 && third <= 0;
    }

    /// <summary>Whether a position lies within half the stroke of the segment.</summary>
    /// <remarks>
    /// Distances are compared squared and multiplied out, never taken as roots or fractions:
    /// <c>4 * distance^2 &lt;= stroke^2</c> is the same question with none of the rounding, and
    /// the same answer wherever it is asked.
    /// </remarks>
    private bool OnStroke(uint x, uint y)
    {
        long alongX = (long)X1 - X0;
        long alongY = (long)Y1 - Y0;
        long fromX = (long)x - X0;
        long fromY = (long)y - Y0;

        long length = (alongX * alongX) + (alongY * alongY);
        long stroke = (long)Stroke * Stroke;

        // A segment of no length is a dot, and its stroke is the disc around it.
        if (length == 0)
        {
            return 4 * ((fromX * fromX) + (fromY * fromY)) <= stroke;
        }

        long along = (fromX * alongX) + (fromY * alongY);

        // Past either end the nearest point of the segment is that end, which is what rounds
        // the caps.
        if (along <= 0)
        {
            return 4 * ((fromX * fromX) + (fromY * fromY)) <= stroke;
        }

        if (along >= length)
        {
            long toEndX = (long)x - X1;
            long toEndY = (long)y - Y1;
            return 4 * ((toEndX * toEndX) + (toEndY * toEndY)) <= stroke;
        }

        long across = (fromX * alongY) - (fromY * alongX);
        return 4 * across * across <= stroke * length;
    }
}

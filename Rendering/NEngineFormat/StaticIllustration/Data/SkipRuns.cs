namespace NEngineFormat.StaticIllustration.Data;

/// <summary>
/// Runs describing which values a channel actually uses, so the ones it never uses can be left
/// out of the numbering.
/// </summary>
/// <remarks>
/// <para>
/// A run is a pair: how many values in a row are usable, then how many are skipped over. A
/// channel using 0-16 and 240-255 is <c>(17, 223)</c> and then whatever is left, so its 32
/// values number 0 to 31 and fit in five bits rather than eight.
/// </para>
/// <para>
/// A leading gap is written as a run of no usable values followed by the gap, which is how a
/// channel that starts part way up the range says so.
/// </para>
/// </remarks>
public static class SkipRuns
{
    /// <summary>Turns a stored number into the value it stands for, jumping the gaps.</summary>
    public static uint Expand(ReadOnlySpan<uint> runs, uint compact)
    {
        if (runs.IsEmpty)
        {
            return compact;
        }

        uint remaining = compact;
        uint actual = 0;

        for (int i = 0; i + 1 < runs.Length; i += 2)
        {
            uint usable = runs[i];
            uint skipped = runs[i + 1];

            if (remaining < usable)
            {
                return actual + remaining;
            }

            remaining -= usable;
            actual += usable + skipped;
        }

        // Past the last pair every remaining value is usable.
        return actual + remaining;
    }

    /// <summary>Turns a value into the number it is stored as.</summary>
    /// <exception cref="InvalidOperationException">The value falls inside a gap.</exception>
    public static uint Compact(ReadOnlySpan<uint> runs, uint actual)
    {
        if (runs.IsEmpty)
        {
            return actual;
        }

        uint compact = 0;
        uint cursor = 0;

        for (int i = 0; i + 1 < runs.Length; i += 2)
        {
            uint usable = runs[i];
            uint skipped = runs[i + 1];

            if (actual < cursor + usable)
            {
                return compact + (actual - cursor);
            }

            if (actual < cursor + usable + skipped)
            {
                throw new InvalidOperationException(
                    $"Value {actual} falls in a skipped range, so it cannot be stored.");
            }

            compact += usable;
            cursor += usable + skipped;
        }

        return compact + (actual - cursor);
    }

    /// <summary>Whether a value survives a set of runs rather than falling in a gap.</summary>
    public static bool Holds(ReadOnlySpan<uint> runs, uint actual)
    {
        if (runs.IsEmpty)
        {
            return true;
        }

        uint cursor = 0;
        for (int i = 0; i + 1 < runs.Length; i += 2)
        {
            uint usable = runs[i];
            uint skipped = runs[i + 1];

            if (actual < cursor + usable)
            {
                return true;
            }

            if (actual < cursor + usable + skipped)
            {
                return false;
            }

            cursor += usable + skipped;
        }

        return true;
    }

    /// <summary>
    /// The runs describing a set of used values, or empty when nothing is skipped.
    /// </summary>
    /// <param name="used">One flag per value, from zero upwards.</param>
    public static uint[] Build(ReadOnlySpan<bool> used)
    {
        var runs = new List<uint>();
        uint usable = 0;
        uint gap = 0;
        int last = -1;

        for (int value = 0; value < used.Length; value++)
        {
            if (used[value])
            {
                last = value;
            }
        }

        // Everything past the last used value is trailing, and needs no run to say so.
        for (int value = 0; value <= last; value++)
        {
            if (used[value])
            {
                if (gap > 0)
                {
                    runs.Add(usable);
                    runs.Add(gap);
                    usable = 0;
                    gap = 0;
                }

                usable++;
            }
            else
            {
                gap++;
            }
        }

        return [.. runs];
    }

    /// <summary>How many values a set of runs leaves, out of a range of the given size.</summary>
    public static int Remaining(ReadOnlySpan<uint> runs, int size)
    {
        if (runs.IsEmpty)
        {
            return size;
        }

        int skipped = 0;
        for (int i = 1; i < runs.Length; i += 2)
        {
            skipped += (int)runs[i];
        }

        return size - skipped;
    }
}

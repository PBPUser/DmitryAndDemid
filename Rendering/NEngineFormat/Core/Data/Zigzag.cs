namespace NEngineFormat.Core.Data;

/// <summary>
/// Turns signed numbers into unsigned ones that stay small when the signed one is small.
/// </summary>
/// <remarks>
/// The order is 0, -1, 1, -2, 2, so a difference of one costs the same either way it went. A
/// plain bias would push every small number into the middle of the range instead, and a
/// two's-complement negative would fill every high bit.
/// </remarks>
public static class Zigzag
{
    /// <summary>The unsigned value a signed one is stored as.</summary>
    public static uint Encode(int value) => (uint)((value << 1) ^ (value >> 31));

    /// <summary>The signed value a stored one came from.</summary>
    public static int Decode(uint stored) => (int)(stored >> 1) ^ -(int)(stored & 1);
}

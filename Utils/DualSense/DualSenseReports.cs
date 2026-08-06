namespace DmitryAndDemid.Utils.DualSense;

/// <summary>
/// One trigger's adaptive-resistance program. The pad keeps running it on its own until replaced, so this is a
/// state to be set on change, not something to push every frame.
///
/// Only the three modes the game uses are modelled (plus a raw escape hatch). The parameter bytes are the first
/// three of the ten the report carries; the rest are always zero for these modes.
/// </summary>
public readonly struct TriggerEffect : IEquatable<TriggerEffect>
{
    /// <summary>Trigger free, no resistance. What the pad is in normally.</summary>
    public const byte ModeOff = 0x00;

    /// <summary>Constant resistance from a start position onwards ("rigid").</summary>
    public const byte ModeRigid = 0x01;

    /// <summary>Resistance over a section that gives way at the end ("pulse"/weapon click).</summary>
    public const byte ModePulse = 0x02;

    public readonly byte Mode;
    public readonly byte P1, P2, P3;

    public TriggerEffect(byte mode, byte p1 = 0, byte p2 = 0, byte p3 = 0)
    {
        Mode = mode;
        P1 = p1;
        P2 = p2;
        P3 = p3;
    }

    public static readonly TriggerEffect Off = new(ModeOff);

    /// <summary>
    /// Constant weight once the trigger passes <paramref name="start"/> (0 = at rest, 255 = fully pulled),
    /// with <paramref name="force"/> deciding how hard it pushes back.
    /// </summary>
    public static TriggerEffect Rigid(byte start, byte force) => new(ModeRigid, start, force);

    /// <summary>Resistance between <paramref name="start"/> and <paramref name="end"/> that releases past it.</summary>
    public static TriggerEffect Pulse(byte start, byte end, byte force) => new(ModePulse, start, end, force);

    public bool Equals(TriggerEffect other) =>
        Mode == other.Mode && P1 == other.P1 && P2 == other.P2 && P3 == other.P3;

    public override bool Equals(object? obj) => obj is TriggerEffect other && Equals(other);
    public override int GetHashCode() => (Mode << 24) | (P1 << 16) | (P2 << 8) | P3;
    public static bool operator ==(TriggerEffect a, TriggerEffect b) => a.Equals(b);
    public static bool operator !=(TriggerEffect a, TriggerEffect b) => !a.Equals(b);
}

/// <summary>
/// Everything one output report can ask the pad to do. Each feature has its own "control" flag because the report
/// is all-or-nothing per field: a field whose valid-flag bit is clear is ignored by the firmware, which is exactly
/// how the game leaves rumble alone (the kernel driver owns that through evdev) while still driving the lightbar.
/// </summary>
public struct DualSenseOutputState : IEquatable<DualSenseOutputState>
{
    public bool ControlLightbar;
    public byte Red, Green, Blue;

    /// <summary>
    /// Clears the firmware's start-up lightbar animation (the blue fade). Without it the pad ignores colour
    /// changes for the first couple of seconds after connecting, so the first report sets this.
    /// </summary>
    public bool ReleaseLightbarFade;

    public bool ControlPlayerLeds;

    /// <summary>Bit per LED, left to right: 0x01 0x02 0x04 0x08 0x10.</summary>
    public byte PlayerLeds;

    /// <summary>0 = bright, 1 = medium, 2 = dim.</summary>
    public byte PlayerLedBrightness;

    public bool ControlTriggers;
    public TriggerEffect LeftTrigger, RightTrigger;

    /// <summary>
    /// Rumble through the output report rather than through evdev. Off by default — on Linux the kernel's
    /// force-feedback path needs no permissions, so <see cref="EvdevRumble"/> handles it and this stays clear.
    /// </summary>
    public bool ControlMotors;
    public byte MotorLeft, MotorRight;

    public bool Equals(DualSenseOutputState o) =>
        ControlLightbar == o.ControlLightbar && Red == o.Red && Green == o.Green && Blue == o.Blue &&
        ReleaseLightbarFade == o.ReleaseLightbarFade &&
        ControlPlayerLeds == o.ControlPlayerLeds && PlayerLeds == o.PlayerLeds &&
        PlayerLedBrightness == o.PlayerLedBrightness &&
        ControlTriggers == o.ControlTriggers && LeftTrigger == o.LeftTrigger && RightTrigger == o.RightTrigger &&
        ControlMotors == o.ControlMotors && MotorLeft == o.MotorLeft && MotorRight == o.MotorRight;

    public override bool Equals(object? obj) => obj is DualSenseOutputState o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Red, Green, Blue, PlayerLeds, LeftTrigger, RightTrigger);
}

/// <summary>
/// Builds DualSense HID output reports. Pure byte-shuffling, no I/O — <see cref="DualSenseHidRaw"/> does the
/// writing — so the layout is unit-testable without a pad plugged in.
///
/// The pad takes two different reports for the same payload: USB sends report 0x02 with the 47-byte common block
/// straight after the id, Bluetooth sends report 0x31 with a sequence/tag pair in front, the same common block,
/// and a CRC-32 the firmware verifies (a report with a wrong CRC is silently dropped).
/// </summary>
public static class DualSenseReports
{
    public const byte UsbReportId = 0x02;
    public const byte BluetoothReportId = 0x31;

    public const int UsbReportLength = 48;
    public const int BluetoothReportLength = 78;

    /// <summary>Length of the payload shared by both transports.</summary>
    public const int CommonLength = 47;

    // Offsets INSIDE the common block.
    private const int ValidFlag0 = 0;
    private const int ValidFlag1 = 1;
    private const int MotorRight = 2;
    private const int MotorLeft = 3;
    private const int RightTriggerEffect = 10;   // 1 mode byte + 10 parameter bytes
    private const int LeftTriggerEffect = 21;
    private const int ValidFlag2 = 38;
    private const int LightbarSetup = 41;
    private const int LedBrightness = 42;
    private const int PlayerLeds = 43;
    private const int LightbarRed = 44;
    private const int LightbarGreen = 45;
    private const int LightbarBlue = 46;

    // valid_flag0
    private const byte FlagCompatibleVibration = 0x01;
    private const byte FlagRightTriggerEffect = 0x04;
    private const byte FlagLeftTriggerEffect = 0x08;

    // valid_flag1
    private const byte FlagLightbarControl = 0x04;
    private const byte FlagPlayerIndicatorControl = 0x10;

    // valid_flag2
    private const byte FlagLightbarSetupControl = 0x01;

    /// <summary>Tells the firmware to drop its start-up light animation and hand the lightbar over.</summary>
    private const byte LightbarSetupRelease = 0x02;

    /// <summary>The byte the pad prefixes to a Bluetooth report before checksumming it.</summary>
    private const byte BluetoothCrcSeed = 0xA2;

    public static byte[] BuildUsb(in DualSenseOutputState state)
    {
        byte[] report = new byte[UsbReportLength];
        report[0] = UsbReportId;
        WriteCommon(report.AsSpan(1, CommonLength), state);
        return report;
    }

    /// <summary>
    /// <paramref name="sequence"/> is a rolling 0-15 counter the pad uses to spot dropped reports; it goes in the
    /// high nibble of byte 1. Reusing a value is harmless, so callers may simply increment and wrap.
    /// </summary>
    public static byte[] BuildBluetooth(in DualSenseOutputState state, byte sequence)
    {
        byte[] report = new byte[BluetoothReportLength];
        report[0] = BluetoothReportId;
        report[1] = (byte)((sequence & 0x0F) << 4);
        report[2] = 0x10;
        WriteCommon(report.AsSpan(3, CommonLength), state);

        uint crc = Crc32(stackalloc byte[] { BluetoothCrcSeed });
        crc = Crc32(report.AsSpan(0, BluetoothReportLength - 4), crc);
        BitConverter.TryWriteBytes(report.AsSpan(BluetoothReportLength - 4), crc);
        return report;
    }

    private static void WriteCommon(Span<byte> common, in DualSenseOutputState state)
    {
        if (state.ControlMotors)
        {
            common[ValidFlag0] |= FlagCompatibleVibration;
            common[MotorLeft] = state.MotorLeft;
            common[MotorRight] = state.MotorRight;
        }

        if (state.ControlTriggers)
        {
            common[ValidFlag0] |= FlagRightTriggerEffect | FlagLeftTriggerEffect;
            WriteTrigger(common[RightTriggerEffect..], state.RightTrigger);
            WriteTrigger(common[LeftTriggerEffect..], state.LeftTrigger);
        }

        if (state.ControlLightbar)
        {
            common[ValidFlag1] |= FlagLightbarControl;
            common[LightbarRed] = state.Red;
            common[LightbarGreen] = state.Green;
            common[LightbarBlue] = state.Blue;
        }

        if (state.ReleaseLightbarFade)
        {
            common[ValidFlag2] |= FlagLightbarSetupControl;
            common[LightbarSetup] = LightbarSetupRelease;
        }

        if (state.ControlPlayerLeds)
        {
            common[ValidFlag1] |= FlagPlayerIndicatorControl;
            common[PlayerLeds] = (byte)(state.PlayerLeds & 0x1F);
            common[LedBrightness] = Math.Min(state.PlayerLedBrightness, (byte)2);
        }
    }

    private static void WriteTrigger(Span<byte> destination, in TriggerEffect effect)
    {
        destination[0] = effect.Mode;
        destination[1] = effect.P1;
        destination[2] = effect.P2;
        destination[3] = effect.P3;
    }

    /// <summary>
    /// The five player LEDs used as a life gauge: they fill outwards from the middle, so a glance at the pad
    /// reads the same way the life row does. Clamped to the five the hardware has.
    /// </summary>
    public static byte PlayerLedsForLives(int lives) => Math.Clamp(lives, 0, 5) switch
    {
        0 => 0x00,
        1 => 0x04,          //   . . X . .
        2 => 0x0A,          //   . X . X .
        3 => 0x0E,          //   . X X X .
        4 => 0x1B,          //   X X . X X
        _ => 0x1F,          //   X X X X X
    };

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            table[i] = value;
        }
        return table;
    }

    /// <summary>
    /// Standard CRC-32 (the zlib/IEEE one). The result is finalised, so <c>Crc32("123456789") == 0xCBF43926</c>,
    /// but it can still be chained: pass a previous return value as <paramref name="running"/> to continue over
    /// more bytes, which is how a Bluetooth report is checksummed in two passes (the 0xA2 seed byte, then the
    /// report). The default means "start fresh".
    /// </summary>
    public static uint Crc32(ReadOnlySpan<byte> data, uint running = 0)
    {
        // The value is carried around inverted (as ~crc) so that chaining calls needs no extra bookkeeping:
        // each call undoes the final XOR, folds in its bytes, and re-applies it.
        uint crc = ~running;
        foreach (byte b in data)
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}

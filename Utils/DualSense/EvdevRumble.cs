using System.Runtime.InteropServices;

namespace DmitryAndDemid.Utils.DualSense;

/// <summary>
/// Rumble through the kernel's force-feedback interface on the pad's event node.
///
/// This is the one DualSense feature that needs no extra permissions: logind hands the seat's user an ACL on
/// /dev/input/event*, so the game can upload an effect and play it as an ordinary user. It is also the reason
/// the game does NOT rumble through the HID output report — that node is root-only without a udev rule.
///
/// One effect slot is uploaded and then reused: the pad has a small, finite number of them, and uploading a
/// fresh effect per hit would exhaust the device after a few dozen deaths.
/// </summary>
internal sealed class EvdevRumble : IDisposable
{
    private int Descriptor = -1;
    private short EffectId = -1;

    public bool IsOpen => Descriptor >= 0;

    /// <summary>Why the last Open failed, for the diagnostics line in the controller settings screen.</summary>
    public string? Error { get; private set; }

    // struct ff_effect, as laid out by the kernel on a 64-bit build: the header fields, then the effect union
    // aligned to 8 (it contains a pointer in its largest member), which is where FF_RUMBLE's two magnitudes sit.
    private const int EffectSize = 48;
    private const int OffsetType = 0;
    private const int OffsetId = 2;
    private const int OffsetReplayLength = 10;
    private const int OffsetStrongMagnitude = 16;
    private const int OffsetWeakMagnitude = 18;

    private const ushort FfRumble = 0x50;

    // struct input_event on a 64-bit build: a 16-byte timeval, then type/code/value.
    private const int EventSize = 24;
    private const ushort EvForceFeedback = 0x15;

    private static readonly nuint UploadEffect = LinuxIo.IoWrite('E', 0x80, EffectSize);
    private static readonly nuint RemoveEffect = LinuxIo.IoWrite('E', 0x81, sizeof(int));

    private readonly byte[] EffectBuffer = new byte[EffectSize];
    private readonly byte[] EventBuffer = new byte[EventSize];

    public bool Open(string eventDevice)
    {
        Close();
        if (!OperatingSystem.IsLinux())
            return false;

        // Read-write: the force-feedback ioctls are rejected on a write-only descriptor.
        int descriptor = LinuxIo.Open(eventDevice,
            LinuxIo.OpenReadWrite | LinuxIo.OpenNonBlocking | LinuxIo.OpenCloseOnExec);
        if (descriptor < 0)
        {
            Error = $"{eventDevice}: {LinuxIo.ErrorText(Marshal.GetLastWin32Error())}";
            return false;
        }
        Descriptor = descriptor;
        Error = null;
        return true;
    }

    /// <summary>
    /// Runs the motors for <paramref name="milliseconds"/>. <paramref name="strong"/> drives the low-frequency
    /// (heavy) motor and <paramref name="weak"/> the high-frequency one; both are 0..1. The kernel stops the
    /// effect on its own when the time is up, so nothing has to be ticked afterwards.
    /// </summary>
    public bool Play(float strong, float weak, int milliseconds)
    {
        if (!IsOpen)
            return false;

        ushort strongMagnitude = ToMagnitude(strong);
        ushort weakMagnitude = ToMagnitude(weak);
        if (strongMagnitude == 0 && weakMagnitude == 0)
            return Stop();

        Array.Clear(EffectBuffer);
        BitConverter.TryWriteBytes(EffectBuffer.AsSpan(OffsetType), FfRumble);
        // A negative id asks the kernel for a new slot; afterwards it writes the assigned id back into the
        // buffer and we keep passing that, which updates the same slot in place.
        BitConverter.TryWriteBytes(EffectBuffer.AsSpan(OffsetId), EffectId);
        BitConverter.TryWriteBytes(EffectBuffer.AsSpan(OffsetReplayLength), (ushort)Math.Clamp(milliseconds, 1, 30000));
        BitConverter.TryWriteBytes(EffectBuffer.AsSpan(OffsetStrongMagnitude), strongMagnitude);
        BitConverter.TryWriteBytes(EffectBuffer.AsSpan(OffsetWeakMagnitude), weakMagnitude);

        if (LinuxIo.Ioctl(Descriptor, UploadEffect, EffectBuffer) < 0)
        {
            Error = "upload: " + LinuxIo.ErrorText(Marshal.GetLastWin32Error());
            return false;
        }
        EffectId = BitConverter.ToInt16(EffectBuffer, OffsetId);
        return WriteEvent(EffectId, 1);
    }

    /// <summary>
    /// Stops whatever is playing. "Nothing was playing" counts as success — the caller treats a false here as a
    /// dead device and tears the handles down, which stopping an idle pad must not do.
    /// </summary>
    public bool Stop()
    {
        if (!IsOpen || EffectId < 0)
            return true;
        return WriteEvent(EffectId, 0);
    }

    /// <summary>Playing an effect is a normal input event written back to the device: EV_FF, the effect id, 1/0.</summary>
    private bool WriteEvent(short effectId, int value)
    {
        Array.Clear(EventBuffer);
        BitConverter.TryWriteBytes(EventBuffer.AsSpan(16), EvForceFeedback);
        BitConverter.TryWriteBytes(EventBuffer.AsSpan(18), (ushort)effectId);
        BitConverter.TryWriteBytes(EventBuffer.AsSpan(20), value);
        if (LinuxIo.Write(Descriptor, EventBuffer, EventSize) == EventSize)
            return true;
        Error = "play: " + LinuxIo.ErrorText(Marshal.GetLastWin32Error());
        return false;
    }

    private static ushort ToMagnitude(float value) => (ushort)(Math.Clamp(value, 0f, 1f) * ushort.MaxValue);

    public void Close()
    {
        if (Descriptor < 0)
            return;
        if (EffectId >= 0)
        {
            Stop();
            LinuxIo.Ioctl(Descriptor, RemoveEffect, EffectId);
            EffectId = -1;
        }
        LinuxIo.Close(Descriptor);
        Descriptor = -1;
    }

    public void Dispose() => Close();
}

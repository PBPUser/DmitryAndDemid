using System.Runtime.InteropServices;

namespace DmitryAndDemid.Utils.DualSense;

/// <summary>
/// The raw HID node. This is the only way to reach the adaptive triggers — the kernel driver exposes the
/// lightbar and the player LEDs as LED class devices, but it has no interface at all for trigger resistance.
///
/// /dev/hidraw* is root-only on a stock system, so this fails to open for most users; that is expected and the
/// game carries on without triggers. Tools/99-dualsense.rules grants access if the player wants them.
/// </summary>
internal sealed class DualSenseHidRaw : IDisposable
{
    private int Descriptor = -1;
    private readonly bool Bluetooth;
    private byte Sequence;

    public bool IsOpen => Descriptor >= 0;
    public string? Error { get; private set; }

    public DualSenseHidRaw(bool bluetooth) => Bluetooth = bluetooth;

    public bool Open(string hidRawDevice)
    {
        Close();
        if (!OperatingSystem.IsLinux())
            return false;

        int descriptor = LinuxIo.Open(hidRawDevice,
            LinuxIo.OpenWriteOnly | LinuxIo.OpenNonBlocking | LinuxIo.OpenCloseOnExec);
        if (descriptor < 0)
        {
            Error = $"{hidRawDevice}: {LinuxIo.ErrorText(Marshal.GetLastWin32Error())}";
            return false;
        }
        Descriptor = descriptor;
        Error = null;
        return true;
    }

    public bool Send(in DualSenseOutputState state)
    {
        if (!IsOpen)
            return false;

        byte[] report = Bluetooth
            ? DualSenseReports.BuildBluetooth(state, Sequence++)
            : DualSenseReports.BuildUsb(state);

        if (LinuxIo.Write(Descriptor, report, report.Length) == report.Length)
            return true;
        Error = "write: " + LinuxIo.ErrorText(Marshal.GetLastWin32Error());
        return false;
    }

    public void Close()
    {
        if (Descriptor < 0)
            return;
        LinuxIo.Close(Descriptor);
        Descriptor = -1;
    }

    public void Dispose() => Close();
}

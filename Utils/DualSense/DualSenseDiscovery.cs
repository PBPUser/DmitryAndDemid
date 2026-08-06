using System.Globalization;

namespace DmitryAndDemid.Utils.DualSense;

/// <summary>The nodes one connected pad exposes. Any of them may be null — the pad is still usable without.</summary>
public sealed class DualSenseDeviceInfo
{
    /// <summary>The HID device directory, e.g. <c>/sys/bus/hid/devices/0003:054C:0CE6.0055</c>.</summary>
    public required string SysPath { get; init; }

    public required string Name { get; init; }

    /// <summary>True when connected over Bluetooth, which changes the output report's shape (see DualSenseReports).</summary>
    public required bool Bluetooth { get; init; }

    /// <summary>The gamepad's event node, e.g. <c>/dev/input/event21</c> — where rumble goes.</summary>
    public string? EventDevice { get; init; }

    /// <summary>e.g. <c>/dev/hidraw6</c>. Root-only unless the udev rule is installed; see docs/dualsense.md.</summary>
    public string? HidRawDevice { get; init; }

    /// <summary>The lightbar's LED class directory (writing <c>multi_intensity</c> tints it).</summary>
    public string? LightbarPath { get; init; }

    /// <summary>The five player-indicator LED directories, left to right.</summary>
    public IReadOnlyList<string> PlayerLedPaths { get; init; } = [];
}

/// <summary>
/// Finds DualSense pads by walking sysfs. This is deliberately NOT how the game reads buttons — the backends
/// already do that through GLFW/SDL, which sees the pad as a generic gamepad. What sysfs adds is the hardware the
/// generic path cannot reach: the rumble motors' event node, the lightbar, the player LEDs and the raw HID node
/// the adaptive triggers need.
///
/// Linux-only. Every other platform gets an empty list and the whole feature quietly turns itself off.
/// </summary>
public static class DualSenseDiscovery
{
    private const string SonyVendorId = "054C";

    /// <summary>DualSense (0CE6) and DualSense Edge (0DF2) — the Edge speaks the same output reports.</summary>
    private static readonly string[] ProductIds = ["0CE6", "0DF2"];

    /// <summary>HID bus ids, as the leading field of a device directory's name.</summary>
    private const string BusUsb = "0003";
    private const string BusBluetooth = "0005";

    /// <summary>
    /// The kernel splits the pad into several input devices sharing one HID device. Only the first is the
    /// gamepad; these suffixes mark the others, which have no buttons we care about.
    /// </summary>
    private static readonly string[] SecondaryInputSuffixes = ["Motion Sensors", "Touchpad", "Headset Jack"];

    /// <summary>
    /// <paramref name="sysRoot"/> and <paramref name="devRoot"/> exist so the walk can be pointed at a fake tree
    /// in tests; in the game they are always /sys and /dev.
    /// </summary>
    public static List<DualSenseDeviceInfo> Scan(string sysRoot = "/sys", string devRoot = "/dev")
    {
        var found = new List<DualSenseDeviceInfo>();
        if (!OperatingSystem.IsLinux())
            return found;

        string hidDevices = Path.Combine(sysRoot, "bus", "hid", "devices");
        if (!Directory.Exists(hidDevices))
            return found;

        foreach (string device in Directory.GetDirectories(hidDevices).OrderBy(d => d, StringComparer.Ordinal))
        {
            string id = Path.GetFileName(device);
            if (!IsDualSense(id, out bool bluetooth))
                continue;

            string? inputDirectory = FindGamepadInput(device);
            found.Add(new DualSenseDeviceInfo
            {
                SysPath = device,
                Name = ReadUeventValue(device, "HID_NAME") ?? "DualSense Wireless Controller",
                Bluetooth = bluetooth,
                EventDevice = inputDirectory is null ? null : FindEventNode(inputDirectory, devRoot),
                HidRawDevice = FindChildNode(device, "hidraw", "hidraw", devRoot),
                LightbarPath = FindLed(device, sysRoot, name => name.EndsWith(":rgb:indicator", StringComparison.Ordinal)),
                PlayerLedPaths = FindPlayerLeds(device, sysRoot),
            });
        }
        return found;
    }

    /// <summary>
    /// A HID device directory is named <c>BUS:VENDOR:PRODUCT.INSTANCE</c>, e.g. <c>0003:054C:0CE6.0055</c>.
    /// The bus tells USB from Bluetooth, which is the only thing that changes in how we talk to the pad.
    /// </summary>
    public static bool IsDualSense(string directoryName, out bool bluetooth)
    {
        bluetooth = false;
        string[] parts = directoryName.Split('.')[0].Split(':');
        if (parts.Length != 3)
            return false;
        if (!parts[1].Equals(SonyVendorId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!ProductIds.Any(p => p.Equals(parts[2], StringComparison.OrdinalIgnoreCase)))
            return false;
        // Anything that is neither USB nor Bluetooth (a virtual/uhid pad, say) is not something we can drive.
        if (parts[0] != BusUsb && parts[0] != BusBluetooth)
            return false;
        bluetooth = parts[0] == BusBluetooth;
        return true;
    }

    /// <summary>
    /// Picks the gamepad out of the pad's several input devices. Force-feedback capability is the reliable mark
    /// (only the gamepad node has motors); the name suffixes are the fallback for a kernel that reports no
    /// capabilities file.
    /// </summary>
    private static string? FindGamepadInput(string device)
    {
        string inputRoot = Path.Combine(device, "input");
        if (!Directory.Exists(inputRoot))
            return null;

        string[] inputs = Directory.GetDirectories(inputRoot).OrderBy(d => d, StringComparer.Ordinal).ToArray();
        string? byName = null;
        foreach (string input in inputs)
        {
            if (HasForceFeedback(input))
                return input;
            if (byName is null && !IsSecondaryInput(input))
                byName = input;
        }
        return byName;
    }

    private static bool HasForceFeedback(string inputDirectory)
    {
        // capabilities/ff is a bitmask printed as space-separated 64-bit words, most significant first. We only
        // need "is any bit set" — the pad advertises FF_RUMBLE and nothing else advertises anything.
        string path = Path.Combine(inputDirectory, "capabilities", "ff");
        if (!File.Exists(path))
            return false;
        try
        {
            foreach (string word in File.ReadAllText(path).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (ulong.TryParse(word.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong bits)
                    && bits != 0)
                    return true;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return false;
    }

    private static bool IsSecondaryInput(string inputDirectory)
    {
        string? name = ReadFirstLine(Path.Combine(inputDirectory, "name"));
        return name is not null &&
               SecondaryInputSuffixes.Any(s => name.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Turns .../input/input157/event21 into /dev/input/event21.</summary>
    private static string? FindEventNode(string inputDirectory, string devRoot)
    {
        string? node = Directory.GetDirectories(inputDirectory)
            .Select(Path.GetFileName)
            .FirstOrDefault(n => n is not null && n.StartsWith("event", StringComparison.Ordinal));
        return node is null ? null : Path.Combine(devRoot, "input", node);
    }

    /// <summary>Turns .../hidraw/hidraw6 into /dev/hidraw6.</summary>
    private static string? FindChildNode(string device, string subdirectory, string prefix, string devRoot)
    {
        string directory = Path.Combine(device, subdirectory);
        if (!Directory.Exists(directory))
            return null;
        string? node = Directory.GetDirectories(directory)
            .Select(Path.GetFileName)
            .FirstOrDefault(n => n is not null && n.StartsWith(prefix, StringComparison.Ordinal));
        return node is null ? null : Path.Combine(devRoot, node);
    }

    /// <summary>
    /// The pad's LEDs are listed under its HID directory but the writable attributes live in the LED class, so
    /// the names found here are resolved against /sys/class/leds.
    /// </summary>
    private static string? FindLed(string device, string sysRoot, Func<string, bool> match) =>
        LedNames(device).Where(match).Select(n => Path.Combine(sysRoot, "class", "leds", n)).FirstOrDefault();

    private static string[] FindPlayerLeds(string device, string sysRoot) =>
        LedNames(device)
            .Where(n => n.Contains(":white:player-", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => Path.Combine(sysRoot, "class", "leds", n))
            .ToArray();

    private static IEnumerable<string> LedNames(string device)
    {
        string directory = Path.Combine(device, "leds");
        if (!Directory.Exists(directory))
            return [];
        return Directory.GetDirectories(directory).Select(Path.GetFileName).OfType<string>();
    }

    private static string? ReadUeventValue(string device, string key)
    {
        string path = Path.Combine(device, "uevent");
        if (!File.Exists(path))
            return null;
        try
        {
            foreach (string line in File.ReadLines(path))
                if (line.StartsWith(key + "=", StringComparison.Ordinal))
                    return line[(key.Length + 1)..];
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private static string? ReadFirstLine(string path)
    {
        if (!File.Exists(path))
            return null;
        try { return File.ReadLines(path).FirstOrDefault()?.Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}

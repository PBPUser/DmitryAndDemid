namespace DmitryAndDemid.Utils.DualSense;

/// <summary>
/// The lightbar and the five player LEDs through the kernel's LED class. The driver turns a write here into an
/// HID output report on a workqueue, so these writes do not block on the USB/Bluetooth link.
///
/// Preferred over the raw HID path for lights because it cooperates with the driver instead of racing it; used
/// only as the fallback when /dev/hidraw is unreadable, since the LED attributes are themselves root-only
/// without the udev rule (see Tools/99-dualsense.rules).
/// </summary>
internal sealed class SysfsLeds
{
    private readonly string? LightbarPath;
    private readonly IReadOnlyList<string> PlayerLedPaths;

    /// <summary>Set once a write fails, so a permission error is reported (and retried) rather than spammed.</summary>
    public string? Error { get; private set; }

    public bool LightbarAvailable { get; private set; }
    public bool PlayerLedsAvailable { get; private set; }

    public SysfsLeds(DualSenseDeviceInfo device)
    {
        LightbarPath = device.LightbarPath;
        PlayerLedPaths = device.PlayerLedPaths;
        LightbarAvailable = LightbarPath is not null && IsWritable(Path.Combine(LightbarPath, "multi_intensity"));
        PlayerLedsAvailable = PlayerLedPaths.Count > 0 &&
                              IsWritable(Path.Combine(PlayerLedPaths[0], "brightness"));
    }

    /// <summary>
    /// The LED class splits colour from intensity: multi_intensity holds the per-channel values and brightness
    /// scales the whole thing, so the brightness has to be opened up once or the colour never shows.
    /// </summary>
    public bool SetLightbar(byte red, byte green, byte blue)
    {
        if (!LightbarAvailable || LightbarPath is null)
            return false;
        if (!Write(Path.Combine(LightbarPath, "multi_intensity"), $"{red} {green} {blue}"))
            return false;
        // The scale only has to be opened up once; re-writing it on every colour change would double the number
        // of output reports the driver sends for no visible difference.
        if (BrightnessWritten)
            return true;
        BrightnessWritten = Write(Path.Combine(LightbarPath, "brightness"), "255");
        return BrightnessWritten;
    }

    private bool BrightnessWritten;

    /// <summary>Lights the LEDs named by the bit mask (bit 0 = leftmost), as built by DualSenseReports.</summary>
    public bool SetPlayerLeds(byte mask)
    {
        if (!PlayerLedsAvailable)
            return false;
        bool ok = true;
        for (int i = 0; i < PlayerLedPaths.Count; i++)
            ok &= Write(Path.Combine(PlayerLedPaths[i], "brightness"), (mask >> i & 1) == 1 ? "1" : "0");
        return ok;
    }

    private static bool IsWritable(string path)
    {
        return false;
        try
        {
            if (!File.Exists(path))
                return false;
            using FileStream probe = File.Open(path, FileMode.Open, FileAccess.Write);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private bool Write(string path, string value)
    {
        try
        {
            File.WriteAllText(path, value + "\n");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A pad unplugged mid-write takes its sysfs entries with it; stop claiming the feature works.
            Error = $"{path}: {e.Message}";
            LightbarAvailable = false;
            PlayerLedsAvailable = false;
            return false;
        }
    }
}

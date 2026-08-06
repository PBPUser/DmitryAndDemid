using DmitryAndDemid.Rendering;

namespace DmitryAndDemid.Utils.DualSense;

/// <summary>
/// The game-facing handle on a DualSense: rumble, lightbar, player LEDs and adaptive triggers.
///
/// Buttons and sticks do NOT come through here — the renderer backends already read the pad as a generic
/// gamepad through GLFW/SDL, and that keeps working whatever this class decides. Everything below is the extra
/// hardware that generic path cannot see, so every part of it is optional: no pad, a pad the user has not given
/// permission for, or a non-Linux platform all leave the game running exactly as it did before.
///
/// Calls are cheap and idempotent. Lights and triggers are stored as a desired state and only written when they
/// actually change (and at most <see cref="MinimumWriteIntervalMs"/> apart), so a caller may set them every
/// frame; rumble is sent immediately, because it is an event rather than a state.
/// </summary>
public static class DualSensePad
{
    private static DualSenseDeviceInfo? Device;
    private static EvdevRumble? Rumbler;
    private static DualSenseHidRaw? HidRaw;
    private static SysfsLeds? Leds;

    private static DualSenseOutputState Desired;
    private static DualSenseOutputState Applied;
    private static bool HasApplied;
    private static long LastWriteMs;
    private static long LastScanMs;
    private static bool Initialized;

    /// <summary>Lights change on a human timescale; 30 Hz is far more than the eye needs and keeps writes rare.</summary>
    private const long MinimumWriteIntervalMs = 33;

    /// <summary>How often to look for a pad that was plugged in after startup.</summary>
    private const long RescanIntervalMs = 2000;

    public static bool IsConnected => Device is not null;

    /// <summary>True when the pad's motors can actually be driven (needs only the usual seat ACL on Linux).</summary>
    public static bool RumbleAvailable => Rumbler?.IsOpen == true;

    /// <summary>True when the lightbar/player LEDs are reachable, through either the raw HID node or sysfs.</summary>
    public static bool LightsAvailable => HidRaw?.IsOpen == true || Leds?.LightbarAvailable == true;

    /// <summary>Adaptive triggers exist only on the raw HID node, which normally needs the udev rule.</summary>
    public static bool TriggersAvailable => HidRaw?.IsOpen == true;

    /// <summary>What went wrong, when a feature is missing — shown on the controller settings screen.</summary>
    public static string? Diagnostic { get; private set; }

    public static void Initialize()
    {
        if (Initialized)
            return;
        Initialized = true;
        Scan();
    }

    /// <summary>
    /// Called once a frame. Picks up a pad plugged in mid-session and flushes any pending light/trigger change.
    /// </summary>
    public static void Poll()
    {
        if (!Initialized)
            return;

        long now = Environment.TickCount64;
        if (!IsConnected)
        {
            if (now - LastScanMs >= RescanIntervalMs)
                Scan();
            return;
        }

        if (now - LastWriteMs < MinimumWriteIntervalMs)
            return;
        Flush(now);
    }

    /// <summary>
    /// Runs the motors for <paramref name="milliseconds"/>: <paramref name="strong"/> is the heavy low-frequency
    /// motor, <paramref name="weak"/> the lighter high-frequency one, both 0..1. Silently does nothing when the
    /// player has rumble off or the pad is absent.
    /// </summary>
    public static void Rumble(float strong, float weak, int milliseconds)
    {
        if (!Configuration.Config.DualSenseRumble || Rumbler is null || !Rumbler.IsOpen)
            return;
        float scale = Math.Clamp(Configuration.Config.DualSenseRumbleStrength, 0f, 1f);
        if (scale <= 0f)
            return;
        if (!Rumbler.Play(strong * scale, weak * scale, milliseconds))
            Drop("rumble: " + Rumbler.Error);
    }

    public static void StopRumble() => Rumbler?.Stop();

    /// <summary>
    /// Sets the lightbar colour. Black turns it off. A switched-off setting is applied rather than ignored — it
    /// resolves to black — so turning the lightbar off in the menu darkens the pad immediately instead of
    /// freezing it on whatever colour the last frame set.
    /// </summary>
    public static void SetLightbar(Rgba color)
    {
        if (!Configuration.Config.DualSenseLightbar)
            color = new Rgba(0, 0, 0);
        Desired.ControlLightbar = true;
        Desired.Red = color.R;
        Desired.Green = color.G;
        Desired.Blue = color.B;
    }

    /// <summary>Shows a life count on the five player LEDs, filling outwards from the middle.</summary>
    public static void SetPlayerLives(int lives)
    {
        if (!Configuration.Config.DualSenseLightbar)
            lives = 0;
        Desired.ControlPlayerLeds = true;
        Desired.PlayerLeds = DualSenseReports.PlayerLedsForLives(lives);
    }

    /// <summary>Programs both triggers' resistance. The pad keeps running them until they are set again.</summary>
    public static void SetTriggers(TriggerEffect left, TriggerEffect right)
    {
        if (!Configuration.Config.DualSenseTriggers)
            (left, right) = (TriggerEffect.Off, TriggerEffect.Off);
        Desired.ControlTriggers = true;
        Desired.LeftTrigger = left;
        Desired.RightTrigger = right;
    }

    /// <summary>
    /// Hands the pad back the way it was found: motors stopped, triggers released, lights dark. Without this a
    /// quit leaves the triggers stiff and the lightbar stuck on whatever colour the last frame set.
    /// </summary>
    public static void Shutdown()
    {
        if (!Initialized)
            return;
        Initialized = false;

        if (IsConnected)
        {
            Rumbler?.Stop();
            Desired = new DualSenseOutputState
            {
                ControlLightbar = true,
                ControlPlayerLeds = true,
                ControlTriggers = true,
                LeftTrigger = TriggerEffect.Off,
                RightTrigger = TriggerEffect.Off,
            };
            Flush(Environment.TickCount64);
        }

        Rumbler?.Dispose();
        HidRaw?.Dispose();
        Rumbler = null;
        HidRaw = null;
        Leds = null;
        Device = null;
        Desired = default;
        Applied = default;
        HasApplied = false;
    }

    /// <summary>A one-line summary of what the pad can do here, for the controller settings screen.</summary>
    public static string StatusLine()
    {
        if (!IsConnected)
            return "not connected";
        var parts = new List<string> { Device!.Bluetooth ? "bluetooth" : "usb" };
        if (RumbleAvailable) parts.Add("rumble");
        if (LightsAvailable) parts.Add("light");
        if (TriggersAvailable) parts.Add("triggers");
        return string.Join(" + ", parts);
    }

    private static void Scan()
    {
        LastScanMs = Environment.TickCount64;
        DualSenseDeviceInfo? device = DualSenseDiscovery.Scan().FirstOrDefault();
        if (device is null)
        {
            Device = null;
            return;
        }

        Device = device;
        HasApplied = false;

        if (device.EventDevice is not null)
        {
            var rumbler = new EvdevRumble();
            Rumbler = rumbler.Open(device.EventDevice) ? rumbler : null;
            if (Rumbler is null)
                Diagnostic = rumbler.Error;
        }

        if (device.HidRawDevice is not null)
        {
            var hidRaw = new DualSenseHidRaw(device.Bluetooth);
            HidRaw = hidRaw.Open(device.HidRawDevice) ? hidRaw : null;
            if (HidRaw is null)
                Diagnostic = hidRaw.Error;
        }

        // The LED class is the fallback for lights when the raw node is closed to us. It is also usually
        // root-only, so this can come up empty too — the game just runs without lights then.
        Leds = HidRaw is null ? new SysfsLeds(device) : null;
        if (Leds is not null && !Leds.LightbarAvailable)
            Diagnostic ??= "lightbar: permission denied (see docs/dualsense.md)";

        // The firmware plays its own light animation for a moment after connecting and ignores colour writes
        // until told to stop; ask on the first report we send.
        Desired.ReleaseLightbarFade = true;
    }

    private static void Flush(long now)
    {
        if (HasApplied && Desired.Equals(Applied))
            return;
        LastWriteMs = now;

        bool ok;
        if (HidRaw is not null && HidRaw.IsOpen)
        {
            ok = HidRaw.Send(Desired);
            if (!ok)
                Drop("lights: " + HidRaw.Error);
        }
        else if (Leds is not null)
        {
            ok = true;
            if (Desired.ControlLightbar)
                ok &= Leds.SetLightbar(Desired.Red, Desired.Green, Desired.Blue);
            if (Desired.ControlPlayerLeds)
                ok &= Leds.SetPlayerLeds(Desired.PlayerLeds);
        }
        else
        {
            ok = true;   // nothing to write to; the state is still remembered for when a pad shows up
        }

        if (!ok)
            return;
        Applied = Desired;
        // Releasing the start-up animation is a one-shot: leaving the bit set would re-send it forever.
        Desired.ReleaseLightbarFade = false;
        Applied.ReleaseLightbarFade = false;
        HasApplied = true;
    }

    /// <summary>
    /// A write failed — almost always because the pad was unplugged. Tear the handles down and let the next
    /// <see cref="Poll"/> rescan, rather than failing once per frame forever.
    /// </summary>
    private static void Drop(string reason)
    {
        Diagnostic = reason;
        Rumbler?.Dispose();
        HidRaw?.Dispose();
        Rumbler = null;
        HidRaw = null;
        Leds = null;
        Device = null;
        HasApplied = false;
        LastScanMs = Environment.TickCount64;
    }
}

using DmitryAndDemid.Rendering;

namespace DmitryAndDemid.Utils.DualSense;

/// <summary>
/// <c>--dualsense-test</c>: exercises every part of the pad from the command line, without opening a window.
///
/// The point is that the DualSense extras are the one thing in the game that cannot be checked by looking at the
/// screen — a lightbar colour or a trigger's weight is only verifiable in your hands. This runs each feature in
/// turn, printing what it is about to do, so a failure can be pinned on a specific node (and its errno) instead
/// of on "the controller doesn't work".
/// </summary>
public static class DualSenseSelfTest
{
    public static int Run()
    {
        Console.WriteLine("=== DualSense test ===");

        List<DualSenseDeviceInfo> devices = DualSenseDiscovery.Scan();
        Console.WriteLine($"pads found: {devices.Count}");
        foreach (DualSenseDeviceInfo device in devices)
        {
            Console.WriteLine($"  {device.Name}");
            Console.WriteLine($"    sysfs:    {device.SysPath}");
            Console.WriteLine($"    link:     {(device.Bluetooth ? "bluetooth" : "usb")}");
            Console.WriteLine($"    event:    {device.EventDevice ?? "(none)"}");
            Console.WriteLine($"    hidraw:   {device.HidRawDevice ?? "(none)"}");
            Console.WriteLine($"    lightbar: {device.LightbarPath ?? "(none)"}");
            Console.WriteLine($"    player:   {device.PlayerLedPaths.Count} LEDs");
        }
        if (devices.Count == 0)
        {
            Console.WriteLine("No DualSense connected — nothing to test.");
            return 1;
        }

        DualSensePad.Initialize();
        Console.WriteLine($"status: {DualSensePad.StatusLine()}");
        Console.WriteLine($"  rumble:   {DualSensePad.RumbleAvailable}");
        Console.WriteLine($"  lights:   {DualSensePad.LightsAvailable}");
        Console.WriteLine($"  triggers: {DualSensePad.TriggersAvailable}");
        if (DualSensePad.Diagnostic is { } diagnostic)
            Console.WriteLine($"  note: {diagnostic}");

        if (DualSensePad.RumbleAvailable)
        {
            Console.WriteLine("rumble: light (high-frequency motor), 400ms");
            DualSensePad.Rumble(0f, 0.6f, 400);
            Thread.Sleep(900);
            Console.WriteLine("rumble: heavy (low-frequency motor), 400ms");
            DualSensePad.Rumble(1f, 0f, 400);
            Thread.Sleep(900);
            Console.WriteLine("rumble: both, 400ms");
            DualSensePad.Rumble(1f, 1f, 400);
            Thread.Sleep(900);
        }
        else
        {
            Console.WriteLine("rumble: UNAVAILABLE (no write access to the event node?)");
        }

        if (DualSensePad.LightsAvailable)
        {
            foreach ((string name, Rgba color) in new[]
                     {
                         ("red", new Rgba(255, 0, 0)), ("green", new Rgba(0, 255, 0)),
                         ("blue", new Rgba(0, 0, 255)), ("off", new Rgba(0, 0, 0)),
                     })
            {
                Console.WriteLine($"lightbar: {name}");
                DualSensePad.SetLightbar(color);
                Flush();
                Thread.Sleep(700);
            }

            for (int lives = 0; lives <= 5; lives++)
            {
                Console.WriteLine($"player LEDs: {lives}");
                DualSensePad.SetPlayerLives(lives);
                Flush();
                Thread.Sleep(400);
            }
        }
        else
        {
            Console.WriteLine("lights: UNAVAILABLE (install Tools/99-dualsense.rules — see docs/dualsense.md)");
        }

        if (DualSensePad.TriggersAvailable)
        {
            Console.WriteLine("triggers: both rigid — pull L2/R2, they should push back");
            DualSensePad.SetTriggers(TriggerEffect.Rigid(0x40, 0xC0), TriggerEffect.Rigid(0x40, 0xC0));
            Flush();
            Thread.Sleep(4000);
            Console.WriteLine("triggers: released");
            DualSensePad.SetTriggers(TriggerEffect.Off, TriggerEffect.Off);
            Flush();
            Thread.Sleep(500);
        }
        else
        {
            Console.WriteLine("triggers: UNAVAILABLE (needs /dev/hidraw access — see docs/dualsense.md)");
        }

        DualSensePad.Shutdown();
        Console.WriteLine("DUALSENSE TEST DONE");
        return 0;
    }

    /// <summary>
    /// Lights and triggers are a desired state that <see cref="DualSensePad.Poll"/> writes out, and Poll rate-limits
    /// itself — so a test stepping through states faster than the game's frame rate has to wait its turn.
    /// </summary>
    private static void Flush()
    {
        Thread.Sleep(40);
        DualSensePad.Poll();
    }
}

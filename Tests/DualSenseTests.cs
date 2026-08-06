using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using DmitryAndDemid.Utils.DualSense;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// The DualSense layer without a DualSense. Everything here is either pure byte-shuffling (the HID output
/// reports, the CRC-32 the pad checks them against, the LED patterns) or a filesystem walk — and the walk takes
/// its root as a parameter precisely so it can be pointed at a fabricated sysfs tree instead of the real one.
///
/// What is NOT covered: the ioctls and the device writes, which need a pad in someone's hands. That is what
/// <c>--dualsense-test</c> (Utils/DualSense/DualSenseSelfTest.cs) is for.
/// </summary>
public class DualSenseTests
{
    // ---- report layout -------------------------------------------------------------------------------------

    [Fact]
    public void Usb_report_has_the_right_id_and_length()
    {
        byte[] report = DualSenseReports.BuildUsb(new DualSenseOutputState());
        Assert.Equal(48, report.Length);
        Assert.Equal(0x02, report[0]);
    }

    [Fact]
    public void An_empty_state_asks_the_pad_for_nothing()
    {
        // Every payload byte is ignored unless its valid-flag bit is set, so a report with no flags must leave
        // the pad exactly as it was. This is what lets the game drive the lightbar without disturbing the
        // rumble the kernel is running.
        byte[] report = DualSenseReports.BuildUsb(new DualSenseOutputState());
        Assert.All(report[1..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Lightbar_colour_lands_in_the_documented_bytes()
    {
        byte[] report = DualSenseReports.BuildUsb(new DualSenseOutputState
        {
            ControlLightbar = true, Red = 0x11, Green = 0x22, Blue = 0x33,
        });

        Assert.Equal(0x04, report[2] & 0x04);   // valid_flag1: LIGHTBAR_CONTROL_ENABLE
        Assert.Equal(0x11, report[45]);
        Assert.Equal(0x22, report[46]);
        Assert.Equal(0x33, report[47]);
    }

    [Fact]
    public void Trigger_effects_land_in_the_documented_bytes()
    {
        byte[] report = DualSenseReports.BuildUsb(new DualSenseOutputState
        {
            ControlTriggers = true,
            RightTrigger = TriggerEffect.Rigid(0x40, 0x80),
            LeftTrigger = TriggerEffect.Pulse(0x10, 0x20, 0x30),
        });

        Assert.Equal(0x0C, report[1] & 0x0C);   // valid_flag0: both trigger-effect bits

        Assert.Equal(0x01, report[11]);         // right trigger: rigid
        Assert.Equal(0x40, report[12]);
        Assert.Equal(0x80, report[13]);

        Assert.Equal(0x02, report[22]);         // left trigger: pulse
        Assert.Equal(0x10, report[23]);
        Assert.Equal(0x20, report[24]);
        Assert.Equal(0x30, report[25]);
    }

    [Fact]
    public void Player_leds_and_brightness_land_in_the_documented_bytes()
    {
        byte[] report = DualSenseReports.BuildUsb(new DualSenseOutputState
        {
            ControlPlayerLeds = true, PlayerLeds = 0x1F, PlayerLedBrightness = 9,
        });

        Assert.Equal(0x10, report[2] & 0x10);   // valid_flag1: PLAYER_INDICATOR_CONTROL_ENABLE
        Assert.Equal(0x1F, report[44]);
        Assert.Equal(2, report[43]);            // clamped: the pad only knows 0..2
    }

    [Fact]
    public void Motors_are_only_touched_when_asked_for()
    {
        // Rumble goes through evdev, so the game's reports leave the motor bytes alone. If this ever regresses,
        // every lightbar update would also stop whatever rumble was playing.
        byte[] quiet = DualSenseReports.BuildUsb(new DualSenseOutputState { ControlLightbar = true });
        Assert.Equal(0, quiet[1] & 0x01);
        Assert.Equal(0, quiet[3]);
        Assert.Equal(0, quiet[4]);

        byte[] buzzing = DualSenseReports.BuildUsb(new DualSenseOutputState
        {
            ControlMotors = true, MotorLeft = 0x55, MotorRight = 0x66,
        });
        Assert.Equal(0x01, buzzing[1] & 0x01);
        Assert.Equal(0x66, buzzing[3]);
        Assert.Equal(0x55, buzzing[4]);
    }

    // ---- bluetooth -----------------------------------------------------------------------------------------

    [Fact]
    public void Crc32_matches_the_reference_vector()
    {
        // The pad drops a Bluetooth report whose CRC is wrong, silently — so this is the check that says whether
        // a "lights do nothing over Bluetooth" bug is in the checksum or somewhere else.
        Assert.Equal(0xCBF43926u, DualSenseReports.Crc32("123456789"u8));
    }

    [Fact]
    public void Crc32_can_be_chained_across_two_calls()
    {
        uint chained = DualSenseReports.Crc32("1234"u8);
        chained = DualSenseReports.Crc32("56789"u8, chained);
        Assert.Equal(DualSenseReports.Crc32("123456789"u8), chained);
    }

    [Fact]
    public void Bluetooth_report_carries_the_header_the_same_payload_and_a_valid_crc()
    {
        var state = new DualSenseOutputState { ControlLightbar = true, Red = 1, Green = 2, Blue = 3 };
        byte[] bluetooth = DualSenseReports.BuildBluetooth(state, sequence: 3);
        byte[] usb = DualSenseReports.BuildUsb(state);

        Assert.Equal(78, bluetooth.Length);
        Assert.Equal(0x31, bluetooth[0]);
        Assert.Equal(0x30, bluetooth[1]);       // sequence in the high nibble
        Assert.Equal(0x10, bluetooth[2]);

        // Same 47-byte common block, just at a different offset.
        Assert.Equal(usb[1..48], bluetooth[3..50]);

        // The firmware checksums a 0xA2 seed byte followed by the report; recompute it the same way.
        uint expected = DualSenseReports.Crc32([0xA2]);
        expected = DualSenseReports.Crc32(bluetooth.AsSpan(0, 74), expected);
        Assert.Equal(expected, BitConverter.ToUInt32(bluetooth, 74));
    }

    [Fact]
    public void Bluetooth_sequence_only_uses_the_high_nibble()
    {
        // The counter wraps at 16; a caller incrementing a byte forever must not spill into the low nibble.
        Assert.Equal(0xF0, DualSenseReports.BuildBluetooth(new DualSenseOutputState(), 0xFF)[1]);
    }

    // ---- LED patterns --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0x00)]
    [InlineData(1, 0x04)]
    [InlineData(3, 0x0E)]
    [InlineData(5, 0x1F)]
    [InlineData(9, 0x1F)]       // more lives than LEDs
    [InlineData(-2, 0x00)]      // a negative count (the life gauge does go below zero)
    public void Player_led_patterns_are_symmetric_and_clamped(int lives, byte expected)
    {
        byte mask = DualSenseReports.PlayerLedsForLives(lives);
        Assert.Equal(expected, mask);
        Assert.Equal(0, mask & ~0x1F);
    }

    // ---- discovery -----------------------------------------------------------------------------------------

    [Fact]
    public void Scan_finds_a_pad_in_a_fabricated_sysfs_tree()
    {
        using var tree = new FakeSysfs();
        tree.AddDualSense("0003:054C:0CE6.0055", inputNumber: 157, eventNumber: 21, hidRaw: "hidraw6");

        DualSenseDeviceInfo device = Assert.Single(DualSenseDiscovery.Scan(tree.SysRoot, tree.DevRoot));
        Assert.False(device.Bluetooth);
        Assert.Equal("Sony Interactive Entertainment DualSense Wireless Controller", device.Name);
        Assert.Equal(Path.Combine(tree.DevRoot, "input", "event21"), device.EventDevice);
        Assert.Equal(Path.Combine(tree.DevRoot, "hidraw6"), device.HidRawDevice);
        Assert.Equal(Path.Combine(tree.SysRoot, "class", "leds", "input157:rgb:indicator"), device.LightbarPath);
        Assert.Equal(5, device.PlayerLedPaths.Count);
    }

    [Fact]
    public void Scan_picks_the_gamepad_node_not_the_motion_sensors_or_touchpad()
    {
        // The kernel splits the pad into four input devices. Binding rumble to the touchpad's event node would
        // fail at the ioctl with no obvious reason why, so the walk has to pick the right one.
        using var tree = new FakeSysfs();
        string device = tree.AddDualSense("0003:054C:0CE6.0055", inputNumber: 157, eventNumber: 21, hidRaw: "hidraw6");
        tree.AddExtraInput(device, 158, 22, "Sony Interactive Entertainment DualSense Wireless Controller Motion Sensors");
        tree.AddExtraInput(device, 159, 23, "Sony Interactive Entertainment DualSense Wireless Controller Touchpad");

        DualSenseDeviceInfo found = Assert.Single(DualSenseDiscovery.Scan(tree.SysRoot, tree.DevRoot));
        Assert.Equal(Path.Combine(tree.DevRoot, "input", "event21"), found.EventDevice);
    }

    [Fact]
    public void Scan_ignores_other_vendors_and_reports_bluetooth_pads_as_such()
    {
        using var tree = new FakeSysfs();
        tree.AddDualSense("0005:054C:0CE6.0003", inputNumber: 20, eventNumber: 5, hidRaw: "hidraw2");
        tree.AddDualSense("0003:045E:028E.0001", inputNumber: 30, eventNumber: 6, hidRaw: "hidraw3");   // an Xbox pad

        DualSenseDeviceInfo device = Assert.Single(DualSenseDiscovery.Scan(tree.SysRoot, tree.DevRoot));
        Assert.True(device.Bluetooth);
    }

    [Fact]
    public void Scan_survives_a_pad_with_no_leds_or_hidraw()
    {
        // A kernel without the playstation driver bound presents the pad as a bare HID gamepad: an event node
        // and nothing else. That must still be found (rumble may work) rather than throw.
        using var tree = new FakeSysfs();
        string device = tree.AddDualSense("0003:054C:0CE6.0001", inputNumber: 1, eventNumber: 3, hidRaw: null);
        Directory.Delete(Path.Combine(device, "leds"), recursive: true);

        DualSenseDeviceInfo found = Assert.Single(DualSenseDiscovery.Scan(tree.SysRoot, tree.DevRoot));
        Assert.Null(found.HidRawDevice);
        Assert.Null(found.LightbarPath);
        Assert.Empty(found.PlayerLedPaths);
        Assert.Equal(Path.Combine(tree.DevRoot, "input", "event3"), found.EventDevice);
    }

    [Fact]
    public void Scan_returns_nothing_when_there_is_no_sysfs_at_all()
    {
        // The Windows/Android/Switch case, and the one where /sys is not mounted.
        Assert.Empty(DualSenseDiscovery.Scan(Path.Combine(Path.GetTempPath(), "aag2-no-such-sysfs"), "/dev"));
    }

    [Theory]
    [InlineData("0003:054C:0CE6.0055", true)]
    [InlineData("0005:054C:0DF2.0001", true)]       // DualSense Edge over Bluetooth
    [InlineData("0003:054C:05C4.0001", false)]      // DualShock 4 — a different protocol entirely
    [InlineData("0003:045E:028E.0001", false)]      // Xbox 360 pad
    [InlineData("0018:054C:0CE6.0001", false)]      // neither USB nor Bluetooth (an I2C/virtual pad)
    [InlineData("nonsense", false)]
    public void Device_ids_are_matched_on_vendor_product_and_bus(string name, bool expected) =>
        Assert.Equal(expected, DualSenseDiscovery.IsDualSense(name, out _));

    // ---- button labels -------------------------------------------------------------------------------------

    [Fact]
    public void Buttons_are_labelled_the_way_the_pad_prints_them()
    {
        Assert.Equal("Cross", PadButtonNames.Describe(PadButton.RightFaceDown, playStationLayout: true));
        Assert.Equal("Square", PadButtonNames.Describe(PadButton.RightFaceLeft, playStationLayout: true));
        Assert.Equal("Options", PadButtonNames.Describe(PadButton.MiddleRight, playStationLayout: true));
        Assert.Equal("R1", PadButtonNames.Describe(PadButton.RightTrigger1, playStationLayout: true));

        // Without a DualSense the engine's own positional names are what a generic pad gets.
        Assert.Equal("RightFaceDown", PadButtonNames.Describe(PadButton.RightFaceDown, playStationLayout: false));
    }

    [Fact]
    public void Every_button_has_a_label_in_both_layouts()
    {
        foreach (PadButton button in Enum.GetValues<PadButton>())
        {
            Assert.False(string.IsNullOrWhiteSpace(PadButtonNames.PlayStation(button)));
            Assert.False(string.IsNullOrWhiteSpace(PadButtonNames.Generic(button)));
        }
    }

    [Fact]
    public void The_dualsense_layout_only_replaces_untouched_defaults()
    {
        // The one-time layout must never eat a binding the player chose, so it is gated on this check.
        var config = new Configuration();
        Assert.True(config.IsUsingDefaultBindings());

        config.BombButton = PadButton.LeftThumb;
        Assert.False(config.IsUsingDefaultBindings());
    }

    /// <summary>
    /// A throwaway /sys + /dev tree shaped like the real one: hid devices with input, leds and hidraw children.
    /// </summary>
    private sealed class FakeSysfs : IDisposable
    {
        private readonly string Root = Directory.CreateTempSubdirectory("aag2-dualsense-").FullName;

        public string SysRoot => Path.Combine(Root, "sys");
        public string DevRoot => Path.Combine(Root, "dev");

        /// <summary>Returns the HID device directory, so a test can add more inputs to it or take parts away.</summary>
        public string AddDualSense(string id, int inputNumber, int eventNumber, string? hidRaw)
        {
            string name = id.Contains("054C:0CE6") || id.Contains("054C:0DF2")
                ? "Sony Interactive Entertainment DualSense Wireless Controller"
                : "Some Other Controller";

            string device = Path.Combine(SysRoot, "bus", "hid", "devices", id);
            Directory.CreateDirectory(device);
            File.WriteAllText(Path.Combine(device, "uevent"),
                $"DRIVER=playstation\nHID_ID={id[..4]}:0000054C:00000CE6\nHID_NAME={name}\n");

            AddExtraInput(device, inputNumber, eventNumber, name, forceFeedback: true);

            foreach (string led in LedNames(inputNumber))
                Directory.CreateDirectory(Path.Combine(device, "leds", led));

            if (hidRaw is not null)
                Directory.CreateDirectory(Path.Combine(device, "hidraw", hidRaw));
            return device;
        }

        public void AddExtraInput(string device, int inputNumber, int eventNumber, string name,
            bool forceFeedback = false)
        {
            string input = Path.Combine(device, "input", $"input{inputNumber}");
            Directory.CreateDirectory(Path.Combine(input, $"event{eventNumber}"));
            Directory.CreateDirectory(Path.Combine(input, "capabilities"));
            File.WriteAllText(Path.Combine(input, "name"), name + "\n");
            // The real format: space-separated 64-bit words, most significant first. Only the gamepad node has
            // any bit set here.
            File.WriteAllText(Path.Combine(input, "capabilities", "ff"), forceFeedback ? "107030000 0\n" : "0 0\n");
        }

        private static IEnumerable<string> LedNames(int inputNumber)
        {
            yield return $"input{inputNumber}:rgb:indicator";
            for (int i = 1; i <= 5; i++)
                yield return $"input{inputNumber}:white:player-{i}";
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
        }
    }
}

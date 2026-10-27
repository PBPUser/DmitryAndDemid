# DualSense support

The Nikitos Engine reads a DualSense as an ordinary gamepad through whichever backend is running (GLFW/SDL under Raylib,
Silk.NET under the OpenGL and Vulkan backends) — buttons and sticks have always worked and are untouched by any
of this. What this feature adds is the hardware that generic path cannot see:

| Feature | How it is driven | Needs |
|---|---|---|
| Rumble | evdev force feedback on `/dev/input/eventN` | nothing — logind already gives the seat's user an ACL |
| Lightbar, player LEDs | HID output report on `/dev/hidrawN`, falling back to `/sys/class/leds` | the udev rule (or root) |
| Adaptive triggers | HID output report on `/dev/hidrawN` | the udev rule (or root) |
| PlayStation button names | none — it is just labelling | a DualSense being detected |

Everything is optional and independent. No pad, a pad without permissions, or a non-Linux platform each turn the
corresponding piece off and the game runs exactly as it did before.

## Granting access to the lightbar and triggers

Rumble works out of the box. The other two go through the raw HID node, which is root-only by default:

```bash
sudo cp Tools/99-dualsense.rules /etc/udev/rules.d/99-dualsense.rules
sudo udevadm control --reload-rules && sudo udevadm trigger --subsystem-match=hidraw
```

Then re-plug the pad (or re-pair it over Bluetooth). The rule tags the node with `uaccess`, which gives the
logged-in user an ACL on it — the same mechanism that already covers the event node. It does not add anyone to a
group and does not make the device world-writable.

## Checking it

```bash
dotnet bin/Debug/net10.0/aag2.dll --dualsense-test
```

Headless, like `--selftest`. It prints the nodes it found and which features are reachable, then buzzes both
motors, cycles the lightbar red/green/blue, counts the player LEDs up from 0 to 5, and stiffens both triggers for
four seconds. Anything unavailable is reported with the errno that made it so.

## What the game does with it

- **Lightbar** — cool blue during play, cyan while focused, red on the last life, dimmed while paused, and a slow
  violet breath in the menus. Death, bomb, extend and spell-card start each pulse a colour over the top.
- **Player LEDs** — the life count, filling outwards from the middle.
- **Adaptive triggers** — resistance follows the *bindings*, not the hardware: a trigger bound to focus gets a
  firm ledge, one bound to shoot a light give, one bound to bomb a heavy pull, and one bound to nothing stays
  free.
- **Rumble** — death (heavy), bomb, extend (light tick), spell-card start.
- **Buttons** — with a DualSense connected, the controller settings screen names buttons the way the pad does
  (Cross, Square, R1, Options) instead of the Nikitos Engine's positional names (RightFaceDown, …).

On first launch with a DualSense connected the game applies a DualSense layout — shoot on Cross, bomb on Square,
focus on R1, pause on Options. It does this **only** if the bindings are still the shipped defaults, so it can
never overwrite a layout the player chose; afterwards `GamepadProfile` in `config.json` records that the offer was
made. The controller settings screen can apply it again on demand, and the reset row puts the generic defaults
back.

Per-feature switches live in the controller settings screen (and in `config.json` as `DualSenseRumble`,
`DualSenseRumbleStrength`, `DualSenseLightbar`, `DualSenseTriggers`). Rows whose hardware is out of reach are
shown greyed out rather than as toggles that would do nothing.

## Code map

| File | What it holds |
|---|---|
| `Utils/DualSense/DualSenseDiscovery.cs` | walks `/sys/bus/hid/devices` to find the pad's event/hidraw/LED nodes |
| `Utils/DualSense/DualSenseReports.cs` | builds the USB (0x02) and Bluetooth (0x31 + CRC-32) output reports |
| `Utils/DualSense/EvdevRumble.cs` | force-feedback effect upload and playback |
| `Utils/DualSense/DualSenseHidRaw.cs` | writes output reports |
| `Utils/DualSense/SysfsLeds.cs` | the LED-class fallback for lights |
| `Utils/DualSense/DualSensePad.cs` | the facade: what is connected, what is reachable, what to send |
| `Utils/DualSense/DualSenseSelfTest.cs` | `--dualsense-test` |
| `Gameplay/DualSenseFeedback.cs` | maps game state and events onto the hardware |
| `Utils/PadButtonNames.cs` | PlayStation vs generic button labels |

The report layout, the CRC-32, the LED patterns and the discovery walk are covered by `Tests/DualSenseTests.cs`,
which needs no pad — it builds reports and points the sysfs walk at a fabricated directory tree.

## Notes and limits

- Linux only. Everything is gated behind `OperatingSystem.IsLinux()` and a sysfs walk that finds nothing
  elsewhere, so Windows/Android/Switch builds simply never turn it on. A Windows port would mean the same output
  reports over `hid.dll`, and rumble over XInput or the same reports.
- The Bluetooth report path is implemented (report 0x31, sequence tag, CRC-32) but has only been exercised over
  USB. A wrong CRC is dropped silently by the firmware, so the failure mode there is "lights do nothing".
- Rumble deliberately does *not* go through the HID report even when that node is open: the kernel's
  force-feedback path needs no permissions, so routing it there would make rumble need the udev rule too.
- The trigger effect encodings (`0x01` rigid, `0x02` pulse) are the community-documented simple modes. The pad
  ignores a mode it does not understand, so a wrong parameter reads as "no resistance", never as a hang.

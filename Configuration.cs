using DmitryAndDemid.Rendering;
using System.Text.Json;
using System.Text.Json.Serialization;
using DmitryAndDemid.Data;

namespace DmitryAndDemid;

public class Configuration
{
    public static Configuration Config;

    static Configuration()
    {
        Config = new Configuration();
        if (File.Exists(Utils.Platform.DataPath("config.json")))
            Config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(Utils.Platform.DataPath("config.json"))) ?? new Configuration();
    }

    [JsonInclude] public string Resolution = "1280x960";
    [JsonInclude] public FullScreenType FullScreenType = FullScreenType.Window;
    [JsonInclude] public bool AlwaysAsk = true;
    [JsonInclude] public float SFXVolume = 0.9f;
    [JsonInclude] public float MusicVolume = 1.0f;
    [JsonInclude] public bool FastLoading = false;
    [JsonInclude] public bool UseVSYNC = true;
    [JsonInclude] public int FrameCap = -1;

    /// <summary>"raylib" (default), "silk" or "vulkan". Override at launch with --renderer=&lt;name&gt;.</summary>
    [JsonInclude] public string Renderer = "raylib";

    /// <summary>
    /// Multiplies the raw gamepad stick reading. 1.0 is the stick as the driver reports it; higher values
    /// make it reach the movement threshold sooner (a "twitchier" stick), lower values make it slower.
    /// </summary>
    [JsonInclude] public float GamepadSensitivity = 1.0f;

    /// <summary>
    /// 32 or 16. NOT WIRED TO ANYTHING — every backend renders RGBA8 and presents to an 8-bit-per-channel
    /// swapchain. The setting is persisted and the configurator shows it (labelled as not working), but no
    /// renderer reads it. Honouring it would mean a 16-bit surface format (e.g. R5G6B5) per backend.
    /// </summary>
    [JsonInclude] public int ColorDepth = 32;

    /// <summary>
    /// On-screen touch controls: drag inside the playfield to move (with auto-fire), plus BOMB and FOCUS
    /// buttons. Off by default — they only make sense on a touchscreen.
    /// </summary>
    [JsonInclude] public bool TouchControls = false;

    /// <summary>
    /// Show a dedicated on-screen SHOOT button. It is additive: moving still auto-fires as before, and the
    /// button lets you also fire without moving (useful with the stick, or to fire while holding position).
    /// </summary>
    [JsonInclude] public bool TouchShootButton = true;

    /// <summary>
    /// Movement style for touch. False (default) is finger-follow: drag inside the playfield and the ship
    /// tracks the finger 1:1. True swaps that for a virtual analog stick placed in the UI strip.
    /// </summary>
    [JsonInclude] public bool TouchStick = false;

    /// <summary>
    /// When on, holding the shoot button also engages focus (slow movement) — so you can slow down for tight
    /// dodging without a separate focus key/button. Off by default; shooting and focus stay independent.
    /// </summary>
    [JsonInclude] public bool AutoSlowdownOnShoot = false;

    /// <summary>
    /// On-screen control positions, in the game's 640x480 design units (top-left corner of each control).
    /// The in-game layout editor (Screens/TouchLayoutScreen) writes these; the sizes live in TouchControls.
    /// </summary>
    [JsonInclude] public float TouchBombX = 424, TouchBombY = 404;
    [JsonInclude] public float TouchFocusX = 524, TouchFocusY = 404;
    [JsonInclude] public float TouchShootX = 474, TouchShootY = 336;
    [JsonInclude] public float TouchStickX = 452, TouchStickY = 150;

    /// <summary>
    /// Portrait ("vertical") presentation: the game renders into a tall backbuffer and the in-game layout
    /// stacks the playfield over the info panel, for a phone held upright. Off = the native 4:3 landscape.
    /// Applied at startup (the backbuffer and every layout are sized from it), so a change needs a restart.
    /// </summary>
    [JsonInclude] public bool Vertical = false;
    [JsonInclude] public PadButton ShootButton = PadButton.RightFaceDown;
    [JsonInclude] public PadButton BombButton = PadButton.RightFaceRight;
    [JsonInclude] public PadButton PauseButton = PadButton.RightTrigger1;
    [JsonInclude] public PadButton FocusButton = PadButton.RightFaceLeft;
    [JsonInclude] public PadButton JumpButton = PadButton.RightTrigger2;

    public void Save()
    {
        File.WriteAllText(Utils.Platform.DataPath("config.json"), JsonSerializer.Serialize(this));
    }
}

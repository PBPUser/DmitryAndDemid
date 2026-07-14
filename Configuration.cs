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
        if (File.Exists("config.json"))
            Config = JsonSerializer.Deserialize<Configuration>(File.ReadAllText("config.json")) ?? new Configuration();
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
    [JsonInclude] public PadButton ShootButton = PadButton.RightFaceDown;
    [JsonInclude] public PadButton BombButton = PadButton.RightFaceRight;
    [JsonInclude] public PadButton PauseButton = PadButton.RightTrigger1;
    [JsonInclude] public PadButton FocusButton = PadButton.RightFaceLeft;
    [JsonInclude] public PadButton JumpButton = PadButton.RightTrigger2;

    public void Save()
    {
        File.WriteAllText("config.json", JsonSerializer.Serialize(this));
    }
}

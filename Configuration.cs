using System.Text.Json;
using System.Text.Json.Serialization;
using DmitryAndDemid.Data;
using Raylib_cs;

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

    [JsonInclude]
    public string Resolution = "1280x960";
    [JsonInclude]
    public FullScreenType FullScreenType = FullScreenType.Window;
    [JsonInclude]
    public bool AlwaysAsk = true;
    [JsonInclude] public float SFXVolume = 0.9f;
    [JsonInclude] public float MusicVolume = 1.0f;
    [JsonInclude] public bool FastLoading = false;
    [JsonInclude] public GamepadButton ShootButton = GamepadButton.RightFaceDown;
    [JsonInclude] public GamepadButton BombButton = GamepadButton.RightFaceRight;
    [JsonInclude] public GamepadButton PauseButton = GamepadButton.RightTrigger1;
    [JsonInclude] public GamepadButton FocusButton = GamepadButton.RightFaceLeft;

    public void Save()
    {
        File.WriteAllText("config.json", JsonSerializer.Serialize(this));
    }
}

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

    [JsonInclude]
    public string Resolution = "1280x960";
    [JsonInclude]
    public FullScreenType FullScreenType = FullScreenType.Window;
    [JsonInclude]
    public bool AlwaysAsk = true;
    [JsonInclude] public float SFXVolume = 0.9f;
    [JsonInclude] public float MusicVolume = 1.0f;

    [JsonInclude] public bool FastLoading = false;

    public void Save()
    {
        File.WriteAllText("config.json", JsonSerializer.Serialize(this));
    }
}

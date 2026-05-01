using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DmitryAndDemid.Data;

public class Replay
{
    public Replay(byte[] data, ReplayJson json)
    {
        Data = data;
        Information = json;
    }
    
    public static Replay Load(string file)
    {
        return Load(File.ReadAllBytes(file));
    }

    public static Replay Load(byte[] file)
    {
        int infoLength = BitConverter.ToInt32(file, 0);
        int dataLength = BitConverter.ToInt32(file, 4);
        byte[] header = new byte[infoLength];
        byte[] data = new byte[dataLength];
        Array.Copy(file, 8, header, 0, infoLength);
        Array.Copy(file, 0, data, 8+infoLength, dataLength);
        return new Replay(data,JsonSerializer.Deserialize<ReplayJson>(Encoding.UTF8.GetString(header)));
    }

    public static ReplayJson? ReadHeader(string fileName)
    {
        byte[] file = System.IO.File.ReadAllBytes(fileName);
        int infoLength = BitConverter.ToInt32(file, 0);
        return JsonSerializer.Deserialize<ReplayJson>(Encoding.UTF8.GetString(file, 4, infoLength));
    }

    public void Save(string fileName)
    {
        string dir = String.Join('/', fileName.Replace("\\", "/").Split("/")[0..^1]);
        if(!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(fileName, Export());
    }

    public byte[] Export()
    {
        string json = JsonSerializer.Serialize(Information);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        byte[] dataLength = BitConverter.GetBytes(Data.Length);
        byte[] jsonLength = BitConverter.GetBytes(jsonBytes.Length);
        byte[] data = new byte[8 + jsonBytes.Length + Data.Length];
        Array.Copy(dataLength, 0, data, 0, 4);
        Array.Copy(jsonLength, 0, data, 4, 4);
        Array.Copy(jsonBytes, 0, data, 8, jsonBytes.Length);
        Array.Copy(Data, 0, data, 8+jsonBytes.Length, Data.Length);
        return data;
    }

    public ReplayJson Information;
    public byte[] Data;
    
    public class ReplayJson
    {
        [JsonInclude] public string Nickname = "";
        [JsonInclude] public string Stage = "";
        [JsonInclude] public int Difficulty = 0;
        [JsonInclude] public string Person = "";
        [JsonInclude] public DateTime Timestamp = new DateTime();
        [JsonInclude] public string Slowdown = "0.0";
        [JsonInclude] public ReplayStageInfo[] ReplayStageInfo = new ReplayStageInfo[0];
    }
}
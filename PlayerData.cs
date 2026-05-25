using DmitryAndDemid.Utils;

namespace DmitryAndDemid;

public class PlayerData
{
    private const string FileName = "scoreaag2.gsy";
    public static PlayerData Instance { get; } = new PlayerData();
    public string LastName = "";
    long Flags = 0;
    long Nicknames = 0;
    long Music = 0;
    public Dictionary<string, PersonPlayerData> Persons = new();

    public bool IsMusicUnlocked(int i)
    {
        long a = 1 << i;
        return (Music & a) == a;
    }

    public void SetMusicUnlocked(int i, bool state)
    {
        long a = 1 << i;
        if (state)
            Music |= a;
        else
            Music &= ~a;
        Save();
    }
    
    
    public bool IsNicknameUnlocked(int i)
    {
        long a = 1 << i;
        return (Nicknames & a) == a;
    }

    public void SetNicknameUnlocked(int i, bool state)
    {
        long a = 1 << i;
        if (state)
            Nicknames |= a;
        else
            Nicknames &= ~a;
        Save();
    }

    public bool IsExtraUnlocked
    {
        get => (Flags & 0x1) == 0x1;
        set
        {
            if(value)
                Flags |= 0x1;
            else
                Flags &= ~0x1;
            Save();
        }
    }

    public bool IsStageUnlocked(int i)
    {
        long a = 2 << i;
        return (Flags & a) == a;
    }

    public void SetStageUnlocked(int stage,  bool state)
    {
        long a = 2 << stage;
        if(state)
            Flags |= a;
        else
            Flags &= ~a;
        Save();
    }
    
    static PlayerData()
    {
        if (File.Exists(FileName))
            Instance = Load();
    }

    static PlayerData Load()
    {
        BitPackage package = BitPackage.OpenStreamReadPackage(FileName);
        PlayerData data = new PlayerData();
        data.LastName = package.ReadString();
        data.Flags = package.ReadVarLong();
        data.Nicknames =  package.ReadVarLong();
        data.Music = package.ReadVarLong();
        var length = package.ReadVarLong();
        for (int i = 0; i < length; i++)
            data.Persons[package.ReadString()] = PersonPlayerData.ReadFromPackage(ref package);
        package.Dispose();
        return data;
    }
    
    public void Save()
    {
        BitPackage package = BitPackage.OpenStreamWritePackage(FileName);
        package.WriteString(LastName);
        package.WriteVarLong(Flags);
        package.WriteVarLong(Nicknames);
        package.WriteVarLong(Music);
        package.WriteVarLong(Persons.Count);
        var arr = Persons.ToArray();
        for (int i = 0; i < Persons.Count; i++)
        {
            package.WriteString(arr[i].Key);
            SaveTries(ref package, arr[i].Value.SpellcardTries);
            SaveTries(ref package, arr[i].Value.SpellcardPracticesTries);
            for(int j = 0; j < 10; j++)
                arr[i].Value.MainScoreRecords[j].WriteToPackage(ref package);
            for(int j = 0; j < 10; j++)
                arr[i].Value.ExtraScoreRecords[j].WriteToPackage(ref package);
        }
        package.Dispose();
    }

    static void SaveTries(ref BitPackage package, Dictionary<string, (int total, int success)> tries)
    {
        package.WriteVarLong(tries.Count);
        foreach (var keyValuePair in tries)
        {
            package.WriteString(keyValuePair.Key);
            package.WriteVarLong(keyValuePair.Value.total);
            package.WriteVarLong(keyValuePair.Value.success);
        }
    }

    static Dictionary<string, (int total, int success)> LoadTries(ref BitPackage package)
    {
        Dictionary<string, (int total, int success)> tries = new();
        int length = (int)package.ReadVarLong();
        for (int i = 0; i < length; i++)
            tries[package.ReadString()] = ((int)package.ReadVarLong(), (int)package.ReadVarLong());
        return tries;
    }
    
    public class PersonPlayerData
    {
        public PersonPlayerData()
        {
            for (int i = 0; i < 10; i++)
            {
                MainScoreRecords[i] = new("--------", (int)Math.Pow(10, 10 - i), -1, -1, -1);
                ExtraScoreRecords[i] = new("--------", (int)Math.Pow(10, 10 - i), -1, -1, -1);
            }
        }
        
        public Dictionary<string, (int, int)> SpellcardTries = new();
        public Dictionary<string, (int, int)> SpellcardPracticesTries = new();
        public PersonPlayerScoreRecord[] MainScoreRecords = new PersonPlayerScoreRecord[10];
        public PersonPlayerScoreRecord[] ExtraScoreRecords = new PersonPlayerScoreRecord[10];

        public static PersonPlayerData ReadFromPackage(ref BitPackage package)
        {
            PersonPlayerData data = new();
            data.SpellcardTries = LoadTries(ref package);
            data.SpellcardPracticesTries = LoadTries(ref package);
            for(int i = 0; i < 10; i++)
                data.MainScoreRecords[i] = PersonPlayerScoreRecord.ReadFromPackage(ref package);
            for(int i = 0; i < 10; i++)
                data.ExtraScoreRecords[i] = PersonPlayerScoreRecord.ReadFromPackage(ref package);
            return data;
        }
    }

    public class PersonPlayerScoreRecord(string name, int score, int date, int stage, float percentage)
    {
        public string Name = name;
        public int Score = score;
        public int Date = date;
        public int Stage = stage;
        public float Percentage = percentage;

        public void WriteToPackage(ref BitPackage package)
        {
            package.WriteString(Name);
            package.WriteVarLong(Score);
            package.WriteVarLong(Date);
            package.WriteVarLong(Stage);
            package.WriteFloat(Percentage);
        }

        public static PersonPlayerScoreRecord ReadFromPackage(ref BitPackage package)
        {
            return new(
                package.ReadString(),
                (int)package.ReadVarLong(),
                (int)package.ReadVarLong(),
                (int)package.ReadVarLong(),
                package.ReadFloat()
            );
        }
    }
}
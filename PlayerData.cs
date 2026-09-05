using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid;

public class PlayerData
{
    private static string FileName => Utils.Platform.DataPath("scoreaag2.gsy");
    public static PlayerData Instance { get; } = new PlayerData();
    public string LastName = "";
    long Flags = 0;
    long Nicknames = 0;
    long Music = 0;
    long Trophies = 0;
    public Dictionary<string, PersonPlayerData> Persons = new();

    /// <summary>Whether trophy <paramref name="i"/> has been earned; locked trophies read as ** in the list.</summary>
    public bool IsTrophyUnlocked(int i)
    {
        long a = 1L << i;
        return (Trophies & a) == a;
    }

    public void SetTrophyUnlocked(int i, bool state)
    {
        long a = 1L << i;
        // Only a genuine locked → unlocked transition is worth feeling. Several of these setters are called
        // unconditionally on things that are usually already unlocked (the game-over theme, every run), so
        // firing on the call rather than on the change would buzz the pad for nothing.
        bool isNew = state && (Trophies & a) != a;
        if (state)
            Trophies |= a;
        else
            Trophies &= ~a;
        if (isNew)
            Gameplay.DualSenseFeedback.OnUnlock();
        Save();
    }

    public bool IsMusicUnlocked(int i)
    {
        if (i == 0)
            return true;   // the title theme (song 0) is always available
        long a = 1 << i;
        return (Music & a) == a;
    }

    public void SetMusicUnlocked(int i, bool state)
    {
        long a = 1 << i;
        bool isNew = state && (Music & a) != a;
        if (state)
            Music |= a;
        else
            Music &= ~a;
        if (isNew)
            Gameplay.DualSenseFeedback.OnUnlock();
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
        bool isNew = state && (Nicknames & a) != a;
        if (state)
            Nicknames |= a;
        else
            Nicknames &= ~a;
        if (isNew)
            Gameplay.DualSenseFeedback.OnUnlock();
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

    /// <summary>
    /// Switches on every unlock the save can hold: Extra, all the stages, all the music, all the trophies, all
    /// the nicknames. What the stats screen's cheat code grants (see <see cref="Screens.ScoreScreen"/>).
    ///
    /// The masks are set here rather than by looping the individual setters because each of THOSE writes the
    /// save file; this writes it once. The mask is 40 bits — far wider than the dozen-odd trophies, songs and
    /// stages that exist (so anything added later comes already unlocked) and comfortably positive, which keeps
    /// it off <see cref="BitPackage.WriteVarLong"/>'s negative path.
    /// </summary>
    public void UnlockEverything()
    {
        const long all = (1L << 40) - 1;
        Flags |= all;
        Nicknames |= all;
        Music |= all;
        Trophies |= all;
        Save();
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
    
    /// <summary>
    /// A spell card's record for one character: (attempts, captures). Spell practice keeps its own counters,
    /// separate from the ones earned in a real run — that is what SpellcardPracticesTries is for.
    /// </summary>
    public (int Total, int Success) GetSpellcardRecord(string personId, string spellName, bool practice)
    {
        if (string.IsNullOrEmpty(spellName) || personId == null)
            return (0, 0);
        if (!Persons.TryGetValue(personId, out PersonPlayerData? person))
            return (0, 0);
        Dictionary<string, (int, int)> tries = practice ? person.SpellcardPracticesTries : person.SpellcardTries;
        return tries.TryGetValue(spellName, out (int, int) record) ? record : (0, 0);
    }

    /// <summary>The player's best recorded score on a spell card (keyed by <see cref="Helper.SpellRecordKey"/>),
    /// or 0 if never scored.</summary>
    public int GetSpellcardBestScore(string personId, string spellName)
    {
        if (string.IsNullOrEmpty(spellName) || personId == null)
            return 0;
        if (!Persons.TryGetValue(personId, out PersonPlayerData? person))
            return 0;
        return person.SpellcardBestScores.TryGetValue(spellName, out int score) ? score : 0;
    }

    /// <summary>Records a spell-card score, keeping only the best. No-op for non-positive scores.</summary>
    public void RecordSpellcardScore(string personId, string spellName, int score)
    {
        if (string.IsNullOrEmpty(spellName) || string.IsNullOrEmpty(personId) || score <= 0)
            return;
        if (!Persons.TryGetValue(personId, out PersonPlayerData? person))
            Persons[personId] = person = new PersonPlayerData();
        if (!person.SpellcardBestScores.TryGetValue(spellName, out int existing) || score > existing)
        {
            person.SpellcardBestScores[spellName] = score;
            Save();
        }
    }

    /// <summary>Counts one finished attempt on a card, and a capture with it if the player pulled it off.</summary>
    public void AddSpellcardTry(string personId, string spellName, bool captured, bool practice)
    {
        if (string.IsNullOrEmpty(spellName) || string.IsNullOrEmpty(personId))
            return;
        if (!Persons.TryGetValue(personId, out PersonPlayerData? person))
            Persons[personId] = person = new PersonPlayerData();
        Dictionary<string, (int, int)> tries = practice ? person.SpellcardPracticesTries : person.SpellcardTries;
        (int total, int success) = tries.TryGetValue(spellName, out (int, int) record) ? record : (0, 0);
        tries[spellName] = (total + 1, success + (captured ? 1 : 0));
        Save();
    }

    /// <summary>The stored record for a character, or null if it has never been played.</summary>
    public PersonPlayerData? GetPerson(string personId) =>
        personId != null && Persons.TryGetValue(personId, out PersonPlayerData? p) ? p : null;

    /// <summary>The stored record for a character, creating (but not yet saving) an empty one if absent.</summary>
    public PersonPlayerData GetOrCreatePerson(string personId)
    {
        if (!Persons.TryGetValue(personId, out PersonPlayerData? p))
            Persons[personId] = p = new PersonPlayerData();
        return p;
    }

    /// <summary>
    /// Records one finished run for a character: bumps games-played, wins (when cleared) and time-spent, and
    /// inserts the score into that difficulty's best-ten board. Difficulty is 0..DifficultyCount-1
    /// (Easy..Extra); out-of-range difficulties are ignored. Persists immediately, like the other setters.
    /// </summary>
    public void RecordGame(string personId, int difficulty, int score, bool won, long durationSeconds,
        int stage, float percentage, string? name = null)
    {
        if (string.IsNullOrEmpty(personId) ||
            difficulty < 0 || difficulty >= PersonPlayerData.DifficultyCount)
            return;
        PersonPlayerData person = GetOrCreatePerson(personId);
        person.GamesPlayed++;
        if (won)
            person.Wins++;
        person.TimeSpentSeconds += Math.Max(0, durationSeconds);
        InsertScore(person.ScoreRecords[difficulty],
            new PersonPlayerScoreRecord(name ?? LastName, score, TodayInt(), stage, percentage));
        Save();
    }

    /// <summary>Today's date packed as YYYYMMDD, the encoding the score board's date column reads.</summary>
    static int TodayInt()
    {
        DateTime now = DateTime.Now;
        return now.Year * 10000 + now.Month * 100 + now.Day;
    }

    /// <summary>Inserts a record into a high-to-low board, shifting the tail down and dropping the last entry.</summary>
    static void InsertScore(PersonPlayerScoreRecord[] board, PersonPlayerScoreRecord record)
    {
        int at = Array.FindIndex(board, r => record.Score > r.Score);
        if (at < 0)
            return;   // did not beat any existing entry
        for (int i = board.Length - 1; i > at; i--)
            board[i] = board[i - 1];
        board[at] = record;
    }

    static PlayerData()
    {
        if (File.Exists(FileName))
        {
            // A save written by an older layout (before per-difficulty boards and the games/wins/time counters)
            // would read back misaligned; fall back to a fresh record rather than bricking startup.
            try { Instance = Load(); }
            catch { Instance = new PlayerData(); }
        }
    }

    static PlayerData Load()
    {
        BitPackage package = BitPackage.OpenStreamReadPackage(FileName);
        PlayerData data = new PlayerData();
        data.LastName = package.ReadString();
        data.Flags = package.ReadVarLong();
        data.Nicknames =  package.ReadVarLong();
        data.Music = package.ReadVarLong();
        data.Trophies = package.ReadVarLong();
        var length = package.ReadVarLong();
        for (int i = 0; i < length; i++)
            data.Persons[package.ReadString()] = PersonPlayerData.ReadFromPackage(ref package);
        // Optional trailing block: per-card best scores (see Save). Older saves end above, so stop cleanly at EOF
        // and just leave the best-score maps empty rather than discarding the whole (otherwise valid) save.
        try
        {
            long bn = package.ReadVarLong();
            for (int i = 0; i < bn; i++)
            {
                string id = package.ReadString();
                int count = (int)package.ReadVarLong();
                var scores = new Dictionary<string, int>();
                for (int j = 0; j < count; j++)
                    scores[package.ReadString()] = (int)package.ReadVarLong();
                if (data.Persons.TryGetValue(id, out PersonPlayerData? p))
                    p.SpellcardBestScores = scores;
            }
        }
        catch (EndOfStreamException ) { /* pre-best-score save: nothing more to read */ }
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
        package.WriteVarLong(Trophies);
        package.WriteVarLong(Persons.Count);
        var arr = Persons.ToArray();
        for (int i = 0; i < Persons.Count; i++)
        {
            package.WriteString(arr[i].Key);
            SaveTries(ref package, arr[i].Value.SpellcardTries);
            SaveTries(ref package, arr[i].Value.SpellcardPracticesTries);
            package.WriteVarLong(arr[i].Value.GamesPlayed);
            package.WriteVarLong(arr[i].Value.Wins);
            package.WriteVarLong(arr[i].Value.TimeSpentSeconds);
            for (int d = 0; d < PersonPlayerData.DifficultyCount; d++)
                for (int j = 0; j < PersonPlayerData.ScoresPerDifficulty; j++)
                    arr[i].Value.ScoreRecords[d][j].WriteToPackage(ref package);
        }
        // Trailing block: per-card best scores, keyed by person id. Appended after everything else so a reader
        // built before this feature simply stops at EOF (see Load) and keeps the rest of the save intact.
        package.WriteVarLong(Persons.Count);
        for (int i = 0; i < Persons.Count; i++)
        {
            package.WriteString(arr[i].Key);
            package.WriteVarLong(arr[i].Value.SpellcardBestScores.Count);
            foreach (var kv in arr[i].Value.SpellcardBestScores)
            {
                package.WriteString(kv.Key);
                package.WriteVarLong(kv.Value);
            }
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

    private const string DEFAULT_NICKNAME = "--------";
    
    public class PersonPlayerData
    {
        /// <summary>Easy, Normal, Hard, Max, Extra — the difficulties the difficulty menu offers, in order.</summary>
        public const int DifficultyCount = 5;
        public const int ScoresPerDifficulty = 10;

        public PersonPlayerData()
        {
            for (int d = 0; d < DifficultyCount; d++)
            {
                ScoreRecords[d] = new PersonPlayerScoreRecord[ScoresPerDifficulty];
                for (int i = 0; i < ScoresPerDifficulty; i++)
                    ScoreRecords[d][i] = new(DEFAULT_NICKNAME, (int)Math.Pow(10, 10 - i), -1, -1, -1);
            }
        }

        /// <summary>Best <see cref="ScoresPerDifficulty"/> scores per difficulty (indexed 0..DifficultyCount-1),
        /// each kept sorted high-to-low. Replaces the old flat Main/Extra pair so the score menu can show a
        /// separate board per difficulty.</summary>
        public PersonPlayerScoreRecord[][] ScoreRecords = new PersonPlayerScoreRecord[DifficultyCount][];
        /// <summary>Total finished runs with this character (any difficulty).</summary>
        public int GamesPlayed = 0;
        /// <summary>Cleared runs with this character.</summary>
        public int Wins = 0;
        /// <summary>Total wall time spent in runs with this character, in whole seconds.</summary>
        public long TimeSpentSeconds = 0;
        public Dictionary<string, (int, int)> SpellcardTries = new();
        public Dictionary<string, (int, int)> SpellcardPracticesTries = new();
        /// <summary>Best score achieved on each spell card, keyed by <see cref="Helper.SpellRecordKey"/> (which
        /// encodes the difficulty). Written from spell practice; shown as the card's hi-score on its difficulty
        /// screen. Persisted as an optional trailing block so older saves (without it) still load.</summary>
        public Dictionary<string, int> SpellcardBestScores = new();

        public static PersonPlayerData ReadFromPackage(ref BitPackage package)
        {
            PersonPlayerData data = new();
            data.SpellcardTries = LoadTries(ref package);
            data.SpellcardPracticesTries = LoadTries(ref package);
            data.GamesPlayed = (int)package.ReadVarLong();
            data.Wins = (int)package.ReadVarLong();
            data.TimeSpentSeconds = package.ReadVarLong();
            for (int d = 0; d < DifficultyCount; d++)
                for (int i = 0; i < ScoresPerDifficulty; i++)
                    data.ScoreRecords[d][i] = PersonPlayerScoreRecord.ReadFromPackage(ref package);
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
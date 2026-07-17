using System.Linq;
using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Data.Archive;

public class FileStageInfo
{
    /// <summary>
    /// The packed campaign stages under <c>Assets/Data/SpellCards</c>: filtered to <c>.sid</c> and ordinally
    /// sorted. This is the ONE source of truth for "which files are playable stages", and every gameplay entry
    /// point (main story, replay/demo, practice, the bench) must go through it.
    ///
    /// The <c>.sid</c> filter is not cosmetic. That folder can also hold non-stage files — editor scratch, an
    /// OS artifact like <c>.DS_Store</c>, a stray unpacked asset — and feeding one of those to <see cref="Load"/>
    /// throws deep inside <see cref="BitPackage"/>. Practice already filtered this way and never crashed; the
    /// main game, demo and bench enumerated with pattern "*" and took [0], so a single non-<c>.sid</c> file that
    /// sorted first was loaded as a "stage" and crashed the run — the "playing main story crashes" bug, which
    /// only ever reproduced where such a file existed (hence not on a clean desktop checkout).
    /// </summary>
    public static string[] CampaignStagePaths() =>
        Assets.Files("Assets/Data/SpellCards", "*.sid").OrderBy(x => x, StringComparer.Ordinal).ToArray();

    public FileStageInfo()
    {
        Scripts = new string[0];
        Chapters = new FileChapterInfo[0];
        Entities = new FileEntityInfo[0];
        Backgrounds = new string[0];
    }

    public FileStageInfo(FileStageInfo other)
    {
        Scripts = other.Scripts;
        Chapters = other.Chapters;
        Entities = other.Entities;
        Backgrounds = other.Backgrounds;
        Header = other.Header;
    }
    
    public int[] Header = new int[16];
    public string[] Scripts;
    public FileChapterInfo[] Chapters;
    public FileEntityInfo[] Entities;
    public string[] Backgrounds;

    /// <summary>
    /// Loads a stage / spell card from either a packed binary <c>.sid</c> or a human-editable <c>.json</c>
    /// file, chosen by the file extension. Lets any caller holding a path accept the JSON form transparently;
    /// both routes yield an identical in-memory object (see <see cref="StageJson"/>).
    /// </summary>
    public static FileStageInfo LoadFromFile(string path)
    {
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return StageJson.Load(path);
        BitPackage package = BitPackage.OpenStreamReadPackage(path);
        try { return Load(ref package); }
        finally { package.Dispose(); }
    }

    public static FileStageInfo Load(ref BitPackage bitPackage)
    {
        FileStageInfo info = new FileStageInfo();
        for (int i = 0; i < 16; i++)
            info.Header[i] = (int)bitPackage.ReadVarLong();
        info.Scripts = new string[info.Header[0x3]];
        for(int i = 0; i < info.Header[0x3]; i++)
            info.Scripts[i] = bitPackage.ReadString();
        info.Backgrounds = new string[info.Header[0x4]];
        for(int i = 0; i < info.Header[0x4]; i++)
            info.Backgrounds[i] = bitPackage.ReadString();
        info.Entities = new FileEntityInfo[info.Header[0x5]];
        for (int i = 0; i < info.Header[0x5]; i++)
            info.Entities[i] = FileEntityInfo.Load(ref bitPackage);
        info.Chapters = new FileChapterInfo[info.Header[0x6]];
        for(int i = 0; i < info.Header[0x6]; i++)
            info.Chapters[i] = FileChapterInfo.Load(ref bitPackage);
        return info;
    }

    /// <summary>
    /// Writes the collection counts into <see cref="Header"/> (Scripts/Backgrounds/Entities/Chapters), the
    /// values <see cref="Load"/> reads back to size those arrays. Called at the top of <see cref="Save"/> and by
    /// the JSON importer so a hand-authored stage — where the arrays are the source of truth — gets a matching
    /// <see cref="Header"/> without the author having to keep the counts in sync by hand.
    /// </summary>
    public void SyncHeader()
    {
        Header[3] = Scripts.Length;
        Header[4] = Backgrounds.Length;
        Header[5] = Entities.Length;
        Header[6] = Chapters.Length;
    }

    public void Save(ref BitPackage bitPackage)
    {
        SyncHeader();
        for(int i = 0; i < 16; i++)
            bitPackage.WriteVarLong(Header[i]);
        for(int i = 0; i < Scripts.Length; i++)
            bitPackage.WriteString(Scripts[i]);
        for(int i = 0; i < Backgrounds.Length; i++)
            bitPackage.WriteString(Backgrounds[i]);
        for(int i = 0; i < Entities.Length; i++)
            Entities[i].Save(ref bitPackage);
        for(int i = 0; i < Chapters.Length; i++)
            Chapters[i].Save(ref bitPackage);
    }
}
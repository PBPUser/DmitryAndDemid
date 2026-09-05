using System.Text.Json;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Gameplay.RuntimeData;
using DmitryAndDemid.Utils;
using Xunit;

namespace DmitryAndDemid.Tests;

/// <summary>
/// Contract tests for the packed stages under <c>Assets/Data/SpellCards</c>. Everything here is the kind of
/// mistake that only shows up when the stage is actually played — a chapter naming a script
/// <see cref="ActionsScope"/> does not define throws on load, a card pointing at a background or shader that
/// was never added draws nothing (or throws), a spell title with no translation shows its raw key.
///
/// All of it is checked headlessly: stages are read through <see cref="BitPackage"/>, visuals are matched
/// against the JSON on disk rather than the GPU-side dictionaries, and translation.json is parsed directly —
/// no window, no GL context. (Deliberately NOT touching <c>BulletRenderingInfo</c> or <c>Helper</c>: both reach
/// into <c>Runtime</c> statics, i.e. the GPU.)
/// </summary>
public class StageDataTests
{
    public StageDataTests() => TestEnvironment.UseRepoAssets();

    private static IEnumerable<(string Path, FileStageInfo Stage)> Stages()
    {
        foreach (string path in Assets.Files("Assets/Data/SpellCards", "*.sid"))
        {
            BitPackage package = BitPackage.OpenStreamReadPackage(path);
            FileStageInfo stage = FileStageInfo.Load(ref package);
            package.Dispose();
            yield return (Path.GetFileName(path), stage);
        }
    }

    public static TheoryData<string> StagePaths()
    {
        var data = new TheoryData<string>();
        foreach (string path in Assets.Files("Assets/Data/SpellCards", "*.sid"))
            data.Add(Path.GetFileName(path));
        return data;
    }

    private static FileStageInfo Load(string fileName)
    {
        BitPackage package = BitPackage.OpenStreamReadPackage(
            Assets.Resolve(Path.Combine("Assets/Data/SpellCards", fileName)));
        FileStageInfo stage = FileStageInfo.Load(ref package);
        package.Dispose();
        return stage;
    }

    /// <summary>
    /// Chapter script names that are known to resolve to nothing. <see cref="RuntimeChapter"/> looks these up
    /// with ContainsKey, so an unknown name is a silent no-op rather than a crash — which is exactly why they
    /// rot unnoticed. The one entry here is stage 1's spell 1, which drives its whole attack from its create
    /// script; its update name has never existed. Listed rather than ignored so the check still covers
    /// everything else, and so cleaning it up is a deliberate act (it needs stage1.sid recompiled).
    /// </summary>
    private static readonly string[] KnownDeadChapterScripts = ["nikitos#spell1#easy"];

    [Fact]
    public void Every_chapter_script_is_defined_in_ActionsScope()
    {
        var missing = new List<string>();
        foreach ((string file, FileStageInfo stage) in Stages())
            foreach (FileChapterInfo chapter in stage.Chapters)
            {
                if (chapter.UseCreateScript && !ActionsScope.ChapterActions.ContainsKey(chapter.CreateScript))
                    missing.Add($"{file}:{chapter.Id} create '{chapter.CreateScript}'");
                if (chapter.UseUpdateScript && !string.IsNullOrEmpty(chapter.UpdateScript)
                                            && !ActionsScope.ChapterActions.ContainsKey(chapter.UpdateScript)
                                            && !KnownDeadChapterScripts.Contains(chapter.UpdateScript))
                    missing.Add($"{file}:{chapter.Id} update '{chapter.UpdateScript}'");
            }

        Assert.True(missing.Count == 0, "Chapter scripts with no ActionsScope entry: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_entity_script_is_defined_in_ActionsScope()
    {
        // RuntimeObject.LoadFromFile indexes ObjectActions with the update script unconditionally, so a name
        // with no entry is a KeyNotFoundException the moment the entity is spawned.
        var missing = new List<string>();
        foreach ((string file, FileStageInfo stage) in Stages())
            foreach (FileEntityInfo entity in stage.Entities)
            {
                string where = $"{file}:entity {entity.Header[3]}";
                if (!string.IsNullOrEmpty(entity.UpdateScript)
                    && !ActionsScope.ObjectActions.ContainsKey(entity.UpdateScript))
                    missing.Add($"{where} update '{entity.UpdateScript}'");
                if (entity.UseCreateScript && !ActionsScope.ObjectActions.ContainsKey(entity.CreateScript))
                    missing.Add($"{where} create '{entity.CreateScript}'");
                if (entity.UseRemoveScript && !ActionsScope.ObjectActions.ContainsKey(entity.RemoveScript))
                    missing.Add($"{where} remove '{entity.RemoveScript}'");
                if (entity.UseDieScript && !entity.IsBullet
                                        && !ActionsScope.ObjectActions.ContainsKey(entity.DieScript))
                    missing.Add($"{where} die '{entity.DieScript}'");
            }

        Assert.True(missing.Count == 0, "Entity scripts with no ActionsScope entry: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_entity_visual_exists()
    {
        var bulletVisuals = Assets.Files("Assets/Data/BulletVisuals", "*.json")
            .Select(p => Path.GetFileNameWithoutExtension(p)!).ToHashSet();
        var missing = new List<string>();
        foreach ((string file, FileStageInfo stage) in Stages())
            foreach (FileEntityInfo entity in stage.Entities)
            {
                if (string.IsNullOrEmpty(entity.Visual))
                    continue;   // an unused table slot; nothing ever spawns it
                bool known = entity.IsBullet ? bulletVisuals.Contains(entity.Visual)
                    : EntityVisual.Visuals.ContainsKey(entity.Visual);
                if (!known)
                    missing.Add($"{file}:entity {entity.Header[3]} visual '{entity.Visual}'");
            }

        Assert.True(missing.Count == 0, "Entity visuals with no preset: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_spell_card_background_and_shader_exists()
    {
        var missing = new List<string>();
        foreach ((string file, FileStageInfo stage) in Stages())
            foreach (FileChapterInfo chapter in stage.Chapters)
            {
                if ((ChapterType)chapter.Header[0] == ChapterType.Spell
                    && !Assets.Exists(Path.Combine("Assets/Textures", chapter.SpellcardTexture)))
                    missing.Add($"{file}:{chapter.Id} background '{chapter.SpellcardTexture}'");
                if (chapter.ApplyShader
                    && !Assets.Exists(Path.Combine("Assets/Shaders", chapter.SpellcardShader + ".fs")))
                    missing.Add($"{file}:{chapter.Id} shader '{chapter.SpellcardShader}'");
            }

        Assert.True(missing.Count == 0, "Spell cards pointing at missing assets: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Spell cards are numbered continuously across the whole game (campaign stages first, then Extra — the
    /// order <c>SpellPracticeScreen</c> builds), and the number is how translation.json is keyed. A card needs
    /// a name for at least one tier (a tier it has no entry for is legitimately "this card is not played at
    /// that difficulty" — stage 2's colour-spam toilets exist only on Hard and Max), and an Extra card needs
    /// the "extra" tier specifically, since that is the only tier Extra mode plays. Without it the card shows
    /// its raw key ("spell.card.7") on screen.
    /// </summary>
    [Fact]
    public void Every_spell_card_number_has_a_name()
    {
        var translations = JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Assets.Resolve("Assets/Data/translation.json")))!;

        bool HasName(int number, string tier)
        {
            string prefix = $"spell.card.{number}.";
            return translations.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal)
                                              && k[prefix.Length..].Split('_').Contains(tier));
        }

        string[] ordered = [..FileStageInfo.CampaignStagePaths(), ..FileStageInfo.ExtraStagePaths()];
        var missing = new List<string>();
        int number = 0;
        foreach (string path in ordered)
        {
            FileStageInfo stage = Load(Path.GetFileName(path));
            bool extra = Path.GetFileName(path).StartsWith("extra", StringComparison.OrdinalIgnoreCase);
            foreach (FileChapterInfo chapter in stage.Chapters)
            {
                if ((ChapterType)chapter.Header[0] != ChapterType.Spell)
                    continue;
                string[] tiers = ["easy", "normal", "hard", "max"];
                if (!tiers.Any(t => HasName(number, t)) && !HasName(number, "extra"))
                    missing.Add($"spell.card.{number}.* ({chapter.Id}) — no name at any tier");
                // Extra mode runs at the "extra" tier and nothing else, so its cards need that key exactly:
                // ResolveSpellcardName falls back to the bare title, not to another tier's name.
                else if (extra && !HasName(number, "extra"))
                    missing.Add($"spell.card.{number}.extra ({chapter.Id})");
                number++;
            }
        }

        Assert.True(missing.Count == 0, "Spell cards with no name: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The packed <c>.sid</c> files are what ships and what the game loads; the <c>.json</c> under
    /// <c>Assets/Data/StagesJson</c> is the source they are compiled from (<c>--compile-stages</c>, or the Stage
    /// Editor's "Compile JSON to SID"). Both are committed, and nothing in an ordinary <c>dotnet build</c>
    /// regenerates one from the other — so editing a stage's JSON and forgetting to recompile leaves the two
    /// disagreeing and the change simply absent from the game, with every other check here still passing
    /// because the .sid it reads is internally consistent, just stale.
    /// </summary>
    [Fact]
    public void Packed_stages_match_their_json_sources()
    {
        var drifted = new List<string>();
        foreach (string jsonPath in Assets.Files("Assets/Data/StagesJson", "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(jsonPath);
            if (!Assets.Exists(Path.Combine("Assets/Data/SpellCards", name + ".sid")))
            {
                drifted.Add($"{name}.json has no compiled {name}.sid");
                continue;
            }
            FileChapterInfo[] source = StageJson.Load(jsonPath).Chapters;
            FileChapterInfo[] packed = Load(name + ".sid").Chapters;
            if (source.Length != packed.Length)
            {
                drifted.Add($"{name}: {source.Length} chapters in JSON, {packed.Length} in the .sid");
                continue;
            }
            for (int i = 0; i < source.Length; i++)
            {
                // Id, type, length and the create script — enough to catch an added, removed, reordered or
                // retimed chapter, which is what recompiling actually changes.
                string a = $"{source[i].Id}/{source[i].Header[0]}/{source[i].Header[2]}/{source[i].CreateScript}";
                string b = $"{packed[i].Id}/{packed[i].Header[0]}/{packed[i].Header[2]}/{packed[i].CreateScript}";
                if (a != b)
                    drifted.Add($"{name} chapter {i}: JSON has {a}, the .sid has {b}");
            }
        }

        Assert.True(drifted.Count == 0,
            "Stage .sid files are out of date with their JSON — rerun `dotnet bin/Debug/net10.0/aag2.dll " +
            "--compile-stages Assets/Data/StagesJson Assets/Data/SpellCards`: " + string.Join("; ", drifted));
    }

    /// <summary>
    /// A spell chapter cannot carry dialog: the packed format reuses Header[4] for its max score and
    /// <see cref="FileChapterInfo.Save"/> skips the lines entirely, so authoring a conversation on a card
    /// silently drops it. Dialogs belong on a preceding non-spell chapter.
    /// </summary>
    [Fact]
    public void No_spell_chapter_carries_dialog()
    {
        var offenders = new List<string>();
        foreach ((string file, FileStageInfo stage) in Stages())
            foreach (FileChapterInfo chapter in stage.Chapters)
                if ((ChapterType)chapter.Header[0] == ChapterType.Spell && chapter.Dialogs.Length > 0)
                    offenders.Add($"{file}:{chapter.Id}");

        Assert.True(offenders.Count == 0, "Spell chapters with dialog lines: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The Extra stage is the one the "menu.extra" entry plays: it must hold the eleven cards and the four
    /// conversations spaced through them — Dmitry's arrival, Demid's when he takes the fight over, Demid's
    /// Chromines speech between the last two cards, and Dmitry's epilogue once every card is closed.
    /// </summary>
    [Fact]
    public void Extra_stage_has_eleven_spell_cards_and_four_conversations()
    {
        FileStageInfo extra = Load("extra1.sid");

        Assert.Equal(11, extra.Chapters.Count(c => (ChapterType)c.Header[0] == ChapterType.Spell));
        Assert.Equal(4, extra.Chapters.Count(c => c.HasDialogs && c.Dialogs.Length > 0));

        FileChapterInfo[] spells = extra.Chapters.Where(c => (ChapterType)c.Header[0] == ChapterType.Spell)
            .ToArray();
        // The pre-last card is the survival one (invincible boss, outlasted rather than captured).
        Assert.True(spells[^2].BossInvincible, "The pre-last Extra card should have an invincible boss.");
        Assert.True(spells[^2].TimeoutCard, "The pre-last Extra card should be a timeout (survival) card.");
        Assert.False(spells[^1].BossInvincible, "The last Extra card has to be killable.");

        int firstCard = Array.FindIndex(extra.Chapters, c => (ChapterType)c.Header[0] == ChapterType.Spell);
        int lastCard = Array.FindLastIndex(extra.Chapters, c => (ChapterType)c.Header[0] == ChapterType.Spell);

        // Demid speaks when he takes the fight over: after Dmitry's last card, before his own first one.
        FileChapterInfo demidArrival = Chapter(extra, "demid#extra#arrival");
        Assert.True(demidArrival.Dialogs.Length > 0, "Demid's arrival has no lines.");
        int demidSpeaks = Array.IndexOf(extra.Chapters, demidArrival);
        Assert.True(demidSpeaks > Array.FindLastIndex(extra.Chapters, c => c.Id.StartsWith("dmitry#extra#card")),
            "Demid's arrival must follow Dmitry's last card.");
        Assert.True(demidSpeaks < Array.IndexOf(extra.Chapters, Chapter(extra, "demid#extra#card1")),
            "Demid's arrival must precede his own first card.");

        // Dmitry closes the stage: his epilogue is the last chapter, after every card has been played.
        FileChapterInfo epilogue = Chapter(extra, "dmitry#extra#epilogue");
        Assert.True(epilogue.Dialogs.Length > 0, "Dmitry's epilogue has no lines.");
        Assert.Equal(extra.Chapters.Length - 1, Array.IndexOf(extra.Chapters, epilogue));
        Assert.True(Array.IndexOf(extra.Chapters, epilogue) > lastCard,
            "The epilogue must follow the last card.");

        // Neither conversation is a fight, so neither may raise a health bar over itself.
        Assert.True(demidArrival.BossInvincible && epilogue.BossInvincible,
            "The framing conversations must be flagged BossInvincible (no health bar over a speech).");

        // Dmitry's beats carry the plot, so both of his are unskippable; Demid's can be pressed through.
        FileChapterInfo arrival = Chapter(extra, "dmitry#extra#arrival");
        foreach (FileChapterInfo talk in new[] { arrival, epilogue })
            Assert.All(talk.Dialogs, d => Assert.True(d.Unskippable,
                $"Dmitry's line '{d.Text}' ({talk.Id}) should be unskippable."));
        Assert.True(Array.IndexOf(extra.Chapters, arrival) < firstCard,
            "The speech must precede the first card.");
    }

    /// <summary>The one chapter with this id, asserting a readable failure when it has been renamed away.</summary>
    private static FileChapterInfo Chapter(FileStageInfo stage, string id)
    {
        FileChapterInfo? chapter = Array.Find(stage.Chapters, c => c.Id == id);
        Assert.True(chapter != null, $"No chapter '{id}' in the stage.");
        return chapter!;
    }
}

using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.ComponentModel.DataAnnotations;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Gameplay.RuntimeData;

public class RuntimeChapter
{
    public readonly bool TimeoutCard;
    public readonly bool BossInvincible;
    public readonly bool ApplyShader;
    public readonly ShaderHandle? SpellShader;
    public readonly int LocPosition;
    public readonly int LocTime;
    public readonly bool HasDialogs;

    /// <summary>The chapter's authored dialog lines. Loaded from the .sid all along; nothing played them.</summary>
    public readonly FileDialogInfo[] Dialogs = [];
    public readonly bool UseUpdateScript;
    public readonly bool UseCreateScript;
    public readonly ChapterType Type;
    public readonly RuntimeChapterReferenceAction? CreateScript;
    public readonly RuntimeChapterReferenceAction? UpdateScript;
    public readonly int Length;
    public readonly int TickStart;
    public readonly int LengthOffset = 0;
    public readonly int MaxScore = 0;
    public readonly RenderedTexture? BossTitleTexture;
    public readonly RenderedTexture? ChapterTitleTexture;
    public readonly BasicTexture? SpellcardTexture;

    /// <summary>The spell card's name — also the key under which PlayerData records tries/successes.</summary>
    public readonly string SpellcardTitle = "";
    public readonly GameBox GameBox;
    public int[] Header = new int[128];

    public RuntimeChapter(FileChapterInfo chapterInfo, int tickStart, GameBox box)
    {
        GameBox = box;
        TickStart = tickStart;
        Length = chapterInfo.Header[2];
        TimeoutCard = chapterInfo.TimeoutCard;
        BossInvincible = chapterInfo.BossInvincible;
        HasDialogs = chapterInfo.HasDialogs;
        Dialogs = chapterInfo.Dialogs;
        UseUpdateScript = chapterInfo.UseUpdateScript;
        UseCreateScript = chapterInfo.UseCreateScript;
        Type = (ChapterType)chapterInfo.Header[0];
        if ((int)Type > 1)
        {
            // The shown boss title resolves through translation.json, exactly like the spell-card title below.
            // TranslateRaw picks one of the key's ;-separated variants and returns the authored name unchanged
            // when it has no entry (so untranslated bosses keep working); the renderer transliterates it. Resolve
            // once so sizing and drawing use the same variant.
            string bossDisplay = Helper.TranslateRaw(chapterInfo.BossName);
            var size = Helper.GetBossTextSize(bossDisplay);
            BossTitleTexture = LoadRenderTexture((int)size.X, (int)size.Y);
            Helper.DrawBossText(BossTitleTexture.Value, bossDisplay);
        }
        if (Type == ChapterType.Spell)
        {
            SpellcardTexture = Runtime.CurrentRuntime.Textures[chapterInfo.SpellcardTexture];
            MaxScore = chapterInfo.Header[4];
            // The record key stays the raw authored title (stable across difficulties); only the SHOWN name is
            // resolved through translation.json and specialised per difficulty. Resolve once — Translate picks a
            // random variant per call, so sizing and drawing must see the same string.
            SpellcardTitle = chapterInfo.SpellcardTitle;
            string displayTitle = Helper.ResolveSpellcardName(chapterInfo.SpellcardTitle, box.Difficulty);
            var size = Helper.GetTitleTextSize(displayTitle);
            ChapterTitleTexture = LoadRenderTexture((int)size.X, (int)size.Y);
            Helper.DrawChapterTitleText(ChapterTitleTexture.Value, displayTitle);
        }
        if(UseCreateScript && ActionsScope.ChapterActions.ContainsKey(chapterInfo.CreateScript))
            CreateScript = ActionsScope.ChapterActions[chapterInfo.CreateScript];
        if(UseUpdateScript && ActionsScope.ChapterActions.ContainsKey(chapterInfo.UpdateScript))
            UpdateScript = ActionsScope.ChapterActions[chapterInfo.UpdateScript];
        ApplyShader = chapterInfo.ApplyShader;
        if (ApplyShader)
        {
            SpellShader = Runtime.CurrentRuntime.Shaders[chapterInfo.SpellcardShader];
            LocPosition = GetShaderLocation(SpellShader.Value, "pos");
            LocTime = GetShaderLocation(SpellShader.Value, "time");
        }
    }

    public void Unload()
    {
        if(BossTitleTexture != null)
            UnloadRenderTexture(BossTitleTexture.Value);
        if(ChapterTitleTexture != null)
            UnloadRenderTexture(ChapterTitleTexture.Value);
    }
}

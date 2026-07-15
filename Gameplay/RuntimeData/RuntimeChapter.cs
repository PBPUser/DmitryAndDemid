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
    public readonly TargetHandle? BossTitleTexture;
    public readonly TargetHandle? ChapterTitleTexture;
    public readonly TextureHandle? SpellcardTexture;

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
            var size = Helper.GetBossTextSize(chapterInfo.BossName);
            BossTitleTexture = LoadRenderTexture((int)size.X, (int)size.Y);
            Helper.DrawBossText(BossTitleTexture.Value, chapterInfo.BossName);
        }
        if (Type == ChapterType.Spell)
        {
            SpellcardTexture = Runtime.CurrentRuntime.Textures[chapterInfo.SpellcardTexture];
            MaxScore = chapterInfo.Header[4];
            SpellcardTitle = chapterInfo.SpellcardTitle;
            var size = Helper.GetTitleTextSize(chapterInfo.SpellcardTitle);
            ChapterTitleTexture = LoadRenderTexture((int)size.X, (int)size.Y);
            Helper.DrawChapterTitleText(ChapterTitleTexture.Value, chapterInfo.SpellcardTitle);
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

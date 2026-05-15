using System.ComponentModel.DataAnnotations;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Utils;
using Raylib_cs;

namespace DmitryAndDemid.Gameplay.RuntimeData;

public class RuntimeChapter
{
    public readonly bool TimeoutCard;
    public readonly bool BossInvincible;
    public readonly bool ApplyShader;
    public readonly Shader? SpellShader;
    public readonly int LocPosition;
    public readonly int LocTime;
    public readonly bool HasDialogs;
    public readonly bool UseUpdateScript;
    public readonly bool UseCreateScript;
    public readonly ChapterType Type;
    public readonly RuntimeChapterReferenceAction? CreateScript;
    public readonly RuntimeChapterReferenceAction? UpdateScript;
    public readonly int Length;
    public readonly int TickStart;
    public readonly int LengthOffset = 0;
    public readonly int MaxScore = 0;
    public readonly Drop BadDrop;
    public readonly Drop GoodDrop;
    public readonly RenderTexture2D? BossTitleTexture;
    public readonly RenderTexture2D? ChapterTitleTexture;
    public readonly Texture2D? SpellcardTexture;
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
        UseUpdateScript = chapterInfo.UseUpdateScript;
        UseCreateScript = chapterInfo.UseCreateScript;
        Type = (ChapterType)chapterInfo.Header[0];
        BadDrop = new Drop(chapterInfo.Header[5]);
        GoodDrop = new Drop(chapterInfo.Header[6]);
        if ((int)Type > 1)
        {
            var size = Helper.GetBossTextSize(chapterInfo.BossName);
            BossTitleTexture = Raylib.LoadRenderTexture((int)size.X, (int)size.Y);
            Helper.DrawBossText(BossTitleTexture.Value, chapterInfo.BossName);
        }
        if (Type == ChapterType.Spell)
        {
            SpellcardTexture = Runtime.CurrentRuntime.Textures[chapterInfo.SpellcardTexture];
            MaxScore = chapterInfo.Header[4];
            var size = Helper.GetTitleTextSize(chapterInfo.SpellcardTitle);
            ChapterTitleTexture = Raylib.LoadRenderTexture((int)size.X, (int)size.Y);
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
            LocPosition = Raylib.GetShaderLocation(SpellShader.Value, "pos");
            LocTime = Raylib.GetShaderLocation(SpellShader.Value, "time");
        }
    }

    public void Unload()
    {
        if(BossTitleTexture != null)
            Raylib.UnloadRenderTexture(BossTitleTexture.Value);
        if(ChapterTitleTexture != null)
            Raylib.UnloadRenderTexture(ChapterTitleTexture.Value);
    }
}

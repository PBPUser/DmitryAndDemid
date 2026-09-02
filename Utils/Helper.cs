using System;
using System.Diagnostics;
using DmitryAndDemid.Rendering;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using Microsoft.CSharp.RuntimeBinder;
using static DmitryAndDemid.Rendering.Gfx;
using Process = GLib.Process;
using SDProcess = System.Diagnostics.Process;
#if ANDROID
using Android.Content;
using Android.Net;
using Android.App;
#endif
namespace DmitryAndDemid.Utils;

public static class Helper
{
    public static string[] DifficultyIds = ["Jlerkuj", "HopMaJlb", "XAPDKOP", "MaKcuM", "3xTpa"];
    
    public static void LoadShaderAttribs()
    {
        PrepareTimerRenderer();
        
        LocationCloudRadius = GetShaderLocation(Runtime.CurrentRuntime.Shaders["cloud"], "radius");
        LocationCloudDimensions = GetShaderLocation(Runtime.CurrentRuntime.Shaders["cloud"], "dimenssions");
        LocationCloudAngle = GetShaderLocation(Runtime.CurrentRuntime.Shaders["cloud"], "angle");
        LocationCloudWidth = GetShaderLocation(Runtime.CurrentRuntime.Shaders["cloud"], "width");
        LocationCloudSize = GetShaderLocation(Runtime.CurrentRuntime.Shaders["cloud"], "size");

        LocationWaveScale = GetShaderLocation(Runtime.CurrentRuntime.Shaders["wave"], "scale");
        LocationWaveXPower = GetShaderLocation(Runtime.CurrentRuntime.Shaders["wave"], "xPower");
        LocationWaveOffsetX = GetShaderLocation(Runtime.CurrentRuntime.Shaders["wave"], "offsetX");
        LocationWaveOffsetY = GetShaderLocation(Runtime.CurrentRuntime.Shaders["wave"], "offsetY");
        LocationWaveScreenSize = GetShaderLocation(Runtime.CurrentRuntime.Shaders["wave"], "screenSize");
        LocationWaveScreenColor = GetShaderLocation(Runtime.CurrentRuntime.Shaders["wave"], "color");

        LocationFlipScreenSize = GetShaderLocation(Runtime.CurrentRuntime.Shaders["flip"], "screenSize");
        
        LocationRenderSelectionHeight = GetShaderLocation(Runtime.CurrentRuntime.Shaders["selection"], "height");
        LocationRenderSelectionScreenSize = GetShaderLocation(Runtime.CurrentRuntime.Shaders["selection"], "screenSize");
        
        LocationContrastOpacity = GetShaderLocation(Runtime.CurrentRuntime.Shaders["contrast"], "opacity");
        LocationContrastLevel = GetShaderLocation(Runtime.CurrentRuntime.Shaders["contrast"], "contrastLevel");

        LocationRotateYaw = GetShaderLocation(Runtime.CurrentRuntime.Shaders["rotate"], "yaw");
        LocationRotatePitch = GetShaderLocation(Runtime.CurrentRuntime.Shaders["rotate"], "pitch");
        LocationRotateRoll = GetShaderLocation(Runtime.CurrentRuntime.Shaders["rotate"], "roll");
        LocationRotateFocal = GetShaderLocation(Runtime.CurrentRuntime.Shaders["rotate"], "focal");

        LocationDisappearShootPosition = GetShaderLocation(Runtime.CurrentRuntime.Shaders["disappear_shoot"], "pos");
        LocationDisappearShootTime = GetShaderLocation(Runtime.CurrentRuntime.Shaders["disappear_shoot"], "u_time");
        
        LocationShadowDepth = GetShaderLocation(Runtime.CurrentRuntime.Shaders["shadow"], "depth");
        LocationShadowResolution = GetShaderLocation(Runtime.CurrentRuntime.Shaders["shadow"], "res");

        LocationGradientBorderWidth = GetShaderLocation(Runtime.CurrentRuntime.Shaders["gradient"], "border_width");
        LocationGradientResoulution = GetShaderLocation(Runtime.CurrentRuntime.Shaders["gradient"], "res");

        PizzaSource = new Rect(0, 0, Runtime.CurrentRuntime.Textures["pizza.png"].Width, Runtime.CurrentRuntime.Textures["pizza.png"].Height);
    }

    static Rect PizzaSource;

    private static int LocationGradientBorderWidth;
    private static int LocationGradientResoulution;

    public static bool GetResolutionFromString(string str, out (int width, int height) res) =>
        HelperPure.GetResolutionFromString(str, out res);

    public static bool GetMultiplyerFromRes(string str, out double multiplyer) =>
        HelperPure.GetMultiplyerFromRes(str, out multiplyer);

    static int LocationCloudRadius;
    static int LocationCloudDimensions;
    static int LocationCloudAngle;
    static int LocationCloudWidth;
    static int LocationCloudSize;
    static int LocationCloudScreenSize;

    private static int LocationContrastLevel;
    private static int LocationContrastOpacity;

    static int LocationRotateRoll;
    static int LocationRotatePitch;
    static int LocationRotateYaw;
    static int LocationRotateFocal;

    private static int LocationShadowDepth;
    private static int LocationShadowResolution;

    public static void BeginRotateShader(float roll, float pitch, float yaw, float focal)
    {
        SetShaderValue(Runtime.CurrentRuntime.Shaders["rotate"], LocationRotateFocal, focal, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["rotate"], LocationRotateRoll, roll, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["rotate"], LocationRotatePitch, pitch, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["rotate"], LocationRotateYaw, yaw, UniformType.Float);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["rotate"]);
    }
    
    public static void BeginContrastShader(float contrastLevel, float opacity)
    {
        SetShaderValue(Runtime.CurrentRuntime.Shaders["contrast"], LocationContrastOpacity, opacity, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["contrast"], LocationContrastLevel, contrastLevel, UniformType.Float);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["contrast"]);
    }

    private const float BossTextFontSize = 8;
    private const float ChapterTitleFontSize = 12;
    private static Rgba BossTextColor = Rgba.Lime;
    
    public static Vector2 GetBossTextSize(string text)
    {
        string transliterate = Transliterate(text);
        return MeasureTextEx(GetFontDefault(),
            transliterate,
            BossTextFontSize * Runtime.CurrentRuntime.ScaleF,
            Runtime.CurrentRuntime.ScaleF);
    }

    public static void DrawBossText(RenderedTexture texture, string text)
    {
        string transliterate = Transliterate(text);
        RenderedTexture temp = LoadRenderTexture(texture.Texture.Width,  texture.Texture.Height);
        BeginTextureMode(temp);
        DrawTextEx(GetFontDefault(),
            transliterate,
            Vector2.Zero,
            BossTextFontSize * Runtime.CurrentRuntime.ScaleF,
            Runtime.CurrentRuntime.ScaleF, BossTextColor);
        EndTextureMode();
        BeginTextureMode(texture);
        // In-range source rect, for the same reason as in DrawChapterTitleText below: the (0, H, W, +H) form
        // this used to carry asks for v in [1,2], which only lands on the right texels because most backends
        // sample render targets with Repeat. On the Silk backend's CLAMP_TO_EDGE targets it smeared the
        // scratch's empty bottom row instead, losing the boss's name.
        Rect source = new(0, 0, temp.Texture.Width, temp.Texture.Height);
        Rect destination = new(0, 0, temp.Texture.Width, temp.Texture.Height);
        DrawTexturePro(temp.Texture, source, destination, Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        SetTextureFilter(texture.Texture, FilterMode.Bilinear);   // anti-alias the boss title when it's scaled
        UnloadRenderTexture(temp);
    }

    public static void DrawChapterTitleText(RenderedTexture texture, string text)
    {
        string transliterate = Transliterate(text);
        RenderedTexture temp = LoadRenderTexture(texture.Texture.Width,  texture.Texture.Height);
        BeginTextureMode(temp);
        var b = GetTitleTextSize(text);
        DrawTextEx(Runtime.CurrentRuntime.Fonts["kodemono"],
            transliterate,
            new(b.X * 0.33f, b.Y * 0.3f),
            ChapterTitleFontSize * Runtime.CurrentRuntime.ScaleF,
            Runtime.CurrentRuntime.ScaleF, Rgba.White);
        EndTextureMode();
        BeginTextureMode(texture);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["spellcard_title"]);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["384x448"],
            new Rect(0, 0, 384, 448),
            new Rect(0, 0, b),
            Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        SetShaderValue(Runtime.CurrentRuntime.Shaders["outline"], LocationOutlineBorderwidth, Runtime.CurrentRuntime.ScaleF , UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["outline"], LocationOutlineResolution,
            [b.X / 1.5f, b.Y / 1.5f], UniformType.Vec2);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["outline"]);
        // Read the scratch back through an IN-RANGE source rect, like DrawTextOutline does for the same
        // text-scratch-into-target step. This used to be spelled (0, H, W, +H) — a positive height starting at
        // the bottom edge, which asks for v in [1,2]. That only ever worked by accident of the wrap mode: on
        // Repeat (Raylib/Vulkan/desktop GL) it wraps to exactly these texels, but render targets on the Silk
        // backend are CLAMP_TO_EDGE, where it instead smears the scratch's empty bottom row over the whole
        // quad and the spell card's name never reaches the title texture at all. Same texels as before on the
        // wrapping backends; correct rather than lucky on the clamping one.
        Rect source = new(0, 0, temp.Texture.Width, temp.Texture.Height);
        Rect destination = new(0, 0, temp.Texture.Width, temp.Texture.Height);
        DrawTexturePro(temp.Texture, source, destination, Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        SetTextureFilter(texture.Texture, FilterMode.Bilinear);   // anti-alias the chapter/spell title when scaled
        UnloadRenderTexture(temp);
    }

    public static Vector2 GetTitleTextSize(string text)
    {
        string transliterate = Transliterate(text);
        return MeasureTextEx(Runtime.CurrentRuntime.Fonts["kodemono"],
            transliterate,
            ChapterTitleFontSize * Runtime.CurrentRuntime.ScaleF,
            Runtime.CurrentRuntime.ScaleF) * 1.5f;
    }
    
    public static RenderedTexture RenderTextureInCloud(BasicTexture texture, float radius = 3f, float angle = -0.85f, float width = 0.35f, float size = 1.4f)
    {
        RenderedTexture cloud = LoadRenderTexture(texture.Width * 2, texture.Height * 2);
        var arr = new float[] { 1, 1 };
        SetShaderValue(Runtime.CurrentRuntime.Shaders["cloud"], LocationCloudRadius, radius, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["cloud"], LocationCloudAngle, angle, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["cloud"], LocationCloudWidth, width, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["cloud"], LocationCloudSize, size, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["cloud"], LocationCloudDimensions, arr, UniformType.Vec2);
        BeginTextureMode(cloud);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["cloud"]);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["pizza.png"], PizzaSource, new Rect(0, 0, cloud.Texture.Width, cloud.Texture.Height), Vector2.Zero, 0f, Rgba.White);//
        EndShaderMode();
        DrawTexture(texture, texture.Width / 2, texture.Height / 2, Rgba.White);
        EndTextureMode();
        return cloud;
    }

    static int LocationWaveScale;
    static int LocationWaveXPower;
    static int LocationWaveOffsetX;
    static int LocationWaveOffsetY;
    static int LocationWaveScreenSize;
    static int LocationWaveScreenColor;

    public static void DrawWave(Rgba color, float offsetX, float offsetY, float xPower, float scale, Rect target)
    {
        SetShaderValue(Runtime.CurrentRuntime.Shaders["wave"], LocationWaveScale, scale, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["wave"], LocationWaveXPower, xPower, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["wave"], LocationWaveOffsetX, offsetX, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["wave"], LocationWaveOffsetY, offsetY, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["wave"], LocationWaveScreenColor, ColorToVector(color), UniformType.Vec4);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["wave"], LocationWaveScreenSize, new float[] { target.Width, target.Height }, UniformType.Vec2);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["wave"]);
        DrawRectanglePro(target, Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
    }

    private static ShaderHandle OutlineShader;
    private static ShaderHandle TextGradientShader;
    private static ShaderHandle AAShader;
    private static float TimerFontSize = 24;
    private static float TimerFontSpacing = 2;
    private static RenderedTexture TempTimerTexture, TempTimerTexture2;
    private static Rect TimerRectangleSource, TimerRectangleTarget;
    // Resolved on use, not in a static field initializer: the fonts dictionary is filled during Load(), and
    // touching Helper before that (as Android's earlier, eager static-init timing does) would otherwise throw
    // KeyNotFound before the font is even loaded.
    private static FontHandle TimerFont => Runtime.CurrentRuntime.Fonts["kodemono"];
    private static int LocationOutlineResolution;
    private static int LocationOutlineFullResolution;
    private static int LocationOutlinePosition;
    private static int LocationOutlineBorderwidth;
    private static int LocationAAResolution;
    private static int LocationAAScale;
    public static Vector2 TimerTextureSize;
    private static Vector2 TimerPos;
    private const float SplashTimerSize = 20;
    private const float SplashTimerMillsSize = 16;
    private const float BonusCountSize = 10;
    
    static void PrepareTimerRenderer()
    {
        AAShader = Runtime.CurrentRuntime.Shaders["font_antialias"];
        OutlineShader = Runtime.CurrentRuntime.Shaders["outline2"];
        TextGradientShader = Runtime.CurrentRuntime.Shaders["text_gradient"];
        LocationOutlineBorderwidth = GetShaderLocation(OutlineShader, "border_width");
        LocationOutlineResolution = GetShaderLocation(OutlineShader, "res");
        LocationOutlineFullResolution = GetShaderLocation(OutlineShader, "fres");
        LocationOutlinePosition = GetShaderLocation(OutlineShader, "pos");
        TimerFontSize *= Runtime.CurrentRuntime.ScaleF;
        TimerFontSpacing *= Runtime.CurrentRuntime.ScaleF;
        TimerTextureSize = MeasureTextEx(TimerFont, "00.00",  TimerFontSize, TimerFontSpacing) * 1.2f;
        TimerPos = TimerTextureSize / 12f; 
        TempTimerTexture = LoadRenderTexture((int)TimerTextureSize.X, (int)TimerTextureSize.Y);
        TempTimerTexture2 = LoadRenderTexture((int)TimerTextureSize.X, (int)TimerTextureSize.Y);
        TimerRectangleSource = new Rect(0, (int)TimerTextureSize.Y, (int)TimerTextureSize.X, -(int)TimerTextureSize.Y);
        TimerRectangleTarget = new Rect(0, 0, (int)TimerTextureSize.X, (int)TimerTextureSize.Y);
        LocationAAResolution = GetShaderLocation(AAShader, "resolution");
        LocationAAScale = GetShaderLocation(AAShader, "scale");
    }
    
    public static void DrawScoreText(string text, float fontSize, Vector2 position, Rgba color)
    {
        const string t = "0123456789./";
        var vec2 = GetScoreTextureSize(text, fontSize);
        Rect copy = new Rect(new(Runtime.CurrentRuntime.ScoreSpacing, 
                Runtime.CurrentRuntime.ScoreSpacing),
            Runtime.CurrentRuntime.ScoreLetterWidth, Runtime.CurrentRuntime.ScoreLetterHeight);
        Rect target = new(0,position.Y, new Vector2(Runtime.CurrentRuntime.ScoreLetterWidth * (fontSize/64), vec2.Y));
        int z = 0, i = 0;
        var ctexture = Runtime.CurrentRuntime.Textures["ScoreDigitsPrerender"];
        foreach (var c in text)
        {
            z = t.IndexOf(c);
            DrawTexturePro(ctexture,
                copy with { X = copy.X + Runtime.CurrentRuntime.ScoreLetterWidth * z },
                target with { X = position.X + target.Width * i },
                Vector2.Zero, 0, color);
            i++;
        }
    }

    public static string FormatScore(int score, int c) => HelperPure.FormatScore(score, c);

    public static Vector2 GetScoreTextureSize(string text, float fontSize)
    {
        return new(
            text.Length * Runtime.CurrentRuntime.ScoreLetterWidth * (fontSize/64),
            Runtime.CurrentRuntime.ScoreLetterHeight * (fontSize/64)
            );
    }

    public static RenderedTexture CreateScoreText(string text, float fontSize)
    {
        var vec2 = GetScoreTextureSize(text, fontSize);
        var texture = LoadRenderTexture((int)vec2.X, (int)vec2.Y);
        BeginTextureMode(texture);
        DrawScoreText(text, fontSize, Vector2.Zero, Rgba.White);
        EndTextureMode();
        return texture;
    }

    private static RenderedTexture BonusTexture;
    private static RenderedTexture SpellTexture;
    private static RenderedTexture SubtitleBufferTexture;
    private static float SpellFontSize;

    // Persistent scratch/output targets for the spell-card subtitle, reused across frames. Each frame a card
    // is on screen DrawSpellSubtitle used to allocate and free ~8 render textures (a mask + a framed output
    // for each of four text parts), churning GPU framebuffers for the whole duration of the card. Each of the
    // four parts keeps its own mask + output; both are (re)allocated only when that part's pixel size changes
    // (never per frame), so a part's mask never thrashes against a differently-sized neighbour.
    private static readonly RenderedTexture[] SubtitleMasks = new RenderedTexture[4];
    private static readonly Vector2[] SubtitleMaskSizes = { Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero };
    private static readonly RenderedTexture[] SubtitleParts = new RenderedTexture[4];
    private static readonly Vector2[] SubtitlePartSizes = { Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero };

    /// <summary>Ensures <paramref name="handle"/> is a render texture of exactly <paramref name="size"/>,
    /// reallocating only when the size differs from <paramref name="currentSize"/>. A <paramref name="currentSize"/>
    /// of <see cref="Vector2.Zero"/> means "not yet allocated" (nothing to free).</summary>
    static void EnsureRenderTexture(ref RenderedTexture handle, ref Vector2 currentSize, Vector2 size)
    {
        int w = Math.Max(1, (int)size.X);
        int h = Math.Max(1, (int)size.Y);
        if ((int)currentSize.X == w && (int)currentSize.Y == h)
            return;
        if (currentSize != Vector2.Zero)
            UnloadRenderTexture(handle);
        handle = LoadRenderTexture(w, h);
        currentSize = new Vector2(w, h);
    }

    /// <summary>Design units (x UI scale) for the score line under a spell card's name.</summary>
    private const float SpellSubtitleFontSize = 11;

    static void PrepareSpellSubtitleTextures()
    {
        SpellFontSize = BonusCountSize *  Runtime.CurrentRuntime.ScaleF;
        string bonusTitle = Translate("spell.bonus");
        string spellTitle = Translate("spell.attempt");
        DrawTextOutline(out BonusTexture, TimerFont, SpellFontSize, bonusTitle, Rgba.Blue, 0);
        DrawTextOutline(out SpellTexture, TimerFont, SpellFontSize, spellTitle, Rgba.Blue, 0);
        SubtitleBufferTexture = LoadRenderTexture(8192, BonusTexture.Texture.Height);
    }
    
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="score"></param>
    /// <param name="renderTexture2D"></param>
    /// <param name="total"></param>
    /// <param name="success"></param>
    /// <returns>Used Texture Width</returns>
    /// <summary>
    /// The score line that sits UNDER the spell card's name: the current bonus, styled with the
    /// text_frame shader (gradient + frame + a highlight sweep while the card is live), followed by the
    /// player's record on this card as good/total.
    ///
    /// Formatting rules: more than 99 successes prints "master" instead of the pair; more than 99 attempts
    /// prints "99+" instead of the number.
    /// </summary>
    /// <summary>
    /// The score line under a spell card's name: "bonus: <score>" and "attempt: <good>/<total>", styled with
    /// the text_frame shader.
    ///
    /// Rules: a failed card shows <paramref name="failedText"/> in place of the score; more than 99 successes
    /// prints spell.master instead of the pair; more than 99 attempts prints "99+" instead of the number.
    /// </summary>
    /// <param name="score">The bonus, or -1 when the card has been failed.</param>
    /// <param name="failedText">
    /// The already-picked spell.failed wording. It is passed in rather than translated here because that key
    /// has four variants and Translate() picks at random — resolving it every frame would make the word
    /// flicker.
    /// </param>
    /// <param name="rightX">RIGHT edge to align to — the name slides in from far left at up to 10x scale.</param>
    public static int DrawSpellSubtitle(RenderedTexture target, int score, int total, int success,
        int rightX = 0, int posY = 0, string failedText = "", float appear = 1f)
    {
        // appear (0..1) fades and slides this line in a beat after the card name, so it does not pop in with
        // the title. Fully hidden at 0, so skip the work entirely.
        appear = Math.Clamp(appear, 0f, 1f);
        if (appear <= 0f)
            return 0;

        bool failed = score < 0;

        string bonusLabel = Translate("spell.bonus");
        string attemptLabel = Translate("spell.attempt");
        string bonusValue = failed ? failedText : score.ToString();
        string triesValue = success > 99
            ? Translate("spell.master")
            : $"{success:00}/{(total > 99 ? "99+" : $"{total:00}")}";

        float fontSize = SpellSubtitleFontSize * Runtime.CurrentRuntime.ScaleF;
        float border = 2 * Runtime.CurrentRuntime.ScaleF;

        // Labels: quiet, no sweep. Values: the bonus is "live" (gold, sweeping) unless the card was failed,
        // in which case it goes red; the attempt record is a static white.
        // Rendered into persistent, reused targets (see SubtitleParts) so no GPU render texture is allocated
        // or freed on a normal frame — only when a part's pixel size actually changes (e.g. the bonus value
        // gains a digit). The four parts are all alive at once for the composite below, so each has its own.
        DrawTextFramedInto(ref SubtitleMasks[0], ref SubtitleMaskSizes[0], ref SubtitleParts[0], ref SubtitlePartSizes[0],
            TimerFont, fontSize, bonusLabel,
            new Rgba(190, 205, 255), new Rgba(120, 140, 200), Rgba.Black, border);
        // Score/bonus value: static gradient, no animated sweep (highlightStrength 0). The moving highlight
        // shader that used to play across the score during a spell card is intentionally removed.
        DrawTextFramedInto(ref SubtitleMasks[1], ref SubtitleMaskSizes[1], ref SubtitleParts[1], ref SubtitlePartSizes[1],
            TimerFont, fontSize, bonusValue,
            failed ? new Rgba(255, 150, 150) : new Rgba(255, 255, 255),   // value is white, red on a failed clean clear
            failed ? new Rgba(200, 30, 30) : new Rgba(215, 215, 215),
            Rgba.Black, border, 0f);
        DrawTextFramedInto(ref SubtitleMasks[2], ref SubtitleMaskSizes[2], ref SubtitleParts[2], ref SubtitlePartSizes[2],
            TimerFont, fontSize, attemptLabel,
            new Rgba(190, 205, 255), new Rgba(120, 140, 200), Rgba.Black, border);
        // Attempt record: white with a slight top-to-bottom shadow gradient (plus the black frame below).
        DrawTextFramedInto(ref SubtitleMasks[3], ref SubtitleMaskSizes[3], ref SubtitleParts[3], ref SubtitlePartSizes[3],
            TimerFont, fontSize, triesValue,
            new Rgba(255, 255, 255), new Rgba(205, 205, 205), Rgba.Black, border);

        float gap = 10 * Runtime.CurrentRuntime.ScaleF;
        RenderedTexture[] parts = SubtitleParts;
        float lineWidth = parts.Sum(p => p.Texture.Width) + gap;   // one gap, between the two pairs

        float posX = rightX - lineWidth;
        // Slide up into place as it fades in, and fade with the same factor.
        float slide = (1f - appear) * 12f * Runtime.CurrentRuntime.ScaleF;
        Rgba tint = Rgba.White with { A = TimeToTransparency(appear) };
        // The name zooms in at up to 10x, which would fling this line off the overlay while that plays.
        posY = Math.Clamp(posY, 0, target.Texture.Height - parts[1].Texture.Height);
        posX = Math.Clamp(posX, 0, Math.Max(0, target.Texture.Width - lineWidth));

        BeginTextureMode(target);
        float x = posX;
        for (int i = 0; i < parts.Length; i++)
        {
            RenderedTexture part = parts[i];
            DrawTexturePro(part.Texture, GetFullSourceRenderTexture(part),
                new Rect(x, posY + slide, part.Texture.Width, part.Texture.Height), Vector2.Zero, 0, tint);
            x += part.Texture.Width;
            if (i == 1)
                x += gap;   // space between "bonus: N" and "attempt: N/N"
        }
        EndTextureMode();

        // Deliberately NOT unloaded — the part targets persist and are reused next frame. Freeing them here
        // (as the original did) is what made the game allocate and destroy render textures every frame a
        // spell card was active.
        return (int)posX;
    }

    /// <summary>
    /// Renders text with a frame (outline) and a vertical gradient, using Assets/Shaders/text_frame.fs, into a
    /// caller-owned persistent mask and output target that are reused across frames. The frame grows OUTWARD
    /// from the glyphs, so the text is first drawn into a padded scratch (the mask) — without the padding the
    /// shader would dilate into the edge of the texture and the frame would be clipped. Nothing is allocated or
    /// freed unless the text's pixel size changes, so the per-frame spell-card subtitle does not churn GPU
    /// render textures. Pass highlightStrength > 0 for the animated sweep (used to emphasise the score during a
    /// spell card); 0 gives a static gradient.
    /// </summary>
    static void DrawTextFramedInto(ref RenderedTexture mask, ref Vector2 maskSize, ref RenderedTexture texture,
        ref Vector2 textureSize, FontHandle font, float fontSize, string text, Rgba colorTop, Rgba colorBottom,
        Rgba borderColor, float borderWidth, float highlightStrength = 0f)
    {
        Vector2 measure = MeasureTextEx(font, text, fontSize, 1);
        float padding = MathF.Ceiling(borderWidth) + 2;
        Vector2 size = measure + new Vector2(padding * 2);

        EnsureRenderTexture(ref mask, ref maskSize, size);
        BeginTextureMode(mask);
        ClearBackground(Rgba.Blank);
        DrawTextEx(font, text, new Vector2(padding), fontSize, 1, Rgba.White);
        EndTextureMode();

        ShaderHandle shader = Runtime.CurrentRuntime.Shaders["text_frame"];
        SetShaderValue(shader, GetShaderLocation(shader, "res"), size, UniformType.Vec2);
        SetShaderValue(shader, GetShaderLocation(shader, "border_width"), borderWidth, UniformType.Float);
        SetShaderValue(shader, GetShaderLocation(shader, "border_color"), borderColor.ToVector4(), UniformType.Vec4);
        SetShaderValue(shader, GetShaderLocation(shader, "color_top"), colorTop.ToVector4(), UniformType.Vec4);
        SetShaderValue(shader, GetShaderLocation(shader, "color_bottom"), colorBottom.ToVector4(), UniformType.Vec4);
        SetShaderValue(shader, GetShaderLocation(shader, "time"), (float)GetTime(), UniformType.Float);
        SetShaderValue(shader, GetShaderLocation(shader, "highlight_strength"), highlightStrength, UniformType.Float);

        EnsureRenderTexture(ref texture, ref textureSize, size);
        BeginTextureMode(texture);
        ClearBackground(Rgba.Blank);
        BeginShaderMode(shader);
        DrawTexturePro(mask.Texture,
            GetFullSourceRenderTexture(mask),
            new Rect(0, 0, size.X, size.Y),
            Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
    }

    /// <summary>
    /// Draws immediate-mode text with a solid outline, by stamping the string eight ways around the fill
    /// position before drawing the fill on top. <see cref="DrawTextOutline"/> is the equivalent for text baked
    /// into a render texture, where the outline shader can run over the result; this one is for text drawn
    /// straight to the screen every frame, which has no target for a shader pass.
    /// </summary>
    public static void DrawTextOutlined(FontHandle font, string text, Vector2 position, float fontSize,
        float spacing, Rgba fill, Rgba outline, float thickness)
    {
        if (thickness > 0.05f && outline.A > 0)
            for (int i = 0; i < 8; i++)
            {
                float a = i * MathF.PI / 4f;
                DrawTextEx(font, text, position + new Vector2(MathF.Cos(a), MathF.Sin(a)) * thickness,
                    fontSize, spacing, outline);
            }
        DrawTextEx(font, text, position, fontSize, spacing, fill);
    }

    /// <param name="borderWidth">Outline thickness in screen px; negative (the default) keeps the historic
    /// fixed 4 * ScaleF. That width is absolute, not relative to <paramref name="fontSize"/>, so small text
    /// baked through here drowns in its own outline unless the caller scales it down to match.</param>
    public static void DrawTextOutline(out RenderedTexture texture, FontHandle font, float fontSize, string text, Rgba color, float padding, float borderWidth = -1)
    {
        DrawTextAliasedA(out var temp, font, fontSize, 0, text, color);
        texture = LoadRenderTexture((int)(temp.Texture.Width + padding * 2),
            (int)(temp.Texture.Height + padding * 2));
        var s = GetFullSource(texture.Texture);
        var temp2 = LoadRenderTexture(texture.Texture.Width, texture.Texture.Height);
        SetShaderValue(OutlineShader, LocationOutlinePosition, [0f, 0f], UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineFullResolution, s.Size, UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineResolution, s.Size, UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineBorderwidth,
            borderWidth < 0 ? 4 * Runtime.CurrentRuntime.ScaleF : borderWidth, UniformType.Float);
        BeginTextureMode(temp2);
        DrawTexturePro(temp.Texture,
            new Rect(0, 0, temp.Texture.Width, temp.Texture.Height),
            new Rect(padding, padding, temp.Texture.Width, temp.Texture.Height),
            Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        BeginTextureMode(texture);
        BeginShaderMode(OutlineShader);
        DrawTexturePro(temp2.Texture,
            new Rect(0, 0, temp2.Texture.Width, temp2.Texture.Height),
            new Rect(0, 0, texture.Texture.Width, texture.Texture.Height),
            Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        UnloadRenderTexture(temp);
        UnloadRenderTexture(temp2);
    }
    
    public static void DrawTextOutlineRef(ref RenderedTexture texture, FontHandle font, float fontSize, string text, Rgba color, float padding)
    {
        //DrawTextAliasedRef(out var temp, font, fontSize, 0, text, color);
        //var s = GetFullSource(texture.Texture);
        //var temp2 = LoadRenderTexture(texture.Texture.Width, texture.Texture.Height);
        //SetShaderValue(OutlineShader, LocationOutlinePosition, [0f, 0f], UniformType.Vec2);
        //SetShaderValue(OutlineShader, LocationOutlineFullResolution, s.Size, UniformType.Vec2);
        //SetShaderValue(OutlineShader, LocationOutlineResolution, s.Size, UniformType.Vec2);
        //SetShaderValue(OutlineShader, LocationOutlineBorderwidth, 4 * Runtime.CurrentRuntime.ScaleF, UniformType.Float);
        //BeginTextureMode(temp2);
        //DrawTexturePro(temp.Texture, 
        //    new Rect(0, 0, temp.Texture.Width, temp.Texture.Height),
        //    new Rect(padding, padding, temp.Texture.Width, temp.Texture.Height),
        //    Vector2.Zero, 0, Rgba.White);
        //EndTextureMode();
        //BeginTextureMode(texture);
        //BeginShaderMode(OutlineShader);
        //DrawTexturePro(temp2.Texture,
        //    new Rect(0, 0, temp2.Texture.Width, temp2.Texture.Height),
        //    new Rect(0, 0, texture.Texture.Width, texture.Texture.Height),
        //    Vector2.Zero, 0, Rgba.White);
        //EndShaderMode();
        //EndTextureMode();
        //UnloadRenderTexture(temp);
        //UnloadRenderTexture(temp2);
    }

    public static void DrawTextGradient(out RenderedTexture texture, FontHandle font, float fontSize, string text,
        Rgba color, float padding, float borderWidth = -1)
    {
        DrawTextOutline(out var temp, font, fontSize, text, color, padding, borderWidth);
        texture = LoadRenderTexture(temp.Texture.Width, temp.Texture.Height);
        BeginTextureMode(texture);
        BeginShaderMode(TextGradientShader);
        DrawTexture(temp.Texture, 0, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        // Bilinear so the baked text stays smooth (anti-aliased) when the UI scales it up/down; render textures
        // default to point/nearest, which makes scaled menu text jagged.
        SetTextureFilter(texture.Texture, FilterMode.Bilinear);
        UnloadRenderTexture(temp);
    }

    public static void DrawTextAliased(out RenderedTexture texture, 
#if DEBUG
        out RenderedTexture unscaled,
#endif
        FontHandle font, float fontSize, float spacing, string text, Rgba color)
    {
        var measure = MeasureTextEx(font, text, fontSize * 4, spacing);
        var tmp = LoadRenderTexture((int)measure.X, (int)measure.Y);
        SetShaderValue(AAShader, LocationAAResolution, measure, UniformType.Vec2);
        SetShaderValue(AAShader, LocationAAScale, 4, UniformType.Int);
        BeginTextureMode(tmp);
        DrawTextEx(font, text, Vector2.Zero, fontSize * 4, spacing, color);
        EndTextureMode();
        texture = LoadRenderTexture((int)measure.X / 4, (int)measure.Y / 4);
        BeginTextureMode(texture);
        BeginShaderMode(AAShader);
        DrawTexture(tmp.Texture, 0, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
#if DEBUG
        unscaled = tmp;
#else
        UnloadRenderTexture(tmp);
#endif
    }

    private static RenderedTexture AlliasTextureTemp = LoadRenderTexture(8192, 8192);
    
    public static void DrawTextAliasedRef(ref RenderedTexture texture,
        FontHandle font, float fontSize, float spacing, string text, Rgba color)
    {
        var measure = MeasureTextEx(font, text, fontSize * 4, spacing);
        var tmp = LoadRenderTexture((int)measure.X, (int)measure.Y);
        SetShaderValue(AAShader, LocationAAResolution, measure, UniformType.Vec2);
        SetShaderValue(AAShader, LocationAAScale, 4, UniformType.Int);
        BeginTextureMode(tmp);
        DrawTextEx(font, text, Vector2.Zero, fontSize * 4, spacing, color);
        EndTextureMode();
        BeginTextureMode(texture);
        BeginShaderMode(AAShader);
        DrawTexture(tmp.Texture, 0, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
    }

    public static void DrawTextAliasedA(out RenderedTexture texture, FontHandle font, float fontSize, float spacing, string text, Rgba color)
    {
        // DrawTextAliased only takes the `unscaled` output in DEBUG (the texture previewer wants it), so the
        // call has to match — passing it in Release did not compile, which is why no Release build worked.
#if DEBUG
        RenderedTexture unscaled = new RenderedTexture();
        DrawTextAliased(out texture, out unscaled, font, fontSize, spacing, text, color);
        UnloadRenderTexture(unscaled);
#else
        DrawTextAliased(out texture, font, fontSize, spacing, text, color);
#endif
    }
    
    public static void DrawTimerSplash(RenderedTexture renderTexture, int ticks, double time)
    {
        var secondsFontSize = (int)(SplashTimerSize * Runtime.CurrentRuntime.ScaleF);
        var millsFonsSize = (int)(SplashTimerMillsSize * Runtime.CurrentRuntime.ScaleF);
        var padding = 2 * Runtime.CurrentRuntime.ScaleF;
        var texture = Runtime.CurrentRuntime.Textures["timer-prerender.png"];
        var source = GetFullSource(texture);
        string gameSecondsStr = $"{Math.Floor((float)ticks/60):000}";
        string gameMillsStr = $".{ticks * 100 / 60 % 100:00}bl";
        string realSecondsStr = $"{Math.Floor(time):000}";
        string realMillsStr = $".{Math.Floor(time * 100 % 100):00}bl";
        var gameSecondsSize = MeasureTextEx(TimerFont, gameSecondsStr, secondsFontSize, 0);
        var gameMillsSize = MeasureTextEx(TimerFont, gameMillsStr,millsFonsSize, 0);
        var realSecondsSize = MeasureTextEx(TimerFont, realSecondsStr, secondsFontSize, 0);
        var realMillsSize = MeasureTextEx(TimerFont, realMillsStr, millsFonsSize, 0);
        var gameTexture = LoadRenderTexture(
            (int)(gameSecondsSize.X + gameMillsSize.X + padding * 2),
            (int)(gameSecondsSize.Y + padding * 2)
        );
        var gameTextureApply = LoadRenderTexture(gameTexture.Texture.Width, gameTexture.Texture.Height);
        var realTexture = LoadRenderTexture(
            (int)(realSecondsSize.X + realMillsSize.X + padding * 2),
            (int)(realSecondsSize.Y + padding * 2));
        var realTextureApply = LoadRenderTexture(
            (int)(realSecondsSize.X + realMillsSize.X + padding * 2),
            (int)(realSecondsSize.Y + padding * 2));
        var gameSource = GetFullSourceRenderTexture(gameTexture);
        var realSource = GetFullSourceRenderTexture(realTexture);
        BeginTextureMode(gameTexture);
        DrawTextPro(TimerFont, gameSecondsStr, new Vector2(padding), Vector2.Zero, 0, secondsFontSize, 0, Rgba.White);
        DrawTextPro(TimerFont, gameMillsStr, new Vector2(padding +gameSecondsSize.X, (padding*.75f)-gameMillsSize.Y+gameSecondsSize.Y), Vector2.Zero, 0, millsFonsSize, 0, Rgba.White);
        EndTextureMode();
        BeginTextureMode(realTexture);
        DrawTextPro(TimerFont, realSecondsStr, new Vector2(padding), Vector2.Zero, 0, secondsFontSize, 0, Rgba.White);
        DrawTextPro(TimerFont, realMillsStr, new Vector2(padding +realSecondsSize.X, (padding*.75f)-realMillsSize.Y+realSecondsSize.Y), Vector2.Zero, 0, millsFonsSize, 0, Rgba.White);
        EndTextureMode();
        BeginTextureMode(gameTextureApply);
        ClearBackground(Rgba.Black with {A=0});
        SetShaderValue(OutlineShader, LocationOutlinePosition, [0,0], UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineResolution, gameSource.Size * new Vector2(1,1), UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineFullResolution, gameSource.Size * new Vector2(1,1), UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineBorderwidth, Runtime.CurrentRuntime.ScaleF * 4f, UniformType.Float);
        BeginShaderMode(OutlineShader);
        DrawTexture(gameTexture.Texture, 0, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        BeginTextureMode(realTextureApply);
        ClearBackground(Rgba.Black with {A=0});
        SetShaderValue(OutlineShader, LocationOutlineResolution, realSource.Size * new Vector2(1,-1), UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineFullResolution, realSource.Size * new Vector2(1,-1), UniformType.Vec2);
        BeginShaderMode(OutlineShader);
        DrawTexture(realTexture.Texture, 0, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        BeginTextureMode(renderTexture);
        ClearBackground(Rgba.Black with {A=0});
        DrawTexture(gameTextureApply.Texture, renderTexture.Texture.Width-gameTextureApply.Texture.Width, (int)(10 * Runtime.CurrentRuntime.ScaleF), Rgba.White);
        DrawTexture(realTextureApply.Texture, renderTexture.Texture.Width-realTextureApply.Texture.Width, (int)(60 * Runtime.CurrentRuntime.ScaleF), Rgba.White);
        DrawTexturePro(texture, source, new Rect(
            0, 0, source.Size / 4 * Runtime.CurrentRuntime.ScaleF
            ), Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        UnloadRenderTexture(gameTexture);
        UnloadRenderTexture(gameTextureApply);
        UnloadRenderTexture(realTexture);
        UnloadRenderTexture(realTextureApply);
    }

    public static void DrawSpellScore(string scoreText, ref RenderedTexture renderTexture2D, out float letterWidth, out float textWidth)
    {
        var fontSize = (int)(SplashTimerSize * Runtime.CurrentRuntime.ScaleF);
        var measure = MeasureTextEx(TimerFont, scoreText, fontSize, 0);
        textWidth = measure.X;
        letterWidth = measure.X / scoreText.Length;
        var tmp = LoadRenderTexture(
            (int)(measure.X + 32), 
            (int)(measure.Y + 32)
        );
        var fullSource = GetFullSource(tmp.Texture);
        var fullSource2 = GetFullSource(renderTexture2D.Texture);
        Vector2 v = new((renderTexture2D.Texture.Width - fullSource.Width) / 2,
            (128 * Runtime.CurrentRuntime.ScaleF));
        BeginTextureMode(tmp);
        DrawTextPro(TimerFont, scoreText, new Vector2(16), Vector2.Zero, 0, fontSize, 0, Rgba.White);
        EndTextureMode();
        SetShaderValue(OutlineShader, LocationOutlineFullResolution, fullSource2.Size, UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineResolution, fullSource.Size, UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlinePosition, v, UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineBorderwidth, Runtime.CurrentRuntime.ScaleF * 4f, UniformType.Float);
        BeginTextureMode(renderTexture2D);
        BeginShaderMode(OutlineShader);
        DrawTexturePro(tmp.Texture, GetFullSourceRenderTexture(tmp), fullSource with { X = v.X, Y = v.Y }, Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        EndShaderMode();
        UnloadRenderTexture(tmp);
    }

    public static void PrepareTimer(int ticks)
    {
        // Seconds plus the 1/100 fraction. Ticks are 60/sec, so hundredths = ticks%60 * 100/60 (the same idiom
        // the end-of-card splash uses). Clamp to >=0 first so the modulo never goes negative near time-out.
        int clamped = Math.Max(0, ticks);
        string text = $"{Math.Clamp(clamped / 60, 0, 99):00}.{clamped % 60 * 100 / 60:00}";
        BeginTextureMode(TempTimerTexture);
        ClearBackground(Rgba.Black with {A=0});
        DrawTextPro(TimerFont, text, TimerPos,
            Vector2.Zero, 0, TimerFontSize, TimerFontSpacing, Rgba.White);
        EndTextureMode();
        SetShaderValue(OutlineShader, LocationOutlinePosition, [0,0], UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineBorderwidth, Runtime.CurrentRuntime.ScaleF * 4, UniformType.Float);
        SetShaderValue(OutlineShader, LocationOutlineResolution, TimerTextureSize, UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineFullResolution, TimerTextureSize, UniformType.Vec2);
        BeginTextureMode(TempTimerTexture2);
        ClearBackground(Rgba.White with {A=0});
        BeginShaderMode(OutlineShader);
        DrawTexture(TempTimerTexture.Texture, 0, 0,Rgba.White);
        EndShaderMode();
        EndTextureMode();
    }
    
    public static void DrawTimer(int x, int y, bool isTimingOut)
    {
        DrawTexture(TempTimerTexture2.Texture, x,y,isTimingOut ? Rgba.Red : Rgba.White);
    }

    public static RenderedTexture DrawDialog(string text, float angle)
    {
        var tx = DrawText(text, 16, 4, 4, 2, GetFontDefault(), Rgba.Black, "shadow");
        var vx = RenderTextureInCloud(tx.Texture, 3f, angle);
        UnloadRenderTexture(tx);
        return vx;
    }

    static int LocationFlipScreenSize;

    public static Vector4 ColorToVector(Rgba color) => HelperPure.ColorToVector(color);

    public static Rect Mix(Rect rc1, Rect rc2, float mix) => HelperPure.Mix(rc1, rc2, mix);

    public static float Mix(float f1, float f2, float mix) => HelperPure.Mix(f1, f2, mix);

    public static Vector4 Mix(Vector4 color1, Vector4 color2, float mix) => HelperPure.Mix(color1, color2, mix);

    public static Rgba Mix(Rgba color1, Rgba color2, float mix) => HelperPure.Mix(color1, color2, mix);
    ///<summary>
    /// Computes object time
    /// </summary>
    public static double ComputeObjectTime(double time, double start, double appearLength, double end, double disappearLength) =>
        HelperPure.ComputeObjectTime(time, start, appearLength, end, disappearLength);

    public static float ComputeObjectTime(float time, float start, float appearLength, float end, float disappearLength) =>
        HelperPure.ComputeObjectTime(time, start, appearLength, end, disappearLength);

    public static float ComputeObjectTime(int time, int start, int appearLength, int end, int disappearLength) =>
        HelperPure.ComputeObjectTime(time, start, appearLength, end, disappearLength);

    public static float ComputeObjectTime0To2(float time, float start, float appearLength, float end,
        float disappearLength) => HelperPure.ComputeObjectTime0To2(time, start, appearLength, end, disappearLength);

    public static double ComputeObjectTimeStart(double time, double start, double appearLength) =>
        HelperPure.ComputeObjectTimeStart(time, start, appearLength);

    public static byte TimeToTransparency(double time) => HelperPure.TimeToTransparency(time);

    public static float Pow2F(float x) => HelperPure.Pow2F(x);

    public static float EaseInOutElasticF(float x) => HelperPure.EaseInOutElasticF(x);

    public static int Vector3ColorToInt(Vector3 vector) => HelperPure.Vector3ColorToInt(vector);

    public static Vector3 ColorIntToVector3(int color) => HelperPure.ColorIntToVector3(color);
    
    public static RenderedTexture DrawTextScaled(string s, int fontSize, int hPadding, int vPadding, int spacing, FontHandle font, string shader = "shadow") => DrawText(s, 
        (int)(fontSize*Runtime.CurrentRuntime.Scale), 
        (int)(hPadding*Runtime.CurrentRuntime.Scale), 
        (int)(vPadding*Runtime.CurrentRuntime.Scale), 
        (int)(spacing*Runtime.CurrentRuntime.Scale),
        font, 
        Rgba.White,
        shader,
        Runtime.CurrentRuntime.ScaleF);
    public static RenderedTexture DrawText(string s, int fontSize, int hPadding, int vPadding, int spacing, FontHandle font, string shader = "shadow", float scale = 1f) => 
        DrawText(s, fontSize, hPadding, vPadding, spacing, font, Rgba.White, shader, scale);

    public static void DrawTextOnRenderTextureWithoutReinitialization(ref RenderedTexture texture, 
        Vector2 pos,
        string s, int fontSize,
        int spacing, FontHandle font, Rgba color,
        string shader, float scale = 1f)
    {
        int sFontSize = (int)(fontSize * scale);
        int sSpacing = (int)(spacing * scale);
        var measure = MeasureTextEx(font, s, sFontSize, sSpacing);
        RenderedTexture temp = LoadRenderTexture((int)measure.X+8, (int)measure.Y+8);
        RenderedTexture temp2 = LoadRenderTexture((int)measure.X+8, (int)measure.Y+8);
        Rect source = new(0, -temp2.Texture.Height, temp2.Texture.Width, -temp2.Texture.Height);
        Rect destination = new(pos - new Vector2(4), source.Size * new Vector2(1, -1));
        BeginTextureMode(temp);
        DrawTextEx(font, s, new Vector2(4, 4), fontSize, sSpacing, color);
        EndTextureMode();
        switch (shader)
        {
            case "shadow":
                SetShaderValue(Runtime.CurrentRuntime.Shaders["shadow"], LocationShadowDepth, 4f, UniformType.Float);
                SetShaderValue(Runtime.CurrentRuntime.Shaders["shadow"], LocationShadowResolution, measure + new Vector2(8,8), UniformType.Vec2);
                break;
            case "gradient":
                SetShaderValue(Runtime.CurrentRuntime.Shaders["gradient"], LocationGradientBorderWidth, 2f, UniformType.Float);
                SetShaderValue(Runtime.CurrentRuntime.Shaders["gradient"], LocationGradientResoulution,  measure + new Vector2(8,8), UniformType.Vec2);
                break;
        }
        BeginTextureMode(temp2);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders[shader]);
        DrawTexture(temp.Texture, 0, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        BeginTextureMode(texture);
        DrawTexturePro(temp2.Texture,
            source, destination, Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        UnloadRenderTexture(temp);
        UnloadRenderTexture(temp2);
    }
    
    public static void DrawTextOnRenderTexture(ref RenderedTexture texture, string s, int fontSize, int hPadding, int vPadding, int spacing, FontHandle font, Rgba color, string shader, float scale = 1f)
    {
        if(IsRenderTextureValid(texture))
            UnloadRenderTexture(texture);
        var measure = MeasureTextEx(font, s, fontSize, spacing);
        int width = (int)(measure.X + hPadding * 2);
        int height = (int)(measure.Y + vPadding * 2);
        RenderedTexture temp = LoadRenderTexture(width, height);
        texture = LoadRenderTexture(width, height);
        BeginTextureMode(temp);
        DrawTextEx(font, s, new Vector2(hPadding, vPadding), fontSize, spacing, color);
        EndTextureMode();
        switch (shader)
        {
            case "shadow":
                SetShaderValue(Runtime.CurrentRuntime.Shaders["shadow"], LocationShadowDepth, 4f, UniformType.Float);
                SetShaderValue(Runtime.CurrentRuntime.Shaders["shadow"], LocationShadowResolution, new float[] { width, height }, UniformType.Vec2);
                break;
            case "gradient":
                SetShaderValue(Runtime.CurrentRuntime.Shaders["gradient"], LocationGradientBorderWidth, scale * 2f, UniformType.Float);
                SetShaderValue(Runtime.CurrentRuntime.Shaders["gradient"], LocationGradientResoulution, new Vector2(width,height), UniformType.Vec2);
                break;
            case "outline":
                SetShaderValue(Runtime.CurrentRuntime.Shaders["outline"], LocationGradientBorderWidth, scale * 3f, UniformType.Float);
                SetShaderValue(Runtime.CurrentRuntime.Shaders["outline"], GetShaderLocation(Runtime.CurrentRuntime.Shaders["outline"], "res"), new Vector2(width,height), UniformType.Vec2);
                break;
        }
        BeginTextureMode(texture);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders[shader]);
        DrawTexture(temp.Texture, 0, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        // Bilinear so the baked text is anti-aliased when the UI scales it (render textures default to point).
        SetTextureFilter(texture.Texture, FilterMode.Bilinear);
        UnloadRenderTexture(temp);
    }

    private static RenderedTexture ScoreDigits;
    public static Vector2 ScoreDigitSize;
    
    public static RenderedTexture DrawText(string s, int fontSize, int hPadding, int vPadding, int spacing, FontHandle font, Rgba color, string shader, float scale = 1f)
    {
        RenderedTexture texture = new RenderedTexture();
        DrawTextOnRenderTexture(ref texture, s, fontSize, hPadding, vPadding, spacing, font, color, shader, scale);
        return texture;
    }

    public static Rect GetFullSource(BasicTexture t) => new Rect(0, 0, t.Width, t.Height);
    public static Rect GetFullSourceRenderTexture(RenderedTexture rt2d) => new Rect(0, rt2d.Texture.Height, rt2d.Texture.Width, -rt2d.Texture.Height);

    public static Rect GetFullscreenSource() => new Rect(0, 0, Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height);

    public static Rect ScaleByHeight(float middle, float y, Vector2 size, float newHeight)
    {
        float mp = newHeight / size.Y;
        return new Rect(middle, y, mp * size.X, newHeight);
    }

    public static Rect Scale(Rect rc, double scale)
    {
        return Scale(rc, (float)scale);
    }

    public static Rect Scale(Rect rc, float scale)
    {
        return new Rect(rc.Position * scale, rc.Size * scale);
    }

    private static int LocationRenderSelectionScreenSize;
    private static int LocationRenderSelectionHeight;
    
    public static BasicTexture RenderSelectionBackground(int width, int height, int vPadding)
    {
        int h = height + vPadding * 2;
        RenderedTexture texture = LoadRenderTexture(width, h);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["selection"], LocationRenderSelectionHeight, (float)height, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["selection"], LocationRenderSelectionScreenSize, new float[] { 200f, 200f }, UniformType.Vec2);
        BeginTextureMode(texture);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["selection"]);
        DrawRectanglePro(new Rect(0,0,width,height), Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        return texture.Texture;
    }

    /// <summary>
    /// Bakes the complaints box of Dmitry's fourth stage-3 card — the "GrievanceBox" texture that
    /// EntityVisuals/grievance_box.json draws. The panel itself is the grievance_box shader; the label
    /// (ЖАЛОБЫ, typed with the Latin lookalikes the game's font wants) is text laid over it. Two passes like
    /// DrawText and the bullet atlas: content into a scratch target, then that target copied into the one that
    /// is kept, so a positive source rect reads it upright.
    /// </summary>
    public static BasicTexture RenderGrievanceBox(int width, int height)
    {
        var shader = Runtime.CurrentRuntime.Shaders["grievance_box"];
        SetShaderValue(shader, GetShaderLocation(shader, "resolution"), new Vector2(width, height), UniformType.Vec2);
        RenderedTexture label = DrawText(")|(AJlo6bI", 20, 2, 2, 1, Runtime.CurrentRuntime.Fonts["kodemono"],
            new Rgba(236, 206, 120, 255), "shadow");
        RenderedTexture scratch = LoadRenderTexture(width, height);
        BeginTextureMode(scratch);
        ClearBackground(Rgba.Black with { A = 0 });
        BeginShaderMode(shader);
        DrawRectanglePro(new Rect(0, 0, width, height), Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        // Centred on the plate, below the slot (which sits 24px above the middle).
        float labelX = (width - label.Texture.Width) / 2f;
        float labelY = height * 0.5f - 6f;
        DrawTexturePro(label.Texture, new Rect(0, 0, label.Texture.Width, label.Texture.Height),
            new Rect(labelX, labelY, label.Texture.Width, label.Texture.Height), Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        RenderedTexture texture = LoadRenderTexture(width, height);
        BeginTextureMode(texture);
        ClearBackground(Rgba.Black with { A = 0 });
        DrawTexture(scratch.Texture, 0, 0, Rgba.White);
        EndTextureMode();
        SetTextureFilter(texture.Texture, FilterMode.Bilinear);
        UnloadRenderTexture(scratch);
        UnloadRenderTexture(label);
        return texture.Texture;
    }

    public static void OpenWebPage(string url)
    {
#if ANDROID
        var ctx = Application.Context;
        var intent = new Intent(Intent.ActionView, Uri.Parse(url));
        intent.AddFlags(ActivityFlags.NewTask);
        ctx.StartActivity(intent);
#else
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            SDProcess.Start(new ProcessStartInfo("cmd", $"/c start {url}" ) { CreateNoWindow = true });
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            SDProcess.Start(new ProcessStartInfo("xdg-open", url));
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            throw new Exception("kupi komp normalniy");
        else if(RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
            try { SDProcess.Start("xdg-open", url); }
            catch {SDProcess.Start("open", url); }
        else
            throw new Exception("nJlaTqpopMa He noDDep)!(uBaeTc9I");
#endif
        
        
    }
    
    public static bool HasNvidiaDriverFile()
    {
        if (Environment.OSVersion.Platform == PlatformID.Unix)
            return File.Exists("/proc/driver/nvidia/version");
        else return false;
    }

    private static int LocationLiquidGlassTime = -1;
    private static int LocationLiquidGlassRes = -1;
    private static int LocationLiquidGlassPosition = -1;
    private static int LocationLiquidGlassSize = -1;
    private static int LocationLiquidGlassRadius = -1;
    private static int LocationLiquidGlassTint = -1;

    /// <summary>Draws an Apple-style "liquid glass" rounded panel at <paramref name="rect"/>: a clear pane
    /// over <paramref name="capturedBackground"/> — a full-screen capture of whatever is drawn behind the
    /// panel this frame (see <see cref="Screens.ListSelectScreen"/> for how that capture is produced) — with
    /// a lens-refracted rim (chromatic dispersion included), a top-left specular streak, a hairline border,
    /// an inner shadow and a soft drop shadow. Draws a full-screen quad; the shader itself masks to the
    /// rounded rect (plus shadow) and discards elsewhere, so it is safe to call with the real backbuffer
    /// still bound.</summary>
    public static void DrawLiquidGlassPanel(RenderedTexture capturedBackground, Rect rect, float cornerRadius, Rgba tint)
    {
        var shader = Runtime.CurrentRuntime.Shaders["liquid_glass"];
        if (LocationLiquidGlassTime < 0)
        {
            LocationLiquidGlassTime = GetShaderLocation(shader, "time");
            LocationLiquidGlassRes = GetShaderLocation(shader, "res");
            LocationLiquidGlassPosition = GetShaderLocation(shader, "position");
            LocationLiquidGlassSize = GetShaderLocation(shader, "size");
            LocationLiquidGlassRadius = GetShaderLocation(shader, "radius");
            LocationLiquidGlassTint = GetShaderLocation(shader, "tint");
        }
        SetShaderValue(shader, LocationLiquidGlassTime, (float)GetTime(), UniformType.Float);
        SetShaderValue(shader, LocationLiquidGlassRes, new[] { (float)Runtime.CurrentRuntime.Width, (float)Runtime.CurrentRuntime.Height }, UniformType.Vec2);
        SetShaderValue(shader, LocationLiquidGlassPosition, new[] { rect.X, rect.Y }, UniformType.Vec2);
        SetShaderValue(shader, LocationLiquidGlassSize, new[] { rect.Width, rect.Height }, UniformType.Vec2);
        SetShaderValue(shader, LocationLiquidGlassRadius, cornerRadius, UniformType.Float);
        SetShaderValue(shader, LocationLiquidGlassTint, new[] { tint.R / 255f, tint.G / 255f, tint.B / 255f, tint.A / 255f }, UniformType.Vec4);
        BeginShaderMode(shader);
        DrawTexturePro(capturedBackground.Texture, GetFullSourceRenderTexture(capturedBackground), GetFullscreenSource(), Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
    }

    public static RenderedTexture FillTextureWithColor(Rgba color, int w, int h)
    {
        var texture = LoadRenderTexture(w, h);
        BeginTextureMode(texture);
        DrawRectangle(0,0,w,h,color);
        EndTextureMode();
        return texture;
    }

    public static float FindAngle(Vector2 v1, Vector2 v2) => MathF.Atan2((v2-v1).Y, (v2-v1).X);

    public static float FindAngleDegrees(Vector2 v1, Vector2 v2) => FindAngle(v1, v2) * 180 / MathF.PI;

    public static float ToRadians(float degree) => degree * MathF.PI / 180f;

    public static Vector2 GetDirection(Vector2 v1, Vector2 v2) => GetDirection(FindAngle(v1, v2));
    
    public static Vector2 GetDirection(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));
    public static Vector2 GetDirection2(float angle) => GetDirection(angle + MathF.PI / 2);

    private static int LocationDisappearShootPosition;
    private static int LocationDisappearShootTime;
    
    //public static void DrawDeathPoints(List<RemovedBullet> objects, string shader)
    //{
    //    float time = (float)GetTime();
    //    foreach (var obj in objects)
    //    {
    //        SetShaderValue(Runtime.CurrentRuntime.Shaders[shader], LocationDisappearShootTime, time - obj.Time, UniformType.Float);
    //        SetShaderValue(Runtime.CurrentRuntime.Shaders[shader], LocationDisappearShootPosition, obj.Position, UniformType.Vec2);
    //        BeginShaderMode(Runtime.CurrentRuntime.Shaders[shader]);
    //        DrawRectangle(0,0,384,448,Rgba.White);
    //        EndShaderMode();
    //    }
    //}

    public static Vector2 Half = Vector2.One / 2;

    public static bool IsInArea(Vector2 xPositionTo, Vector2 areaStart, Vector2 areaEnd) =>
        HelperPure.IsInArea(xPositionTo, areaStart, areaEnd);

    /// <summary>Pizzics' one primitive: two rects overlap if their centres are closer than their half-widths
    /// added together — i.e. they are treated as circles, which is what every collision in the game wants.</summary>
    public static bool IsCollied(Rect rc1, Rect rc2) => HelperPure.IsCollied(rc1, rc2);
    
    public static double BossAppearCurve(double x, double pow)
    {
        return (Math.Pow(x/2 - 1, pow) + 1) / 2;
    } 
    
    public static float BossAppearCurveF(float x, float pow)
    {
        return (MathF.Pow(x/2 - 1, pow) + 1) / 2;
    }
    
    /// <summary>
    /// Plays a one-shot. Alias/ring-buffer handling now lives in the backend (IAudio.Play), which also
    /// fixes the old bug here: this stored the original sound rather than the alias it created, so
    /// UnloadSoundAlias was later handed a non-alias.
    /// </summary>
    public static void PlaySound(SoundHandle sound)
    {
        Engine.Audio.SfxVolume = Runtime.CurrentRuntime.SFXVolume;
        Engine.Audio.Play(sound);
    }

    private const int AliasCount = 4096;
    private static int AliasIndex = 0;
    private static bool RequiresUnloading = false;
    private static SoundHandle[] SoundAlieases = new SoundHandle[4096];
    
    // The dictionaries live in HelperPure so the translation data is reachable without a GPU backend (Helper's
    // own static constructor allocates render textures); these properties keep Helper's callers unchanged.
    static Dictionary<string, string> TransliterationDictionary => HelperPure.TransliterationDictionary;
    static Dictionary<string, string> TranslationDictionary => HelperPure.TranslationDictionary;

    public static string Translate(string j57v)
    {
        if (TranslationDictionary.ContainsKey(j57v))
        {
            var translitions = TranslationDictionary[j57v].Split(";");
            return Transliterate(translitions[GetRandomValue(0, translitions.Length - 1)]);
        }
        return Transliterate(j57v);
    }


    /// <summary>
    /// Checks if translitions has transltion, if it has - translates
    /// </summary>
    /// <param name="j57v">string to translate</param>
    /// <param name="translition">returns translition string if translition not found, otherwise - translition</param>
    /// <returns></returns>
    public static bool HasTranslition(string j57v, out string translition)
    {
        translition = j57v;
        if (TranslationDictionary.ContainsKey(j57v))
            return false;
        var translitions = TranslationDictionary[j57v].Split(";");
        translition = Transliterate(translitions[GetRandomValue(0, translitions.Length - 1)]);
        return true;
    }

    /// <summary>
    /// Translates every word splited by space in string via HasTranslition(string, string)
    /// </summary>
    public static string TranslateEachWord(string j67v)
    {
        return string.Join(" ", j67v.ToLower().Split(" ")
            .Select(x => Translate(x)));
    }

    /// <summary>True if translation.json carries an entry for this key.</summary>
    public static bool HasTranslation(string key) => HelperPure.HasTranslation(key);

    /// <summary>
    /// Resolves a key through translation.json (picking one of its <c>;</c>-separated variants) but does NOT
    /// transliterate — for callers that transliterate the text themselves (the spell-card / chapter title
    /// renderers do). Returns the key unchanged when it is not a translation entry.
    /// </summary>
    public static string TranslateRaw(string key)
    {
        if (TranslationDictionary.ContainsKey(key))
        {
            var variants = TranslationDictionary[key].Split(";");
            return variants[GetRandomValue(0, variants.Length - 1)];
        }
        return key;
    }

    /// <summary>
    /// Translates a GPU name word by word: the name is lowercased and split on spaces, and every word is
    /// replaced with its <c>benchmark.gpu.&lt;word&gt;</c> entry — "AMD Radeon RX 580" becomes
    /// "amd radeon rx 580" and then a row of lookups. A word with no entry is kept as-is (Translate alone
    /// would transliterate the miss, mangling a perfectly good Latin word).
    /// </summary>
    public static string TranslateGpuName(string name)
    {
        string[] words = name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < words.Length; i++)
            if (HasTranslation($"benchmark.gpu.{words[i]}"))
                words[i] = Translate($"benchmark.gpu.{words[i]}");
        return string.Join(' ', words);
    }

    // Suffixes for per-difficulty spell-card names, index 0..4 = Easy, Normal, Hard, Max, Extra (the order the
    // difficulty menu offers). See PersonPlayerData.DifficultyCount.
    private static readonly string[] DifficultyKeys = { "easy", "normal", "hard", "max", "extra" };

    /// <summary>
    /// The spell-card name to DISPLAY, from translation.json and optionally specialised per difficulty. Tries
    /// "&lt;title&gt;.&lt;difficulty&gt;" first (e.g. <c>spell.toilet.lunatic</c>), then the plain "&lt;title&gt;",
    /// then falls back to the raw authored title so cards with a plain name keep working. The result is NOT
    /// transliterated (the title renderer does that) and the underlying <see cref="Data.Archive.FileChapterInfo.SpellcardTitle"/>
    /// is untouched — it stays the stable key PlayerData records tries/successes under, the same across every difficulty.
    /// </summary>
    public static string ResolveSpellcardName(string title, int difficulty)
    {
        if (string.IsNullOrEmpty(title))
            return title ?? "";
        if (difficulty >= 0 && difficulty < DifficultyKeys.Length)
        {
            string keyed = title + "." + DifficultyKeys[difficulty];
            if (HasTranslation(keyed))
                return TranslateRaw(keyed);
        }
        return HasTranslation(title) ? TranslateRaw(title) : title;
    }

    /// <summary>
    /// The name a spell card shows at a given difficulty, keyed by its GLOBAL card number:
    /// <c>spell.card.&lt;number&gt;.&lt;difficulty&gt;</c> (e.g. <c>spell.card.2.hard</c>). Returns null when no
    /// such entry exists — the caller treats that as "this card does not offer that difficulty". The result is
    /// raw (not transliterated); the caller transliterates when it draws.
    /// </summary>
    public static string? SpellcardDifficultyName(int number, int difficulty)
    {
        if (difficulty < 0 || difficulty >= DifficultyKeys.Length)
            return null;
        string diffId = DifficultyKeys[difficulty];
        // Exact key wins; otherwise honour an underscore-combined suffix that lists this tier — e.g.
        // "spell.card.1.hard_max" supplies one shared name for both Hard and Max.
        string exact = $"spell.card.{number}.{diffId}";
        if (HasTranslation(exact))
            return TranslateRaw(exact);
        string prefix = $"spell.card.{number}.";
        foreach (var kv in TranslationDictionary)
        {
            if (!kv.Key.StartsWith(prefix))
                continue;
            if (Array.IndexOf(kv.Key.Substring(prefix.Length).Split('_'), diffId) >= 0)
                return TranslateRaw(kv.Key);
        }
        return null;
    }

    /// <summary>
    /// The PlayerData key a spell card's tries/successes are recorded under, now per difficulty so each tier
    /// keeps its own attempts/captures. A plain title (no difficulty) stays the base key for older records.
    /// </summary>
    public static string SpellRecordKey(string title, int difficulty) => $"{title}#d{difficulty}";

    public static string Transliterate(string text) => HelperPure.Transliterate(text);

    public static void UpdatePlayingMusic()
    {
        throw new NotImplementedException();
    }

    public static Vector2 GetSize(BasicTexture texture)
    {
        return new Vector2(texture.Width, texture.Height);
    }
#if DEBUG
    public static void ReprepareTimerShader()
    {
        OutlineShader = Runtime.CurrentRuntime.Shaders["outline"];
        LocationOutlineBorderwidth = GetShaderLocation(OutlineShader, "border_width");
        LocationOutlineResolution =  GetShaderLocation(OutlineShader, "res");
    }
    #endif
}

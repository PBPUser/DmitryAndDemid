using DmitryAndDemid.Rendering;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DmitryAndDemid.Data;
using DmitryAndDemid.Gameplay;
using Microsoft.CSharp.RuntimeBinder;
using Pango;
using static DmitryAndDemid.Rendering.Gfx;

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

    public static bool GetResolutionFromString(string str, out (int width, int height) res)
    {
        res = (0, 0);
        var split = str.Split("x");
        if (split.Length < 2)
            return false;
        if (!int.TryParse(split[0], out res.width))
            return false;
        return int.TryParse(split[1], out res.height);
    }

    public static bool GetMultiplyerFromRes(string str, out double multiplyer)
    {
        multiplyer = 0;
        (int width, int height) res;
        if (!GetResolutionFromString(str, out res))
            return false;
        multiplyer = ((double)res.width) / 640d;
        return true;
    }

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

    public static void DrawBossText(TargetHandle texture, string text)
    {
        string transliterate = Transliterate(text);
        TargetHandle temp = LoadRenderTexture(texture.Texture.Width,  texture.Texture.Height);
        BeginTextureMode(temp);
        DrawTextEx(GetFontDefault(),
            transliterate,
            Vector2.Zero,
            BossTextFontSize * Runtime.CurrentRuntime.ScaleF,
            Runtime.CurrentRuntime.ScaleF, BossTextColor);
        EndTextureMode();
        BeginTextureMode(texture);
        Rect rc = new(0, 0, temp.Texture.Width, temp.Texture.Height);
        Rect rc2 = new(0, temp.Texture.Height, temp.Texture.Width, temp.Texture.Height);
        DrawTexturePro(temp.Texture, rc2,   rc, Vector2.Zero, 0, Rgba.White);
        EndTextureMode();
        UnloadRenderTexture(temp);
    }

    public static void DrawChapterTitleText(TargetHandle texture, string text)
    {
        string transliterate = Transliterate(text);
        TargetHandle temp = LoadRenderTexture(texture.Texture.Width,  texture.Texture.Height);
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
        Rect rc = new(0, 0, temp.Texture.Width, temp.Texture.Height);
        Rect rc2 = new(0, temp.Texture.Height, temp.Texture.Width, temp.Texture.Height);
        DrawTexturePro(temp.Texture, rc2,   rc, Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
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
    
    public static TargetHandle RenderTextureInCloud(TextureHandle texture, float radius = 3f, float angle = -0.85f, float width = 0.35f, float size = 1.4f)
    {
        TargetHandle cloud = LoadRenderTexture(texture.Width * 2, texture.Height * 2);
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
    private static TargetHandle TempTimerTexture, TempTimerTexture2;
    private static Rect TimerRectangleSource, TimerRectangleTarget;
    private static FontHandle TimerFont = Runtime.CurrentRuntime.Fonts["kodemono"];
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
        TimerTextureSize = MeasureTextEx(TimerFont, "00",  TimerFontSize, TimerFontSpacing) * 1.2f;
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

    public static string FormatScore(int score, int c)
    {
        string str = string.Join("", $"{(score == 0 ? "" : score)}{c}".Reverse());
        int spacing = ((str.Length + 2) / 3 * 3) - str.Length;
        str = str.PadRight(spacing + str.Length, 'o');
        return string.Join("",string.Join(".", Enumerable.Range(0, str.Length / 3).Select(x => str[(x*3)..(x*3+3)]))
            .Reverse()).Substring(spacing);
    }

    public static Vector2 GetScoreTextureSize(string text, float fontSize)
    {
        return new(
            text.Length * Runtime.CurrentRuntime.ScoreLetterWidth * (fontSize/64),
            Runtime.CurrentRuntime.ScoreLetterHeight * (fontSize/64)
            );
    }

    public static TargetHandle CreateScoreText(string text, float fontSize)
    {
        var vec2 = GetScoreTextureSize(text, fontSize);
        var texture = LoadRenderTexture((int)vec2.X, (int)vec2.Y);
        BeginTextureMode(texture);
        DrawScoreText(text, fontSize, Vector2.Zero, Rgba.White);
        EndTextureMode();
        return texture;
    }

    private static TargetHandle BonusTexture;
    private static TargetHandle SpellTexture;
    private static TargetHandle SubtitleBufferTexture;
    private static float SpellFontSize;

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
    public static int DrawSpellSubtitle(TargetHandle target, int score, int total, int success,
        int rightX = 0, int posY = 0, string failedText = "")
    {
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
        DrawTextFramed(out TargetHandle bonusTitle, TimerFont, fontSize, bonusLabel,
            new Rgba(190, 205, 255), new Rgba(120, 140, 200), Rgba.Black, border);
        DrawTextFramed(out TargetHandle bonus, TimerFont, fontSize, bonusValue,
            failed ? new Rgba(255, 150, 150) : new Rgba(255, 240, 170),
            failed ? new Rgba(200, 30, 30) : new Rgba(255, 150, 20),
            Rgba.Black, border, failed ? 0f : 0.65f);
        DrawTextFramed(out TargetHandle attemptTitle, TimerFont, fontSize, attemptLabel,
            new Rgba(190, 205, 255), new Rgba(120, 140, 200), Rgba.Black, border);
        DrawTextFramed(out TargetHandle tries, TimerFont, fontSize, triesValue,
            Rgba.White, new Rgba(170, 170, 190), Rgba.Black, border);

        float gap = 10 * Runtime.CurrentRuntime.ScaleF;
        TargetHandle[] parts = [bonusTitle, bonus, attemptTitle, tries];
        float lineWidth = parts.Sum(p => p.Texture.Width) + gap;   // one gap, between the two pairs

        float posX = rightX - lineWidth;
        // The name zooms in at up to 10x, which would fling this line off the overlay while that plays.
        posY = Math.Clamp(posY, 0, target.Texture.Height - bonus.Texture.Height);
        posX = Math.Clamp(posX, 0, Math.Max(0, target.Texture.Width - lineWidth));

        BeginTextureMode(target);
        float x = posX;
        for (int i = 0; i < parts.Length; i++)
        {
            TargetHandle part = parts[i];
            DrawTexturePro(part.Texture, GetFullSourceRenderTexture(part),
                new Rect(x, posY, part.Texture.Width, part.Texture.Height), Vector2.Zero, 0, Rgba.White);
            x += part.Texture.Width;
            if (i == 1)
                x += gap;   // space between "bonus: N" and "attempt: N/N"
        }
        EndTextureMode();

        foreach (TargetHandle part in parts)
            UnloadRenderTexture(part);
        return (int)posX;
    }

    /// <summary>
    /// Renders text with a frame (outline) and a vertical gradient, using Assets/Shaders/text_frame.fs.
    ///
    /// The frame grows OUTWARD from the glyphs, so the text is first drawn into a padded scratch target —
    /// without the padding the shader would dilate into the edge of the texture and the frame would be
    /// clipped. Pass highlightStrength > 0 for the animated sweep (used to emphasise the score during a
    /// spell card); 0 gives a static gradient.
    /// </summary>
    public static void DrawTextFramed(out TargetHandle texture, FontHandle font, float fontSize, string text,
        Rgba colorTop, Rgba colorBottom, Rgba borderColor, float borderWidth, float highlightStrength = 0f)
    {
        Vector2 measure = MeasureTextEx(font, text, fontSize, 1);
        float padding = MathF.Ceiling(borderWidth) + 2;
        Vector2 size = measure + new Vector2(padding * 2);

        TargetHandle mask = LoadRenderTexture((int)size.X, (int)size.Y);
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

        texture = LoadRenderTexture((int)size.X, (int)size.Y);
        BeginTextureMode(texture);
        ClearBackground(Rgba.Blank);
        BeginShaderMode(shader);
        DrawTexturePro(mask.Texture,
            GetFullSourceRenderTexture(mask),
            new Rect(0, 0, size.X, size.Y),
            Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();

        UnloadRenderTexture(mask);
    }

    public static void DrawTextOutline(out TargetHandle texture, FontHandle font, float fontSize, string text, Rgba color, float padding)
    {
        DrawTextAliasedA(out var temp, font, fontSize, 0, text, color);
        texture = LoadRenderTexture((int)(temp.Texture.Width + padding * 2),
            (int)(temp.Texture.Height + padding * 2));
        var s = GetFullSource(texture.Texture);
        var temp2 = LoadRenderTexture(texture.Texture.Width, texture.Texture.Height);
        SetShaderValue(OutlineShader, LocationOutlinePosition, [0f, 0f], UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineFullResolution, s.Size, UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineResolution, s.Size, UniformType.Vec2);
        SetShaderValue(OutlineShader, LocationOutlineBorderwidth, 4 * Runtime.CurrentRuntime.ScaleF, UniformType.Float);
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
    
    public static void DrawTextOutlineRef(ref TargetHandle texture, FontHandle font, float fontSize, string text, Rgba color, float padding)
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

    public static void DrawTextGradient(out TargetHandle texture, FontHandle font, float fontSize, string text,
        Rgba color, float padding)
    {
        DrawTextOutline(out var temp, font, fontSize, text, color, padding);
        texture = LoadRenderTexture(temp.Texture.Width, temp.Texture.Height);
        BeginTextureMode(texture);
        BeginShaderMode(TextGradientShader);
        DrawTexture(temp.Texture, 0, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        UnloadRenderTexture(temp);
    }

    public static void DrawTextAliased(out TargetHandle texture, 
#if DEBUG
        out TargetHandle unscaled,
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

    private static TargetHandle AlliasTextureTemp = LoadRenderTexture(8192, 8192);
    
    public static void DrawTextAliasedRef(ref TargetHandle texture,
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

    public static void DrawTextAliasedA(out TargetHandle texture, FontHandle font, float fontSize, float spacing, string text, Rgba color)
    {
        TargetHandle unscaled = new TargetHandle();
        DrawTextAliased(out texture, out unscaled, font, fontSize, spacing, text, color);
        UnloadRenderTexture(unscaled);
    }
    
    public static void DrawTimerSplash(TargetHandle renderTexture, int ticks, double time)
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

    public static void DrawSpellScore(string scoreText, ref TargetHandle renderTexture2D, out float letterWidth, out float textWidth)
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
        string text = $"{Math.Clamp(ticks/60, 0, 99):00}";
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

    public static TargetHandle DrawDialog(string text, float angle)
    {
        var tx = DrawText(text, 16, 4, 4, 2, GetFontDefault(), Rgba.Black, "shadow");
        var vx = RenderTextureInCloud(tx.Texture, 3f, angle);
        UnloadRenderTexture(tx);
        return vx;
    }

    static int LocationFlipScreenSize;

    public static Vector4 ColorToVector(Rgba color)
    {
        return new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }

    public static Rect Mix(Rect rc1, Rect rc2, float mix)
    {
        float imix = 1f - mix;
        return new Rect(
            rc1.X * imix + rc2.X * mix,
            rc1.Y * imix + rc2.Y * mix,
            rc1.Width * imix + rc2.Width * mix,
            rc1.Height * imix + rc2.Height * mix
        );
    }

    public static float Mix(float f1, float f2, float mix)
    {
        return f1 * (1 - mix) + f2 * mix;
    }

    public static Vector4 Mix(Vector4 color1, Vector4 color2, float mix)
    {
        float imix = 1f - mix;
        return new Vector4(
            color1[0] * imix + color2[0] * mix,
            color1[1] * imix + color2[1] * mix,
            color1[2] * imix + color2[2] * mix,
            color1[3] * imix + color2[3] * mix
        );
    }

    public static Rgba Mix(Rgba color1, Rgba color2, float mix)
    {
        float imix = 1f - mix;
        return new Rgba(
            (byte)(color1.R * imix + color2.R * mix),
            (byte)(color1.G * imix + color2.G * mix),
            (byte)(color1.B * imix + color2.B * mix),
            (byte)(color1.A * imix + color2.A * mix)
        );
    }
    ///<summary>
    /// Computes object time
    /// </summary>
    public static double ComputeObjectTime(double time, double start, double appearLength, double end, double disappearLength)
    {
        double timeAppear = Math.Clamp((time - start) / appearLength, 0, 1);
        double timeDisappear = Math.Clamp((end - time) / disappearLength, 0, 1);
        return timeAppear * timeDisappear;
    }

    static float Clamp(float value, float min, float max)
    {
        return MathF.Max(MathF.Min(value, max), min);
    }

    public static float ComputeObjectTime(float time, float start, float appearLength, float end, float disappearLength)
    {
        float timeAppear = Clamp((time - start) / appearLength, 0, 1);
        float timeDisappear = Clamp((end - time) / disappearLength, 0, 1);
        return timeAppear * timeDisappear;
    }

    public static float ComputeObjectTime(int time, int start, int appearLength, int end, int disappearLength)
    {
        float timeAppear = Clamp((time - start) / (float)appearLength, 0, 1);
        float timeDisappear = Clamp((end - time) / (float)disappearLength, 0, 1);
        return timeAppear * timeDisappear;
    }

    public static float ComputeObjectTime0To2(float time, float start, float appearLength, float end,
        float disappearLength)
    {
        float timeAppear = Clamp((time - start) / appearLength, 0, 1);
        float timeDisappear = Clamp((time - end) / disappearLength, 0, 1);
        return timeAppear + timeDisappear;
    }

    public static double ComputeObjectTimeStart(double time, double start, double appearLength)
    {
        return Math.Clamp((time - start) / appearLength, 0, 1);
    }

    public static byte TimeToTransparency(double time)
    {
        return (byte)(255 * time);
    }

    public static float Pow2F(float x)
    {
        return x * x;
    }

    public static float EaseInOutElasticF(float x)
    {
        float c5 = (2f * MathF.PI) / 4.5f;
        return x == 0
        ? 0
        : x == 1
        ? 1
        : x < 0.5
        ? -(MathF.Pow(2, 20 * x - 10) * MathF.Sin((20 * x - 11.125f) * c5)) / 2
        : (MathF.Pow(2, -20 * x + 10) * MathF.Sin((20 * x - 11.125f) * c5)) / 2 + 1;
    }
    
    public static int Vector3ColorToInt(Vector3 vector)
    {
        int r = (int)(0xFF * vector.X);
        int g = (int)(0xFF * vector.Y);
        int b = (int)(0xFF * vector.Z);
        return r << 16 | g << 8 | b;
    }

    public static Vector3 ColorIntToVector3(int color)
    {
        float r = (color >> 16) & 0xFF;
        float g = (color >> 8) & 0xFF;
        float b = color & 0xFF;
        return new Vector3(r / 0xFF, g / 0xFF, b / 0xFF);
    }
    
    public static TargetHandle DrawTextScaled(string s, int fontSize, int hPadding, int vPadding, int spacing, FontHandle font, string shader = "shadow") => DrawText(s, 
        (int)(fontSize*Runtime.CurrentRuntime.Scale), 
        (int)(hPadding*Runtime.CurrentRuntime.Scale), 
        (int)(vPadding*Runtime.CurrentRuntime.Scale), 
        (int)(spacing*Runtime.CurrentRuntime.Scale),
        font, 
        Rgba.White,
        shader,
        Runtime.CurrentRuntime.ScaleF);
    public static TargetHandle DrawText(string s, int fontSize, int hPadding, int vPadding, int spacing, FontHandle font, string shader = "shadow", float scale = 1f) => 
        DrawText(s, fontSize, hPadding, vPadding, spacing, font, Rgba.White, shader, scale);

    public static void DrawTextOnRenderTextureWithoutReinitialization(ref TargetHandle texture, 
        Vector2 pos,
        string s, int fontSize,
        int spacing, FontHandle font, Rgba color,
        string shader, float scale = 1f)
    {
        int sFontSize = (int)(fontSize * scale);
        int sSpacing = (int)(spacing * scale);
        var measure = MeasureTextEx(font, s, sFontSize, sSpacing);
        TargetHandle temp = LoadRenderTexture((int)measure.X+8, (int)measure.Y+8);
        TargetHandle temp2 = LoadRenderTexture((int)measure.X+8, (int)measure.Y+8);
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
    
    public static void DrawTextOnRenderTexture(ref TargetHandle texture, string s, int fontSize, int hPadding, int vPadding, int spacing, FontHandle font, Rgba color, string shader, float scale = 1f)
    {
        if(IsRenderTextureValid(texture))
            UnloadRenderTexture(texture);
        var measure = MeasureTextEx(font, s, fontSize, spacing);
        int width = (int)(measure.X + hPadding * 2);
        int height = (int)(measure.Y + vPadding * 2);
        TargetHandle temp = LoadRenderTexture(width, height);
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
        UnloadRenderTexture(temp);
    }
    
    private static TargetHandle ScoreDigits;
    public static Vector2 ScoreDigitSize;
    
    public static TargetHandle DrawText(string s, int fontSize, int hPadding, int vPadding, int spacing, FontHandle font, Rgba color, string shader, float scale = 1f)
    {
        TargetHandle texture = new TargetHandle();
        DrawTextOnRenderTexture(ref texture, s, fontSize, hPadding, vPadding, spacing, font, color, shader, scale);
        return texture;
    }

    public static Rect GetFullSource(TextureHandle t) => new Rect(0, 0, t.Width, t.Height);
    public static Rect GetFullSourceRenderTexture(TargetHandle rt2d) => new Rect(0, rt2d.Texture.Height, rt2d.Texture.Width, -rt2d.Texture.Height);

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
    
    public static TextureHandle RenderSelectionBackground(int width, int height, int vPadding)
    {
        int h = height + vPadding * 2;
        TargetHandle texture = LoadRenderTexture(width, h);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["selection"], LocationRenderSelectionHeight, (float)height, UniformType.Float);
        SetShaderValue(Runtime.CurrentRuntime.Shaders["selection"], LocationRenderSelectionScreenSize, new float[] { 200f, 200f }, UniformType.Vec2);
        BeginTextureMode(texture);
        BeginShaderMode(Runtime.CurrentRuntime.Shaders["selection"]);
        DrawRectanglePro(new Rect(0,0,width,height), Vector2.Zero, 0, Rgba.White);
        EndShaderMode();
        EndTextureMode();
        return texture.Texture;
    }

    public static TargetHandle FillTextureWithColor(Rgba color, int w, int h)
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

    public static bool IsInArea(Vector2 xPositionTo, Vector2 areaStart, Vector2 areaEnd)
    {
        return 
            areaStart.X < xPositionTo.X && areaStart.Y < xPositionTo.Y &&
            areaEnd.X > xPositionTo.X && areaEnd.Y > xPositionTo.Y;
    }

    public static bool IsCollied(Rect rc1, Rect rc2)
    {
        #if DEBUG
        if (rc1.X > rc2.X)
            (rc2.X, rc1.X) = (rc1.X, rc2.X);
        var vecDistance = MathF.Abs(MathUtil.Vector2Distance(rc1.Center, rc2.Center));
        var wDistance = (rc1.Width + rc2.Width) / 2;
        return vecDistance < wDistance;
#else
        return MathUtil.Vector2Distance(rc1.Center, rc2.Center) < (rc1.Width + rc2.Width) / 2;
#endif
    }
    
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
    
    static Dictionary<string, string> TransliterationDictionary = 
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText("Assets/Data/cyrilic-transliteration-table.json"));
    static Dictionary<string, string> TranslationDictionary = 
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText("Assets/Data/translation.json"));

    public static string Translate(string j57v)
    {
        if (TranslationDictionary.ContainsKey(j57v))
        {
            var translitions = TranslationDictionary[j57v].Split(";");
            return Transliterate(translitions[GetRandomValue(0, translitions.Length - 1)]);
        }
        return Transliterate(j57v);
    }
    
    public static string Transliterate(string text)
    {
        string final = "";
        string[] chars;
        foreach (var c in text)
        {
            if (TransliterationDictionary.ContainsKey(c.ToString()))
            {
                chars = TransliterationDictionary[c.ToString()].Split(";;");
                final += chars[new Random().Next(chars.Length - 1)];
            }
            else
                final += c;
        }
        return final;
    }

    public static void UpdatePlayingMusic()
    {
        throw new NotImplementedException();
    }

    public static Vector2 GetSize(TextureHandle texture)
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

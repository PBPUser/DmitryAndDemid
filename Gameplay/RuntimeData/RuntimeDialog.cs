using System.Numerics;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Gameplay.RuntimeData;

/// <summary>
/// Plays a chapter's dialog: the pre-fight exchange between the player and the boss.
///
/// Everything it needs already existed and was simply never driven — <see cref="FileDialogInfo"/> lines in the
/// chapter (authored in the stage editor), the speech-cloud renderer in <see cref="Helper.DrawDialog"/>, and
/// the *_dialog_arts.png sheets, one 768x1024 frame per reaction.
///
/// A line stays up for <see cref="LineDuration"/> seconds on its own; pressing shoot moves to the next one
/// early. Shoot is taken on the press, not while it is held, so keeping the button down through a fight's
/// opening does not blow through the whole conversation. Holding shoot (or Control, its keyboard-only stand-in)
/// for longer than <see cref="SkipHoldThreshold"/> instead skips the whole conversation at once.
///
/// A line flagged <see cref="FileDialogInfo.Unskippable"/> opts out of both: it can only be waited out, so a
/// story beat that has to be read cannot be pressed or held away.
/// </summary>
public class RuntimeDialog
{
    /// <summary>How long a line stays up on its own, in seconds.</summary>
    public const double LineDuration = 5.0;

    /// <summary>A press is ignored for this long after a line appears, so one press cannot skip two lines.</summary>
    private const double AdvanceCooldown = 0.15;

    /// <summary>Holding the skip input longer than this closes the whole dialog instead of advancing one line.</summary>
    private const double SkipHoldThreshold = 0.75;

    /// <summary>The dialog window bounces in over this long when the conversation opens...</summary>
    private const double AppearDuration = 0.38;
    /// <summary>...and bounces back out over this long when the last line is dismissed, before it truly ends.</summary>
    private const double CloseDuration = 0.30;

    /// <summary>&gt;= 0 once the last line has been dismissed: counts up the bounce-out before <see cref="Finished"/>.</summary>
    private double CloseElapsed = -1;

    /// <summary>The dialog art sheets are authored as a horizontal strip of 768x1024 reaction frames. Only the
    /// ASPECT is used at draw time: the sheets arrive scaled to the window, so the frame's pixel size comes from
    /// the loaded texture (see <see cref="DrawPortrait"/>).</summary>
    private const int FrameWidth = 768;
    private const int FrameHeight = 1024;

    private readonly Line[] Lines;
    private readonly GameBox Box;

    private int Index;
    private double Elapsed;
    private double LineElapsed;
    private double LastUpdate;
    private bool ShootWasDown;
    private double SkipHeldElapsed;

    /// <summary>How long a new line takes to take hold: the speaker steps forward, the listener steps back, and
    /// the text writes itself out.</summary>
    private const double LineEnterDuration = 0.35;

    /// <summary>
    /// Which side spoke the PREVIOUS line, so a portrait knows which pose it is animating away FROM. Without it
    /// a character with two lines in a row would visibly shrink back to the listener pose and regrow between
    /// them. Seeded as the opposite of the first line's speaker so that opener animates in rather than starting
    /// already emphasised.
    /// </summary>
    private bool PreviousIsPlayer;

    /// <summary>The current line's entrance progress, 0 → 1.</summary>
    private float LineEnter => (float)Math.Clamp(LineElapsed / LineEnterDuration, 0, 1);

    /// <summary>
    /// How strongly the given side is holding the floor right now: 1 while it is the speaker, 0 while it is
    /// listening, easing between the two across <see cref="LineEnter"/> when the turn changes. Drives the
    /// portrait's size, its step toward the middle and how brightly it is lit, so a turn passing from one
    /// character to the other is a movement rather than a cut.
    /// </summary>
    private float SpeakingPose(bool playerSide)
    {
        float from = PreviousIsPlayer == playerSide ? 1f : 0f;
        float to = Current.IsPlayer == playerSide ? 1f : 0f;
        return Helper.Mix(from, to, LineEnter);
    }

    public bool Finished { get; private set; }

    /// <summary>The line on screen right now.</summary>
    private Line Current => Lines[Index];

    /// <summary>Plays <paramref name="chapter"/>'s lines; their emotions were baked when the chapter loaded.</summary>
    public RuntimeDialog(RuntimeChapter chapter, ProtogonistData protogonist, GameBox box)
    {
        Box = box;
        FileDialogInfo[] dialogs = chapter.Dialogs;
        // The boss stands there for the whole conversation, listening while the player talks. A player line
        // names no character art, so each line takes the boss art of the nearest boss line — the last one
        // before it, or the first one after it when the player opens — instead of dropping the boss off
        // screen for the length of every player line.
        string[] bossArt = new string[dialogs.Length];
        string carried = "";
        for (int i = 0; i < dialogs.Length; i++)
        {
            if (!dialogs[i].IsPlayerDialog && !string.IsNullOrEmpty(dialogs[i].CharacterTexture))
                carried = dialogs[i].CharacterTexture;
            bossArt[i] = carried;
        }
        string first = Array.Find(bossArt, a => !string.IsNullOrEmpty(a)) ?? "";
        for (int i = 0; i < dialogs.Length; i++)
            if (string.IsNullOrEmpty(bossArt[i]))
                bossArt[i] = first;
        Lines = new Line[dialogs.Length];
        for (int i = 0; i < dialogs.Length; i++)
            Lines[i] = new Line(dialogs[i], protogonist, bossArt[i],
                i < chapter.DialogEmotions.Length ? chapter.DialogEmotions[i] : null,
                i < chapter.DialogEmotionTilts.Length ? chapter.DialogEmotionTilts[i] : 0f);
        Finished = Lines.Length == 0;
        LastUpdate = Gfx.GetTime();
        if (Lines.Length > 0)
            PreviousIsPlayer = !Lines[0].IsPlayer;
        WindowHeight = MeasureWindowHeight();
    }

    /// <summary>
    /// The panel's height, in real (scaled) pixels, worked out once when the conversation opens: enough for its
    /// TALLEST line at the fixed text size, floored at the height the window always had and capped so it cannot
    /// take over the playfield. Sized for the whole conversation rather than per line, so the panel does not
    /// jump about between one line and the next.
    /// </summary>
    private readonly float WindowHeight;

    private float MeasureWindowHeight()
    {
        float scale = Runtime.CurrentRuntime.ScaleF;
        float needed = 0;
        foreach (Line line in Lines)
            needed = MathF.Max(needed, line.TextTex.Texture.Height / TextHeightFraction);
        return Math.Clamp(needed, WindowMinHeight1x * scale, WindowMaxHeight1x * scale);
    }

    /// <summary>
    /// Advances the dialog. Driven from GameBox's update, which stops simulating while a dialog is up — hence
    /// the wall clock here rather than the tick counter, and the clamp: after a pause the gap since the last
    /// call is arbitrarily large and must not be counted against the line's five seconds.
    /// </summary>
    public void Update()
    {
        if (Finished)
            return;

        double now = Gfx.GetTime();
        double delta = Math.Clamp(now - LastUpdate, 0, 0.1);
        LastUpdate = now;
        Elapsed += delta;

        // Bouncing out: no input, no advancing — just run the close animation, then finish for real.
        if (CloseElapsed >= 0)
        {
            CloseElapsed += delta;
            if (CloseElapsed >= CloseDuration)
                Finished = true;
            return;
        }

        LineElapsed += delta;

        bool shootDown = IsKeyDown(KeyCode.Z) || Controller.IsButtonDown(Configuration.Config.ShootButton)
                                              || TouchControls.IsDragging;
        // An unskippable line ignores the input entirely: it cannot be advanced early and it cannot be held
        // through, so the conversation runs at its own pace. The hold counter is kept at zero while such a line
        // is up, so a button already held when it appears does not close the dialog the moment it ends.
        bool unskippable = Current.Unskippable;
        bool skipHeld = !unskippable && (shootDown || IsKeyDown(KeyCode.LeftControl));
        SkipHeldElapsed = skipHeld ? SkipHeldElapsed + delta : 0;
        if (SkipHeldElapsed > SkipHoldThreshold)
        {
            CloseElapsed = 0;   // holding through: bounce the whole dialog out at once, like reaching the last line
            return;
        }

        bool pressed = !unskippable && shootDown && !ShootWasDown && LineElapsed > AdvanceCooldown;
        ShootWasDown = shootDown;

        if (pressed || LineElapsed >= LineDuration)
            Next();
    }

    private void Next()
    {
        // Past the last line: begin the bounce-out instead of ending instantly. Finished flips when it completes.
        if (Index >= Lines.Length - 1)
        {
            CloseElapsed = 0;
            return;
        }
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["dialogue"]);
        PreviousIsPlayer = Current.IsPlayer;   // recorded BEFORE the index moves — see SpeakingPose
        Index++;
        LineElapsed = 0;
    }

    /// <summary>Overshooting ease (bounces slightly past 1) for the window opening.</summary>
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * MathF.Pow(x - 1f, 3) + c1 * MathF.Pow(x - 1f, 2);
    }

    /// <summary>Anticipating ease (dips slightly below 0 first) for the window closing.</summary>
    private static float EaseInBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return c3 * x * x * x - c1 * x * x;
    }

    /// <summary>0 → 1 (with a bounce) as the window opens, back → 0 as it closes. Drives the whole panel.</summary>
    private float OpenAmount()
    {
        if (CloseElapsed >= 0)
            return 1f - EaseInBack((float)Math.Clamp(CloseElapsed / CloseDuration, 0, 1));
        return EaseOutBack((float)Math.Clamp(Elapsed / AppearDuration, 0, 1));
    }

    /// <summary>The gap between the window's bottom edge and the bottom of the playfield, 384x448 space. The
    /// window is pinned here and grows UPWARDS as the conversation needs the room.</summary>
    private const float WindowBottom1x = 10f;

    /// <summary>
    /// The window never gets shorter than this — the height it always was, when its top edge was nailed to
    /// y=350 and the text was squeezed into whatever was left.
    /// </summary>
    private const float WindowMinHeight1x = 88f;

    /// <summary>
    /// ...and never taller than this, so no one line can swallow the playfield. A conversation needing more room
    /// than this is the one case where the text is still scaled down (the clamp in <see cref="Draw"/>); no
    /// authored conversation comes close.
    /// </summary>
    private const float WindowMaxHeight1x = 200f;

    /// <summary>The window's left/right margin, in the 384x448 space.</summary>
    private const float WindowMargin1x = 8f;

    /// <summary>How much of the window's width the text may use; the rest is the fork dressing on the right.</summary>
    private const float TextWidthFraction = 0.72f;

    /// <summary>Gap between the window's left edge and the text block, in the 384x448 space.</summary>
    private const float TextInset1x = 12f;

    /// <summary>How much of the window's height the text may use.</summary>
    private const float TextHeightFraction = 0.9f;

    /// <summary>
    /// The size a line is drawn at, in the 384x448 space — the same for every line, whatever it says. The text
    /// used to be fitted to the window instead, which meant the size was a function of how much the line had to
    /// say: a two-line beat came out around 22, a short retort at nearly 41, and (on a backend that does not
    /// know '\n' — see <see cref="Helper.DrawTextOnRenderTexture"/>) a long one collapsed onto one 16-tall row.
    /// Now the size is pinned here and a line too wide for the window is given the newlines it lacks instead,
    /// see <see cref="Wrap"/>.
    /// </summary>
    private const int TextFontSize1x = 13;
    private const int TextPadding1x = 6;
    private const int TextSpacing1x = 2;

    /// <summary>
    /// The widest a baked line may be, in the 384x448 space: the text's share of the window, less the inset it
    /// is drawn at and the padding baked into its own texture.
    /// </summary>
    private const float TextWrapWidth1x =
        (384f - WindowMargin1x * 2f) * TextWidthFraction - TextInset1x - TextPadding1x * 2f;

    /// <summary>
    /// The text is rasterised at the window's scale rather than at 1x and blown up at draw time, so it is as
    /// crisp as the resolution allows. Everything the bake needs comes from here, in real (scaled) pixels.
    /// </summary>
    private readonly record struct TextMetrics(int FontSize, int Padding, int Spacing, float WrapWidth)
    {
        public static TextMetrics Current()
        {
            float scale = Runtime.CurrentRuntime.ScaleF;
            return new TextMetrics(Math.Max(1, (int)(TextFontSize1x * scale)),
                (int)(TextPadding1x * scale), Math.Max(1, (int)(TextSpacing1x * scale)),
                TextWrapWidth1x * scale);
        }

        public float Width(string s, FontHandle font) => MeasureTextEx(font, s, FontSize, Spacing).X;
    }

    /// <summary>
    /// Breaks a line so nothing in it is wider than the window: the newlines the author wrote are kept, and the
    /// ones they did not write are added. Words move down whole; a single word too wide to sit on a line of its
    /// own — which no authored line is, but a translation or a transliteration could produce — is cut at the
    /// character that overflows rather than left hanging out over the playfield.
    /// </summary>
    private static string Wrap(string text, FontHandle font, TextMetrics m)
    {
        List<string> output = [];
        foreach (string paragraph in (text ?? "").Replace("\r", "").Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && m.Width(candidate, font) > m.WrapWidth)
                {
                    output.Add(line);          // the word does not fit beside what is already there: next line
                    candidate = word;
                }
                while (candidate.Length > 1 && m.Width(candidate, font) > m.WrapWidth)
                {
                    int fit = candidate.Length - 1;
                    while (fit > 1 && m.Width(candidate[..fit], font) > m.WrapWidth)
                        fit--;
                    output.Add(candidate[..fit]);
                    candidate = candidate[fit..];
                }
                line = candidate;
            }
            output.Add(line);
        }
        return string.Join("\n", output);
    }

    /// <summary>
    /// Draws the current line: the portraits stand at the bottom (speaker lit, the other dimmed), and the
    /// conversation text sits in a dark window pinned near the bottom of the playfield (top edge at y=350 in the
    /// 384x448 space). The window bounces in when the dialog opens and out when it ends, and its right side is
    /// dressed with randomly-placed, randomly-coloured forks.
    /// </summary>
    public void Draw(RenderedTexture target)
    {
        if (Finished || Lines.Length == 0)
            return;

        float scale = Runtime.CurrentRuntime.ScaleF;
        float width = target.Texture.Width;
        float height = target.Texture.Height;
        float time = (float)GetTime();

        // The window opens with a vertical bounce about its own centre; content (forks, text) fades in with it.
        // Read before the portraits now, because they ride it in from the sides too.
        float open = OpenAmount();

        // Portraits stand on the bottom edge, player on the left, boss on the right, each turned inward.
        float artHeight = height * 0.62f;
        float artWidth = artHeight * FrameWidth / FrameHeight;
        float artY = height - artHeight;

        DrawPortrait(Current.PlayerArt, Current.PlayerFrame,
            new Rect(-artWidth * 0.12f, artY, artWidth, artHeight), false,
            SpeakingPose(true), open, time, scale, (float)LineElapsed);
        DrawPortrait(Current.BossArt, Current.BossFrame,
            new Rect(width - artWidth * 0.88f, artY, artWidth, artHeight), true,
            SpeakingPose(false), open, time, scale, (float)LineElapsed);

        if (open <= 0.001f)
            return;
        float contentA = Math.Clamp((open - 0.35f) / 0.5f, 0f, 1f);   // forks/text fade in once the panel is open

        float margin = WindowMargin1x * scale;
        float fullX = margin;
        float fullW = width - margin * 2;
        float fullH = WindowHeight;                                   // as tall as this conversation needs
        float fullY = (448f - WindowBottom1x) * scale - fullH;        // bottom pinned, grown upwards

        float cy = fullY + fullH / 2f;
        float drawH = fullH * open;                                   // vertical squeeze/bounce
        Rect win = new Rect(fullX, cy - drawH / 2f, fullW, drawH);

        // Dark panel. (The thin accent line that used to sit along the window's top edge is intentionally gone.)
        DrawRectangleRec(win, new Rgba(12, 12, 22, (byte)(225 * Math.Min(1f, open))));

        // Randomly-placed, randomly-coloured forks dressing the right side of the window (generated once per line).
        BasicTexture fork = Runtime.CurrentRuntime.Textures["vilkaCut.png"];
        Vector2 fsz = Helper.GetSize(fork);
        float aspect = fsz.X > 0 ? fsz.Y / fsz.X : 1f;
        foreach (ForkDeco d in Current.Forks)
        {
            float fw = d.Scale * 26f * scale;
            float fh = fw * aspect;
            // The dressing drifts and turns instead of sitting there: each fork spins at its own lazy rate and
            // bobs on its own phase. Applied BEFORE the clamp below, so a drifting fork is still cut to the panel.
            float bob = MathF.Sin(time * (0.5f + d.Scale) + d.Rx * 7f) * 3f * scale;
            float spin = d.Rotation + time * d.Spin;
            float fx = win.X + d.Rx * win.Width;
            float fy = win.Y + d.Ry * win.Height + bob;
            // Cut: keep the fork inside the panel. Rotated quads have a diagonal reach, so clamp the centre by
            // the half-diagonal — a fork that would overhang the window edge is pulled in and "cut" to fit,
            // instead of spilling past the dark panel onto the playfield. (No cross-backend scissor is available.)
            float half = 0.5f * MathF.Sqrt(fw * fw + fh * fh);
            float minX = win.X + half, maxX = win.X + win.Width - half;
            float minY = win.Y + half, maxY = win.Y + win.Height - half;
            fx = maxX > minX ? Math.Clamp(fx, minX, maxX) : win.X + win.Width / 2f;
            fy = maxY > minY ? Math.Clamp(fy, minY, maxY) : win.Y + win.Height / 2f;
            // Dim: mix the bright random colour toward the panel dark and drop the alpha, so the forks recede
            // into the background behind the text rather than competing with it.
            Rgba dim = Helper.Mix(d.Color, new Rgba(12, 12, 22), 0.5f) with { A = (byte)(contentA * 70) };
            DrawTexturePro(fork, new Rect(0, 0, fsz.X, fsz.Y),
                new Rect(fx, fy, fw, fh), new Vector2(fw / 2f, fh / 2f), spin, dim);
        }

        // The line's translated text, rendered once into a texture, blitted into the left portion of the window.
        BasicTexture text = Current.TextTex.Texture;
        if (text.Width > 0 && contentA > 0f)
        {
            float availW = win.Width * TextWidthFraction - TextInset1x * scale;
            // Measured against the window at FULL height, not the bounce-squeezed one: the size must not depend
            // on how far through its opening animation the panel is.
            float availH = fullH * TextHeightFraction;
            // Fixed size — the block is drawn 1:1 at the size it was baked at, however much the line says; a
            // line too wide for the window was wrapped when it was baked. The clamp never scales the text UP,
            // and only pulls it down for something wrapping could not save: a line broken into more rows than
            // the window is tall.
            float ts = MathF.Min(1f, MathF.Min(availW / text.Width, availH / text.Height));
            float tw = text.Width * ts, th = text.Height * ts;
            // The line writes itself out top-down rather than appearing whole: the source is cropped to the top
            // `reveal` of the pre-rendered block and the destination cropped to match, which unrolls the text a
            // row at a time. It works on the finished texture, so nothing has to be re-rendered per frame — and
            // it stays put, because the block is positioned from its top edge, not re-centred as it grows.
            float reveal = LineEnter;
            // No Y-flip: this composites into the UIAboveGameplay render target, where render-texture content
            // (the boss/chapter titles right above) blits upright with a positive source. The old -Height flip
            // is what turned the dialog text upside-down.
            DrawTexturePro(text,
                new Rect(0, 0, text.Width, text.Height * reveal),
                new Rect(win.X + 12 * scale, win.Y + (win.Height - th) / 2f, tw, th * reveal),
                Vector2.Zero, 0, Rgba.White with { A = (byte)(contentA * 255) });
        }

        DrawEmotion(win, scale, time, contentA);
        DrawContinueHint(win, fork, fsz, aspect, scale, time, contentA);
        DrawBossNameCard(width, scale, contentA, time);
    }

    /// <summary>How tall a line's emotion is drawn, in the 384x448 space (the bake is this size times the UI
    /// scale, so it is never upscaled).</summary>
    private const float EmotionHeight1x = 44f;

    /// <summary>
    /// The line's emotion: its baked glyph perched on the window's top edge on the speaker's side — left for
    /// the player, right for the boss — popping in with the line, tilted the way it was rolled and swaying a
    /// few degrees either way of that, bobbing along with the rest of the dressing.
    /// </summary>
    private void DrawEmotion(Rect win, float scale, float time, float contentA)
    {
        if (Current.Emotion is not { } glyph || contentA <= 0f || glyph.Height <= 0)
            return;
        float pop = EaseOutBack(LineEnter);
        float eh = EmotionHeight1x * scale * pop;
        float ew = eh * glyph.Width / glyph.Height;
        float ex = Current.IsPlayer ? win.X + 30f * scale : win.X + win.Width - 30f * scale;
        float ey = win.Y + 2f * scale + MathF.Sin(time * 1.9f) * 2.5f * scale;
        float tilt = Current.EmotionTilt + MathF.Sin(time * 1.3f + 1f) * 3f;
        DrawTexturePro(glyph, new Rect(0, 0, glyph.Width, glyph.Height),
            new Rect(ex, ey, ew, eh), new Vector2(ew / 2f, eh / 2f), tilt,
            Rgba.White with { A = (byte)(contentA * 255) });
    }

    /// <summary>
    /// A small fork bobbing in the window's bottom-right corner once the line can actually be dismissed — the
    /// game's "press to continue". It is gated on the same conditions the input is, so an unskippable line (or a
    /// line still inside its advance cooldown) never shows a prompt that would do nothing if obeyed.
    /// </summary>
    private void DrawContinueHint(Rect win, BasicTexture fork, Vector2 fsz, float aspect, float scale,
        float time, float contentA)
    {
        if (Current.Unskippable || CloseElapsed >= 0 || LineElapsed <= AdvanceCooldown || contentA <= 0f)
            return;

        float w = 13f * scale, h = w * aspect;
        float bob = MathF.Sin(time * 5.2f) * 2.5f * scale;
        float blink = 0.5f + 0.5f * MathF.Sin(time * 5.2f);
        DrawTexturePro(fork, new Rect(0, 0, fsz.X, fsz.Y),
            new Rect(win.X + win.Width - w, win.Y + win.Height - h * 0.75f + bob, w, h),
            new Vector2(w / 2f, h / 2f), 180f + MathF.Sin(time * 2.6f) * 8f,
            Rgba.White with { A = (byte)(contentA * blink * 210) });
    }

    /// <summary>
    /// The ShowBossName tag's visual: a card near the top of the playfield with the boss's name, a rotating fork
    /// tinted in the boss's accent colour, and (if it exists) the profile-&lt;boss&gt;.png art. Everything is drawn
    /// only when a profile json exists for the speaker; missing art just omits that piece.
    /// </summary>
    private void DrawBossNameCard(float width, float scale, float contentA, float t)
    {
        Line line = Current;
        if (!line.ShowBossName || line.Profile == null || contentA <= 0f)
            return;

        byte alpha = (byte)(contentA * 255);
        Rgba accent = line.Profile.AccentColor() with { A = alpha };

        float art = 56f * scale;                    // profile square / fork size
        float pad = 8f * scale;
        float x = width - art - pad * 2f;           // top-right corner (the boss stands on the right)
        float y = pad;
        // The card rides in from off the right edge with the rest of the content, and once it has landed it
        // keeps drifting a couple of pixels so the corner is never completely dead.
        x += (1f - contentA) * (art + pad * 3f) + MathF.Sin(t * 1.3f) * 2f * scale;
        y += MathF.Cos(t * 1.1f) * 2f * scale;

        // Rotating tinted fork behind the profile.
        BasicTexture fork = Runtime.CurrentRuntime.Textures["forkCut.png"];
        Vector2 fsz = Helper.GetSize(fork);
        float fh = art * 1.35f, fw = fsz.X > 0 ? fh * fsz.X / fsz.Y : fh;
        DrawTexturePro(fork, new Rect(0, 0, fsz.X, fsz.Y),
            new Rect(x + art / 2f, y + art / 2f, fw, fh),
            new Vector2(fw / 2f, fh / 2f), t * 60f, accent);

        // Profile art, only if it exists.
        if (line.ProfileArt != null)
        {
            BasicTexture pa = line.ProfileArt.Value;
            DrawTexturePro(pa, new Rect(0, 0, pa.Width, pa.Height),
                new Rect(x, y, art, art), Vector2.Zero, 0, Rgba.White with { A = alpha });
        }

        // Boss name, right-aligned to the left of the profile square.
        if (!string.IsNullOrEmpty(line.Profile.Name))
        {
            FontHandle font = Runtime.CurrentRuntime.Fonts["newsreader"];
            float fontSize = 18f * scale;
            Vector2 m = MeasureTextEx(font, line.Profile.Name, fontSize, 1);
            Vector2 pos = new Vector2(x - pad - m.X, y + (art - m.Y) / 2f);
            DrawTextEx(font, line.Profile.Name, pos + new Vector2(1.5f * scale), fontSize, 1,
                Rgba.Black with { A = alpha });        // shadow
            DrawTextEx(font, line.Profile.Name, pos, fontSize, 1, accent);
        }
    }

    /// <summary>
    /// One character. They used to be two still images that swapped tint when the turn changed; now the pair
    /// acts the conversation out:
    ///
    /// • they walk on from their own side of the screen as the window opens, and back off as it closes;
    /// • whoever has the floor steps toward the middle, grows slightly and lights up, while the other backs off
    ///   and dims — eased across the turn (<paramref name="pose"/>), so it reads as one handing over to the other;
    /// • both breathe on a slow sine, out of phase with each other so they never look like one sprite drawn twice;
    /// • the speaker punches in with a couple of quick nods as their line lands, which is what sells it as talking.
    ///
    /// All the growth is anchored to the BOTTOM edge, so however much a character swells their feet stay planted
    /// on the floor of the playfield rather than sliding down it.
    /// </summary>
    private static void DrawPortrait(BasicTexture? art, int frame, Rect destination, bool flip,
        float pose, float open, float time, float scale, float lineElapsed)
    {
        if (art == null || open <= 0.001f)
            return;

        // The sheets are loaded scaled to the window (their .json has MatchGameResolutionScaling), so the
        // frame is measured off the texture that actually arrived: a frame is as tall as the sheet and 3:4
        // wide, and the frame count is what fits — never the authored 768x1024, which at most resolutions
        // would slice a frame and a half out of a sheet a third shorter than assumed.
        float frameH = art.Value.Height;
        float frameW = frameH * FrameWidth / FrameHeight;
        int frames = Math.Max(1, (int)MathF.Round(art.Value.Width / frameW));
        frameW = art.Value.Width / (float)frames;
        Rect source = new(Math.Clamp(frame, 0, frames - 1) * frameW, 0,
            flip ? -frameW : frameW, frameH);

        // Outward is +x for the character on the right, -x for the one on the left.
        float outward = flip ? 1f : -1f;
        // Walk-on. `open` overshoots slightly past 1 (EaseOutBack), which carries them a few pixels past their
        // mark and back — the same bounce the window itself lands with.
        float entrance = (1f - open) * destination.Width * 0.55f * outward;
        // Holding the floor: a step toward the centre, and a lift out of the breath.
        float step = pose * 7f * scale * -outward;
        float breath = MathF.Sin(time * 1.4f + (flip ? 1.7f : 0f)) * 2.5f * scale;
        float nod = MathF.Exp(-lineElapsed * 6f) * MathF.Sin(lineElapsed * 22f) * 7f * scale * pose;

        float grow = 1f + pose * 0.05f;
        float grownW = destination.Width * grow, grownH = destination.Height * grow;
        Rect dest = new(
            destination.X + entrance + step - (grownW - destination.Width) / 2f,
            destination.Y - (grownH - destination.Height) + breath + nod,
            grownW, grownH);

        // The listener stays on screen but recedes: dimmed, so it is obvious who is talking. Mixed by the pose
        // rather than switched, so the lighting crossfades with the movement instead of snapping a frame early.
        Rgba tint = Helper.Mix(new Rgba(128, 128, 148, 220), Rgba.White, pose);
        DrawTexturePro(art.Value, source, dest, Vector2.Zero, 0, tint);
    }

    public void Unload()
    {
        foreach (Line line in Lines)
            line.Unload();
    }

    /// <summary>One fork dressing the right side of the window: a fixed (per-line) random spot, spin and colour.</summary>
    private struct ForkDeco
    {
        public float Rx, Ry;      // 0..1 position within the window
        public float Rotation;    // degrees
        public float Spin;        // degrees per second, either direction
        public float Scale;
        public Rgba Color;
    }

    /// <summary>One authored line, with its translated text rendered once up front and its fork dressing rolled.</summary>
    private class Line
    {
        public readonly bool IsPlayer;
        /// <summary>The line refuses press-to-advance and hold-to-skip (see the file format's 0x10 bit).</summary>
        public readonly bool Unskippable;
        public readonly RenderedTexture TextTex;
        public readonly ForkDeco[] Forks;
        public readonly BasicTexture? PlayerArt;
        public readonly BasicTexture? BossArt;
        public readonly int PlayerFrame;
        public readonly int BossFrame;

        /// <summary>The ShowBossName tag: when set, the boss's name card (name + optional profile art + a
        /// rotating tinted fork) is shown for this line. All parts are opt-in by file existence.</summary>
        public readonly bool ShowBossName;
        public readonly BossProfile? Profile;
        public readonly BasicTexture? ProfileArt;

        /// <summary>The line's baked emotion glyph (owned by the chapter, not this line) and its tilt in
        /// degrees; null when the line has none.</summary>
        public readonly BasicTexture? Emotion;
        public readonly float EmotionTilt;

        /// <param name="bossArt">The boss's art sheet for this line — resolved by the dialog across the whole
        /// conversation, so a player line (which names none) still keeps the boss on screen.</param>
        public Line(FileDialogInfo info, ProtogonistData protogonist, string bossArt, BasicTexture? emotion,
            float emotionTilt)
        {
            IsPlayer = info.IsPlayerDialog;
            Unskippable = info.Unskippable;
            Emotion = emotion;
            EmotionTilt = emotionTilt;

            // Boss-name tag: resolve the boss key from the character art (e.g. "nikitab_dialog_art.png" ->
            // "nikitab"), then look up its profile json (for the name + accent colour) and the optional
            // profile-<key>.png art. Both are shown only if they exist — nothing is required to ship.
            ShowBossName = info.ShowBossName;
            if (ShowBossName)
            {
                string key = BossProfile.KeyFromCharacterTexture(info.CharacterTexture);
                Profile = BossProfile.Get(key);
                ProfileArt = Runtime.CurrentRuntime.Textures.TryGetValue($"profile-{key}.png", out BasicTexture pt)
                    ? pt
                    : null;
            }

            // Route the line through the game's translator (Helper.Translate): a translation.json key resolves to
            // one of its variants, anything else falls through transliterated — either way it comes back ready to
            // draw. Wrapped to the window's width (the text is drawn at a fixed size, so a long line has to be
            // broken rather than shrunk), then rendered once into a texture (white with a shadow, to read on the
            // dark window).
            FontHandle textFont = Runtime.CurrentRuntime.Fonts["newsreader"];
            TextMetrics m = TextMetrics.Current();
            TextTex = Helper.DrawText(Wrap(Helper.Translate(info.Text), textFont, m),
                m.FontSize, m.Padding, m.Padding, m.Spacing, textFont, Rgba.White, "shadow");

            // Roll the right-side fork dressing once, so it stays put instead of flickering every frame. The
            // forks live in the right ~40% of the window, at random heights, spins, sizes and bright colours.
            int count = 5 + GetRandomValue(0, 4);
            Forks = new ForkDeco[count];
            for (int i = 0; i < count; i++)
                Forks[i] = new ForkDeco
                {
                    Rx = 0.58f + GetRandomValue(0, 1000) / 1000f * 0.38f,
                    Ry = 0.12f + GetRandomValue(0, 1000) / 1000f * 0.76f,
                    Rotation = GetRandomValue(0, 359),
                    Spin = GetRandomValue(-120, 120) / 10f,   // ±12°/s: a lazy turn, not a spin
                    Scale = 0.55f + GetRandomValue(0, 1000) / 1000f * 0.75f,
                    Color = new Rgba((byte)GetRandomValue(70, 255), (byte)GetRandomValue(70, 255),
                        (byte)GetRandomValue(70, 255), 255),
                };

            PlayerArt = Lookup(protogonist.DialogArtName);
            BossArt = Lookup(bossArt);

            // Header[2] is the reaction frame, and it only applies to whoever is speaking.
            int reaction = info.SwitchReaction ? info.Header[2] : 0;
            PlayerFrame = IsPlayer ? reaction : 0;
            BossFrame = IsPlayer ? 0 : reaction;

            // Header[3] would switch the track here, but the game has no music playback yet
            // (Helper.UpdatePlayingMusic throws NotImplementedException), so SwitchMusic is carried in the
            // data and ignored at runtime.
        }

        static BasicTexture? Lookup(string name) =>
            !string.IsNullOrEmpty(name) && Runtime.CurrentRuntime.Textures.TryGetValue(name, out BasicTexture t)
                ? t
                : null;

        public void Unload() => UnloadRenderTexture(TextTex);
    }
}

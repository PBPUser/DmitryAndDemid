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
/// opening does not blow through the whole conversation.
/// </summary>
public class RuntimeDialog
{
    /// <summary>How long a line stays up on its own, in seconds.</summary>
    public const double LineDuration = 5.0;

    /// <summary>A press is ignored for this long after a line appears, so one press cannot skip two lines.</summary>
    private const double AdvanceCooldown = 0.15;

    /// <summary>The dialog window bounces in over this long when the conversation opens...</summary>
    private const double AppearDuration = 0.38;
    /// <summary>...and bounces back out over this long when the last line is dismissed, before it truly ends.</summary>
    private const double CloseDuration = 0.30;

    /// <summary>&gt;= 0 once the last line has been dismissed: counts up the bounce-out before <see cref="Finished"/>.</summary>
    private double CloseElapsed = -1;

    /// <summary>The dialog art sheets are a horizontal strip of 768x1024 reaction frames.</summary>
    private const int FrameWidth = 768;
    private const int FrameHeight = 1024;

    private readonly Line[] Lines;
    private readonly GameBox Box;

    private int Index;
    private double Elapsed;
    private double LineElapsed;
    private double LastUpdate;
    private bool ShootWasDown;

    public bool Finished { get; private set; }

    /// <summary>The line on screen right now.</summary>
    private Line Current => Lines[Index];

    public RuntimeDialog(FileDialogInfo[] dialogs, ProtogonistData protogonist, GameBox box)
    {
        Box = box;
        Lines = dialogs.Select(d => new Line(d, protogonist)).ToArray();
        Finished = Lines.Length == 0;
        LastUpdate = Gfx.GetTime();
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
        bool pressed = shootDown && !ShootWasDown && LineElapsed > AdvanceCooldown;
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

    /// <summary>The dialog window's top edge, in the 384x448 gameplay space (scaled to the target below).</summary>
    private const float WindowTop1x = 350f;

    /// <summary>
    /// Draws the current line: the portraits stand at the bottom (speaker lit, the other dimmed), and the
    /// conversation text sits in a dark window pinned near the bottom of the playfield (top edge at y=350 in the
    /// 384x448 space). The window bounces in when the dialog opens and out when it ends, and its right side is
    /// dressed with randomly-placed, randomly-coloured forks.
    /// </summary>
    public void Draw(TargetHandle target)
    {
        if (Finished || Lines.Length == 0)
            return;

        float scale = Runtime.CurrentRuntime.ScaleF;
        float width = target.Texture.Width;
        float height = target.Texture.Height;

        // Portraits stand on the bottom edge, player on the left, boss on the right, each turned inward.
        float artHeight = height * 0.62f;
        float artWidth = artHeight * FrameWidth / FrameHeight;
        float artY = height - artHeight;

        DrawPortrait(Current.PlayerArt, Current.PlayerFrame,
            new Rect(-artWidth * 0.12f, artY, artWidth, artHeight), Current.IsPlayer, false);
        DrawPortrait(Current.BossArt, Current.BossFrame,
            new Rect(width - artWidth * 0.88f, artY, artWidth, artHeight), !Current.IsPlayer, true);

        // The window opens with a vertical bounce about its own centre; content (forks, text) fades in with it.
        float open = OpenAmount();
        if (open <= 0.001f)
            return;
        float contentA = Math.Clamp((open - 0.35f) / 0.5f, 0f, 1f);   // forks/text fade in once the panel is open

        float margin = 8 * scale;
        float fullX = margin;
        float fullW = width - margin * 2;
        float fullY = WindowTop1x * scale;
        float fullH = (448f - WindowTop1x - 10f) * scale;             // fill from y=350 to near the bottom edge

        float cy = fullY + fullH / 2f;
        float drawH = fullH * open;                                   // vertical squeeze/bounce
        Rect win = new Rect(fullX, cy - drawH / 2f, fullW, drawH);

        // Dark panel. (The thin accent line that used to sit along the window's top edge is intentionally gone.)
        DrawRectangleRec(win, new Rgba(12, 12, 22, (byte)(225 * Math.Min(1f, open))));

        // Randomly-placed, randomly-coloured forks dressing the right side of the window (generated once per line).
        TextureHandle fork = Runtime.CurrentRuntime.Textures["vilkaCut.png"];
        Vector2 fsz = Helper.GetSize(fork);
        float aspect = fsz.X > 0 ? fsz.Y / fsz.X : 1f;
        foreach (ForkDeco d in Current.Forks)
        {
            float fw = d.Scale * 26f * scale;
            float fh = fw * aspect;
            float fx = win.X + d.Rx * win.Width;
            float fy = win.Y + d.Ry * win.Height;
            DrawTexturePro(fork, new Rect(0, 0, fsz.X, fsz.Y),
                new Rect(fx, fy, fw, fh), new Vector2(fw / 2f, fh / 2f), d.Rotation,
                d.Color with { A = (byte)(contentA * 130) });
        }

        // The line's translated text, rendered once into a texture, blitted into the left portion of the window.
        TextureHandle text = Current.TextTex.Texture;
        if (text.Width > 0 && contentA > 0f)
        {
            float availW = win.Width * 0.66f - 12 * scale;
            float availH = win.Height * 0.82f;
            float ts = MathF.Min(availW / text.Width, availH / text.Height);
            float tw = text.Width * ts, th = text.Height * ts;
            // No Y-flip: this composites into the UIAboveGameplay render target, where render-texture content
            // (the boss/chapter titles right above) blits upright with a positive source. The old -Height flip
            // is what turned the dialog text upside-down.
            DrawTexturePro(text,
                new Rect(0, 0, text.Width, text.Height),
                new Rect(win.X + 12 * scale, win.Y + (win.Height - th) / 2f, tw, th),
                Vector2.Zero, 0, Rgba.White with { A = (byte)(contentA * 255) });
        }

        DrawBossNameCard(width, scale, contentA);
    }

    /// <summary>
    /// The ShowBossName tag's visual: a card near the top of the playfield with the boss's name, a rotating fork
    /// tinted in the boss's accent colour, and (if it exists) the profile-&lt;boss&gt;.png art. Everything is drawn
    /// only when a profile json exists for the speaker; missing art just omits that piece.
    /// </summary>
    private void DrawBossNameCard(float width, float scale, float contentA)
    {
        Line line = Current;
        if (!line.ShowBossName || line.Profile == null || contentA <= 0f)
            return;

        float t = (float)GetTime();
        byte alpha = (byte)(contentA * 255);
        Rgba accent = line.Profile.AccentColor() with { A = alpha };

        float art = 56f * scale;                    // profile square / fork size
        float pad = 8f * scale;
        float x = width - art - pad * 2f;           // top-right corner (the boss stands on the right)
        float y = pad;

        // Rotating tinted fork behind the profile.
        TextureHandle fork = Runtime.CurrentRuntime.Textures["forkCut.png"];
        Vector2 fsz = Helper.GetSize(fork);
        float fh = art * 1.35f, fw = fsz.X > 0 ? fh * fsz.X / fsz.Y : fh;
        DrawTexturePro(fork, new Rect(0, 0, fsz.X, fsz.Y),
            new Rect(x + art / 2f, y + art / 2f, fw, fh),
            new Vector2(fw / 2f, fh / 2f), t * 60f, accent);

        // Profile art, only if it exists.
        if (line.ProfileArt != null)
        {
            TextureHandle pa = line.ProfileArt.Value;
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

    private static void DrawPortrait(TextureHandle? art, int frame, Rect destination, bool speaking, bool flip)
    {
        if (art == null)
            return;

        int frames = Math.Max(1, art.Value.Width / FrameWidth);
        Rect source = new(Math.Clamp(frame, 0, frames - 1) * FrameWidth, 0,
            flip ? -FrameWidth : FrameWidth, FrameHeight);

        // The listener stays on screen but recedes: dimmed, so it is obvious who is talking.
        Rgba tint = speaking ? Rgba.White : new Rgba(128, 128, 148, 220);
        DrawTexturePro(art.Value, source, destination, Vector2.Zero, 0, tint);
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
        public float Scale;
        public Rgba Color;
    }

    /// <summary>One authored line, with its translated text rendered once up front and its fork dressing rolled.</summary>
    private class Line
    {
        public readonly bool IsPlayer;
        public readonly TargetHandle TextTex;
        public readonly ForkDeco[] Forks;
        public readonly TextureHandle? PlayerArt;
        public readonly TextureHandle? BossArt;
        public readonly int PlayerFrame;
        public readonly int BossFrame;

        /// <summary>The ShowBossName tag: when set, the boss's name card (name + optional profile art + a
        /// rotating tinted fork) is shown for this line. All parts are opt-in by file existence.</summary>
        public readonly bool ShowBossName;
        public readonly BossProfile? Profile;
        public readonly TextureHandle? ProfileArt;

        public Line(FileDialogInfo info, ProtogonistData protogonist)
        {
            IsPlayer = info.IsPlayerDialog;

            // Boss-name tag: resolve the boss key from the character art (e.g. "nikitab_dialog_art.png" ->
            // "nikitab"), then look up its profile json (for the name + accent colour) and the optional
            // profile-<key>.png art. Both are shown only if they exist — nothing is required to ship.
            ShowBossName = info.ShowBossName;
            if (ShowBossName)
            {
                string key = BossProfile.KeyFromCharacterTexture(info.CharacterTexture);
                Profile = BossProfile.Get(key);
                ProfileArt = Runtime.CurrentRuntime.Textures.TryGetValue($"profile-{key}.png", out TextureHandle pt)
                    ? pt
                    : null;
            }

            // Route the line through the game's translator (Helper.Translate): a translation.json key resolves to
            // one of its variants, anything else falls through transliterated — either way it comes back ready to
            // draw. Rendered once into a texture (white with a shadow, to read on the dark window).
            TextTex = Helper.DrawText(Helper.Translate(info.Text), 16, 6, 6, 2,
                Runtime.CurrentRuntime.Fonts["newsreader"], Rgba.White, "shadow");

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
                    Scale = 0.55f + GetRandomValue(0, 1000) / 1000f * 0.75f,
                    Color = new Rgba((byte)GetRandomValue(70, 255), (byte)GetRandomValue(70, 255),
                        (byte)GetRandomValue(70, 255), 255),
                };

            PlayerArt = Lookup(protogonist.DialogArtName);
            BossArt = Lookup(info.CharacterTexture);

            // Header[2] is the reaction frame, and it only applies to whoever is speaking.
            int reaction = info.SwitchReaction ? info.Header[2] : 0;
            PlayerFrame = IsPlayer ? reaction : 0;
            BossFrame = IsPlayer ? 0 : reaction;

            // Header[3] would switch the track here, but the game has no music playback yet
            // (Helper.UpdatePlayingMusic throws NotImplementedException), so SwitchMusic is carried in the
            // data and ignored at runtime.
        }

        static TextureHandle? Lookup(string name) =>
            !string.IsNullOrEmpty(name) && Runtime.CurrentRuntime.Textures.TryGetValue(name, out TextureHandle t)
                ? t
                : null;

        public void Unload() => UnloadRenderTexture(TextTex);
    }
}

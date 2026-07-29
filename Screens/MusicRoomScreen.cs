using DmitryAndDemid.Rendering;
using static DmitryAndDemid.Rendering.Gfx;
using System.Numerics;
using System.Text.Json;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Screens;

public class MusicRoomScreen : MenuScreen
{
    private string[] SpoilerWarning = Helper.Translate("musicroom.warning").Split("\n");
    private MusicInfo[] Infos;
    private int CurrentDescriptionIndex = 0;
    private string[][] DescriptionLines = [];
    public int FontSize;
    // A locked track shows a spoiler warning on the first confirm and only plays on the second. SpoilerPending
    // is the track currently warned about; Revealed are tracks the player accepted the spoiler for this session.
    private int SpoilerPendingIndex = -1;
    private readonly HashSet<int> Revealed = new();
    
    
    public MusicRoomScreen()
    {
        FontSize = (int)(13 * Runtime.CurrentRuntime.ScaleF);   // the light serif reads smaller than the old bold
    }

    public override void Exiting()
    {
        base.Exiting();
    }

    public override void Activated()
    {
        SwitchDescription(0);
        SelectedIndex = 0;
        RefreshLockedTints();
        base.Activated();
    }

    /// <summary>Greys out the rows for tracks still locked in-game, so the "???" entries read as unavailable
    /// rather than as ordinary items. Re-applied on every Activated because tracks unlock between visits.
    /// A locked row stays selectable — confirming it is what raises the spoiler prompt.</summary>
    private void RefreshLockedTints()
    {
        for (int i = 0; i < MenuItems.Count && i < Infos.Length; i++)
            MenuItems[i].Tint = PlayerData.Instance.IsMusicUnlocked(Infos[i].Number)
                ? Rgba.White
                : new Rgba(128, 132, 142, 190);
    }
    
    public override void CreateMenu()
    {
        EnableScrolling = true;   // the track list can be longer than the screen — scroll to follow the cursor
        MaxVisibleItems = 7;      // show a fixed 7-row window with the edge-fade gradient, not the whole list
        SetTitle(Runtime.CurrentRuntime.Textures["music_room.png"]);
        SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
        string[] files = Assets.Files("Assets/Music/Descriptions");
#if SWITCH
        Runtime.SwTrace($"[musicroom] CreateMenu: deserializing {files.Length} MusicInfo files");
#endif
        Infos = new MusicInfo[files.Length];
        for (int i = 0; i < files.Length; i++)
            Infos[i] = JsonSerializer.Deserialize<MusicInfo>(File.ReadAllText(files[i])) ?? new MusicInfo();
#if SWITCH
        Runtime.SwTrace("[musicroom] CreateMenu: MusicInfo deserialize done, building items");
#endif
        Infos = Infos.OrderBy(x => x.Number).ToArray();
        DescriptionLines = new string[Infos.Length][];
        for (int i = 0; i < Infos.Length; i++)
        {
            // Split the description into its own lines so each \n renders on its own row.
            DescriptionLines[i] = Helper.Transliterate(Infos[i].Description).Split('\n');
            MenuItems.Add(new MenuItem(DisplayTitle(i), "", a => PlayMusic()));
        }
#if SWITCH
        Runtime.SwTrace("[musicroom] CreateMenu done");
#endif
        RefreshLockedTints();
        CurrentX = (int)(Runtime.CurrentRuntime.Scale * 32);
        CurrentY = (int)(Runtime.CurrentRuntime.Scale * 64);
    }

    public override void Render()
    {
        float time = (float)GetTime();
        float s =
            Helper.EaseInOutElasticF(Helper.ComputeObjectTime(time, TimeAppear, 1f, TimeDisappear, 1f));
        CurrentY = (int)(Runtime.CurrentRuntime.Height*(1-s) + s*128*Runtime.CurrentRuntime.ScaleF);
        DrawBackground();
        DrawMenu();
        DrawTitle();
        DrawTitleNotes(time);
        // Navigating away from a warned track cancels its spoiler prompt.
        if (SpoilerPendingIndex >= 0 && SpoilerPendingIndex != SelectedIndex)
            SpoilerPendingIndex = -1;

        // A locked track awaiting its second confirm replaces the description with a spoiler warning; otherwise
        // the description is drawn — each \n line on its own row, the block centred with lines left-aligned.
        // The description is set in the light serif, which needs an outline to stay legible over the busy
        // scrolling background — the menu's own labels get one from the outline shader when their texture is
        // baked, but this text is drawn straight to the screen each frame, so it is stamped by hand.
        var descFont = Runtime.CurrentRuntime.Fonts["notoseriflight"];
        float sf = Runtime.CurrentRuntime.ScaleF;
        bool warning = SpoilerPendingIndex >= 0 && SpoilerPendingIndex == SelectedIndex;
        string[] lines = warning
            ? SpoilerWarning
            : CurrentDescriptionIndex < DescriptionLines.Length ? DescriptionLines[CurrentDescriptionIndex] : [];
        Rgba lineColor = warning ? new Rgba(255, 200, 80, 255) : Rgba.White;
        byte descAlpha = warning ? (byte)255 : Helper.TimeToTransparency(Helper.ComputeObjectTimeStart(time, DescriptionSwitchTime, 0.35));
        float lineH = MeasureTextEx(descFont, "Ay", FontSize, 2).Y + 4 * sf;
        float blockW = 0;
        foreach (string ln in lines)
            blockW = MathF.Max(blockW, MeasureTextEx(descFont, ln, FontSize, 2).X);
        float blockX = (Runtime.CurrentRuntime.Width - blockW) / 2f;
        float y0 = 360 * sf;
        for (int i = 0; i < lines.Length; i++)
            Helper.DrawTextOutlined(descFont, lines[i], new Vector2(blockX, y0 + i * lineH), FontSize, 2,
                lineColor with { A = descAlpha }, new Rgba(0, 0, 0, (byte)(descAlpha * 0.85f)),
                MathF.Max(1f, 1.5f * sf));
    }

    /// <summary>A pair of music notes flanking the title, in the empty margins the "music room" banner leaves on
    /// either side of its lettering. They ride the banner's slide-in and bob gently once it has settled.</summary>
    private void DrawTitleNotes(float time)
    {
        float sf = Runtime.CurrentRuntime.ScaleF;
        float slide = TitleOffsetY;
        float bobL = MathF.Sin(time * 1.5f);
        float bobR = MathF.Sin(time * 1.5f + 1.9f);
        Rgba ink = new Rgba(74, 108, 158, 255);   // the blue of the splash behind the title lettering
        DrawEighthNote(new Vector2(104 * sf, slide + (48 + bobL * 3f) * sf), 54 * sf, -7f + bobL * 4f, ink);
        DrawEighthNote(new Vector2(546 * sf, slide + (54 + bobR * 3f) * sf), 40 * sf, 9f + bobR * 4f,
            ink with { A = 190 });
    }

    /// <summary>Draws an eighth note (a ♪ sign) out of primitives: a scanned ellipse for the head, a rectangle
    /// for the stem and a swept bezier for the flag. It cannot be a glyph — the menu fonts are loaded with the
    /// default ASCII codepoint set, so U+266A would come out as a missing-character box — and there is no
    /// circle primitive in <see cref="Gfx"/>, hence the scanline ellipse.</summary>
    /// <param name="center">Screen position of the note head's centre.</param>
    /// <param name="height">Overall height of the note, head to flag, in screen pixels.</param>
    /// <param name="tiltDeg">Rotation of the whole note about <paramref name="center"/>, in degrees.</param>
    private static void DrawEighthNote(Vector2 center, float height, float tiltDeg, Rgba color)
    {
        float c = MathF.Cos(tiltDeg * MathF.PI / 180f), s = MathF.Sin(tiltDeg * MathF.PI / 180f);
        // Note space → screen: +x right, +y down, rotated by tiltDeg about the head centre.
        Vector2 P(float x, float y) => center + new Vector2(x * c - y * s, x * s + y * c);
        // A quad the long way: centred on the midpoint of p0..p1, thickness across.
        void Bar(Vector2 p0, Vector2 p1, float thickness)
        {
            Vector2 d = p1 - p0;
            float len = d.Length();
            if (len <= 0.01f)
                return;
            float ang = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;
            DrawRectanglePro(new Rect((p0.X + p1.X) / 2f, (p0.Y + p1.Y) / 2f, len, thickness),
                new Vector2(len / 2f, thickness / 2f), ang, color);
        }

        // Head — an ellipse slanted the way a notehead is, filled as horizontal scanlines in head space.
        const float HeadTilt = -24f;
        float hc = MathF.Cos(HeadTilt * MathF.PI / 180f), hs = MathF.Sin(HeadTilt * MathF.PI / 180f);
        float rx = 0.30f * height, ry = 0.21f * height;
        int rows = Math.Max(8, (int)(ry * 2f));
        float step = ry * 2f / rows;
        for (int i = 0; i < rows; i++)
        {
            float ly = -ry + (i + 0.5f) * step;
            float hw = rx * MathF.Sqrt(MathF.Max(0, 1 - ly * ly / (ry * ry)));
            if (hw <= 0.01f)
                continue;
            Vector2 mid = P(-ly * hs, ly * hc);                 // (0, ly) rotated into note space
            Vector2 half = new Vector2(hw * (c * hc - s * hs), hw * (s * hc + c * hs));
            Bar(mid - half, mid + half, step * 1.7f);           // overlap slightly so the rows leave no seams
        }

        // Stem, rising from the right of the head.
        Bar(P(0.272f * height, 0.03f * height), P(0.272f * height, -0.74f * height), 0.058f * height);

        // Flag: a quadratic bezier swept off the stem's tip, tapering as it curls back down.
        Vector2 f0 = new Vector2(0.29f * height, -0.74f * height);
        Vector2 f1 = new Vector2(0.66f * height, -0.62f * height);
        Vector2 f2 = new Vector2(0.40f * height, -0.28f * height);
        const int Segments = 12;
        Vector2 prev = P(f0.X, f0.Y);
        for (int i = 1; i <= Segments; i++)
        {
            float t = i / (float)Segments;
            float u = 1 - t;
            Vector2 p = u * u * f0 + 2 * u * t * f1 + t * t * f2;
            Vector2 next = P(p.X, p.Y);
            Bar(prev, next, (0.105f - 0.070f * t) * height);
            prev = next;
        }
    }

    void PlayMusic()
    {
        int idx = SelectedIndex;
        if (idx >= Infos.Length)
            return;
        bool available = PlayerData.Instance.IsMusicUnlocked(Infos[idx].Number) || Revealed.Contains(idx);
        if (!available)
        {
            if (SpoilerPendingIndex == idx)
            {
                // Second confirm: accept the spoiler, then reveal and play.
                Revealed.Add(idx);
                SpoilerPendingIndex = -1;
                Play(idx);
            }
            else
            {
                // First confirm on a locked track: show the spoiler warning instead of playing.
                SpoilerPendingIndex = idx;
                Helper.PlaySound(Runtime.CurrentRuntime.Sounds["esc"]);
            }
            return;
        }
        Play(idx);
    }

    /// <summary>Reveals the track's description. The track itself stays LOCKED: previewing it here never
    /// exposes the real title (the list keeps showing "???") and never marks the song unlocked — only reaching
    /// it in-game does that. There is no now-playing card here any more; naming what is playing belongs to the
    /// gameplay screen, where <see cref="Gameplay.GameplayOverlays.MusicTitleOverlay"/> does it.</summary>
    private void Play(int idx)
    {
        SwitchDescription(idx);
    }

    /// <summary>The title to show for a track: its real name once unlocked in-game, otherwise the locked "???"
    /// placeholder. Playing a track in the music room never changes this — the song stays locked.</summary>
    private string DisplayTitle(int idx) =>
        PlayerData.Instance.IsMusicUnlocked(Infos[idx].Number)
            ? Infos[idx].Title
            : Infos[idx].NonUnlockedMusicRoomTitle;

    private double DescriptionSwitchTime = 0;
    
    void SwitchDescription(int index)
    {
        if (index >= Infos.Length)
            return;
        DescriptionSwitchTime = GetTime();
        CurrentDescriptionIndex = index;
    }
}
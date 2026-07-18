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
    // Now-playing splash: slides in from the bottom-right when a track is played, holds, then slides out.
    private double SplashTime = -100;
    private string SplashText = "";
    // A locked track shows a spoiler warning on the first confirm and only plays on the second. SpoilerPending
    // is the track currently warned about; Revealed are tracks the player accepted the spoiler for this session.
    private int SpoilerPendingIndex = -1;
    private readonly HashSet<int> Revealed = new();
    
    
    public MusicRoomScreen()
    {
        FontSize = (int)(11 * Runtime.CurrentRuntime.ScaleF);   // a bit smaller than before
    }

    public override void Exiting()
    {
        base.Exiting();
    }

    public override void Activated()
    {
        SwitchDescription(0);
        SelectedIndex = 0;
        base.Activated();
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
        // Navigating away from a warned track cancels its spoiler prompt.
        if (SpoilerPendingIndex >= 0 && SpoilerPendingIndex != SelectedIndex)
            SpoilerPendingIndex = -1;

        // A locked track awaiting its second confirm replaces the description with a spoiler warning; otherwise
        // the description is drawn — each \n line on its own row, the block centred with lines left-aligned.
        var font = Runtime.CurrentRuntime.Fonts["newsreader"];
        float sf = Runtime.CurrentRuntime.ScaleF;
        bool warning = SpoilerPendingIndex >= 0 && SpoilerPendingIndex == SelectedIndex;
        string[] lines = warning
            ? SpoilerWarning
            : CurrentDescriptionIndex < DescriptionLines.Length ? DescriptionLines[CurrentDescriptionIndex] : [];
        Rgba lineColor = warning ? new Rgba(255, 200, 80, 255) : Rgba.White;
        byte descAlpha = warning ? (byte)255 : Helper.TimeToTransparency(Helper.ComputeObjectTimeStart(time, DescriptionSwitchTime, 0.35));
        float lineH = MeasureTextEx(font, "Ay", FontSize, 2).Y + 2 * sf;
        float blockW = 0;
        foreach (string ln in lines)
            blockW = MathF.Max(blockW, MeasureTextEx(font, ln, FontSize, 2).X);
        float blockX = (Runtime.CurrentRuntime.Width - blockW) / 2f;
        float y0 = 360 * sf;
        for (int i = 0; i < lines.Length; i++)
            DrawTextEx(font, lines[i], new Vector2(blockX, y0 + i * lineH), FontSize, 2, lineColor with { A = descAlpha });

        DrawNowPlayingSplash(time, font, sf);
    }

    /// <summary>The now-playing card: a black bar at the bottom-centre of the screen, dressed with
    /// semi-transparent coloured forks that fade out toward the right, squeezing open and shut vertically.</summary>
    private void DrawNowPlayingSplash(float time, FontHandle font, float sf)
    {
        float t = (float)(time - SplashTime);
        if (t < 0 || t > 3.4f)
            return;
        // 0 → 1 → 0 across the splash's life: drives a vertical squeeze open, hold, then squeeze shut.
        float squeeze = (float)Helper.ComputeObjectTime(time, SplashTime, 0.35, SplashTime + 3.0, 0.4);
        if (squeeze <= 0.002f)
            return;

        float pad = 12 * sf, textSize = 16 * sf, margin = 14 * sf;
        float tw = MeasureTextEx(font, SplashText, textSize, 1).X;
        float cardW = MathF.Max(tw + pad * 2, 220 * sf);
        float fullH = textSize + pad * 1.4f;
        float cardH = fullH * squeeze;                                   // vertical squeeze
        float W = Runtime.CurrentRuntime.Width;
        float x = (W - cardW) / 2f;                                      // centred
        float y = Runtime.CurrentRuntime.Height - margin - cardH;       // pinned to the bottom, grows upward
        byte a = (byte)(255 * MathF.Min(1f, squeeze * 1.6f));

        // Black background.
        DrawRectangleRec(new Rect(x, y, cardW, cardH), new Rgba(0, 0, 0, (byte)(a * 0.82f)));

        // Coloured forks across the bar, fading to transparent toward the right edge.
        TextureHandle fork = Runtime.CurrentRuntime.Textures["vilkaCut.png"];
        Vector2 fsz = Helper.GetSize(fork);
        float fh = cardH * 0.92f;
        float fscale = fsz.Y > 0 ? fh / fsz.Y : 1f;
        float fw = fsz.X * fscale;
        Rgba[] forkColors =
        {
            new Rgba(80, 255, 120, 255), new Rgba(255, 90, 90, 255),
            new Rgba(90, 170, 255, 255), new Rgba(255, 210, 60, 255),
        };
        float step = MathF.Max(1f, fw * 0.8f);
        int n = (int)(cardW / step) + 1;
        for (int i = 0; i < n; i++)
        {
            float fx = x + i * step;
            float rightFade = Math.Clamp(1f - (fx - x) / cardW, 0f, 1f);   // 1 at left → 0 (transparent) at right
            byte fa = (byte)(a * 0.32f * rightFade);
            DrawTexturePro(fork, new Rect(0, 0, fsz.X, fsz.Y),
                new Rect(fx, y + (cardH - fh) / 2f, fw, fh), Vector2.Zero, 0,
                forkColors[i % forkColors.Length] with { A = fa });
        }

        // Track name on top, only once the bar is tall enough to hold it.
        if (cardH > textSize * 0.7f)
            DrawTextEx(font, SplashText, new Vector2(x + pad, y + (cardH - textSize) / 2f), textSize, 1,
                Rgba.White with { A = a });
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

    private void Play(int idx)
    {
        SwitchDescription(idx);   // reveal the track's description...
        // ...but keep the track LOCKED: the now-playing splash shows the same locked ("???") title the list does,
        // never the real one. Listening in the music room lets you preview a track and read its description, yet
        // it never reveals the title or marks the song unlocked — that only happens by reaching it in-game.
        SplashText = DisplayTitle(idx);
        SplashTime = GetTime();
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
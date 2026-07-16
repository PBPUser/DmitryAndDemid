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
    private static readonly string[] SpoilerWarning =
    {
        "! ВНИМАНИЕ: СПОЙЛЕР !",
        "Эта композиция ещё не открыта.",
        "Нажмите ещё раз, чтобы всё равно послушать.",
    };
    
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
            // A still-locked track shows its "non-unlocked" title instead of the real one.
            string title = PlayerData.Instance.IsMusicUnlocked(Infos[i].Number)
                ? Infos[i].Title
                : Infos[i].NonUnlockedMusicRoomTitle;
            MenuItems.Add(new MenuItem(title, "", a => PlayMusic()));
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

    /// <summary>The now-playing card that slides in from the bottom-right edge, holds, then slides back out.</summary>
    private void DrawNowPlayingSplash(float time, FontHandle font, float sf)
    {
        float t = (float)(time - SplashTime);
        if (t < 0 || t > 3.4f)
            return;
        // 0 → 1 → 0 across the splash's life, so it slides in and later back out.
        float slide = (float)Helper.ComputeObjectTime(time, SplashTime, 0.35, SplashTime + 3.0, 0.4);
        float pad = 12 * sf, textSize = 16 * sf, margin = 16 * sf;
        float tw = MeasureTextEx(font, SplashText, textSize, 1).X;
        float cardW = tw + pad * 2, cardH = textSize + pad;
        float restX = Runtime.CurrentRuntime.Width - cardW - margin;
        float x = Runtime.CurrentRuntime.Width - (Runtime.CurrentRuntime.Width - restX) * slide;   // in from the right
        float y = Runtime.CurrentRuntime.Height - cardH - margin;
        byte alpha = (byte)(230 * MathF.Min(1f, slide * 1.4f));
        DrawRectangleRec(new Rect(x, y, cardW, cardH), new Rgba(0, 0, 0, (byte)(alpha * 0.65f)));
        DrawTextEx(font, SplashText, new Vector2(x + pad, y + pad / 2f), textSize, 1, Rgba.White with { A = alpha });
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
        SwitchDescription(idx);
        // "Reaching" a track pops the now-playing splash (real audio playback is not yet wired in the engine).
        SplashText = Infos[idx].Title;
        SplashTime = GetTime();
    }

    private double DescriptionSwitchTime = 0;
    
    void SwitchDescription(int index)
    {
        if (index >= Infos.Length)
            return;
        DescriptionSwitchTime = GetTime();
        CurrentDescriptionIndex = index;
    }
}
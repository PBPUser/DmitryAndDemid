using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Data.Archive;
using DmitryAndDemid.Rendering;
using DmitryAndDemid.Utils;
using GLib;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

public class PersonSelectScreen : MenuScreen
{
    public GameType GameType;
    private int Difficulty;

    public PersonSelectScreen(GameType gameType, int difficulty) : base()
    {
        ArtSource = new Rect(0, 0, 200 * Runtime.CurrentRuntime.ScaleF, 400 * Runtime.CurrentRuntime.ScaleF);
        Difficulty = difficulty;
        HorizontalDirectionNavigation = true;
        VerticalDirectionNavigation = false;
        GameType = gameType;
        ArtDestination = Helper.Scale(new Rect(40, 80, 200, 400), Runtime.CurrentRuntime.Scale);
        DescriptionDestination = Helper.Scale(new Rect(320, 100, 280, 200), Runtime.CurrentRuntime.Scale);
        ArtShift = (float)(Runtime.CurrentRuntime.Scale * 40f);
        DescriptionShift = (float)(Runtime.CurrentRuntime.Scale * 30f);
        SetTitle(Runtime.CurrentRuntime.Textures["hero_select.png"]);
        if(gameType == GameType.SpellPractice)
            SetBackground(Runtime.CurrentRuntime.Textures["MenuBackground"]);
    }

    private static Rect RectangleSelectionSource = new Rect(0, 0, 200, 200);
    Rect ArtSource;
    Rect ArtDestination;
    Rect DescriptionDestination;
    private float ArtShift;
    private float DescriptionShift;

    /// <summary>How long the horizontal-axis flip takes when the player switches character. A touch longer
    /// than the input cooldown so the flip reads as a rotation rather than a snap.</summary>
    private const double SwitchFlipDuration = 0.28;

    private BasicTexture[] ArtTextures;
    /// <summary>Each person's description as written text (from the JSON), and the render texture it is baked
    /// into on first use so the flip animation can squash it. Only the description rotates now; the art and the
    /// panel behind it keep their original slide.</summary>
    private string[] Descriptions;
    private RenderedTexture[] DescriptionTextTextures;

    string[] Files;

    public override void CreateMenu()
    {
        Files = Assets.Files("Assets/Data/PlayablePersons/", "*.json");
        ArtTextures = new BasicTexture[Files.Length];
        Descriptions = new string[Files.Length];
        DescriptionTextTextures = new RenderedTexture[Files.Length];
        int i = 0;
        foreach (var x in Files)
        {
            MenuItems.Add(new MenuItem(Path.GetFileNameWithoutExtension(x),"", a => OpenNext()));
            var json = JsonSerializer.Deserialize<ProtogonistData>(File.ReadAllText(x));
            ArtTextures[i] = Runtime.CurrentRuntime.Textures[json.ArtName];
            Descriptions[i] = json.Description;
            i++;
        }
    }

    public override void Render()
    {
        DrawBackground();
        if (MenuItems.Count == 0)
        {
            DrawTitle();
            return;
        }

        float time = (float)GetTime();
        float appear = (float)Helper.ComputeObjectTime(time, TimeAppear, .5f, TimeDisappear, .5f);
        float invertedAppearElastic = Helper.EaseInOutElasticF(1 - appear);
        float index = (float)ComputeAnimationIndexLoop();

        // Person art and the description-panel background keep their ORIGINAL carousel animation: slide in from
        // the side and shrink to the centre on enter/exit, cross-fading between neighbours. No rotation here.
        for (int j = 0; j < MenuItems.Count; j++)
        {
            float position = ((index + 1 - j + MenuItems.Count) % MenuItems.Count) - 1;
            float transparency = 1 - Math.Abs(position);
            DrawTexturePro(
                Runtime.CurrentRuntime.Textures["MenuItemSelectionGradient1"],
                RectangleSelectionSource,
                DescriptionDestination with
                {
                    X = MathUtil.Lerp(DescriptionDestination.X + position * ArtShift, (Runtime.CurrentRuntime.Width + DescriptionDestination.Width) / 2, invertedAppearElastic),
                    Width = MathUtil.Lerp(DescriptionDestination.Width, 0, invertedAppearElastic)
                },
                Vector2.Zero,
                (1 - transparency) * 10f,
                Rgba.White with { A = Helper.TimeToTransparency(appear * transparency) });
            DrawTexturePro(
                ArtTextures[j],
                ArtSource,
                ArtDestination with
                {
                    X = MathUtil.Lerp(ArtDestination.X + position * ArtShift, (Runtime.CurrentRuntime.Width - ArtDestination.Width) / 2, invertedAppearElastic),
                    Width = MathUtil.Lerp(ArtDestination.Width, 0, invertedAppearElastic)
                },
                Vector2.Zero,
                0,
                Rgba.White with { A = Helper.TimeToTransparency(appear * transparency) });
        }

        // The description TEXT is the only thing that rotates. It flips around the vertical axis — its WIDTH
        // squashes to a line and back — on a character switch, and on entering/leaving the screen. An elastic
        // ease on the appear gives the "bounce" as the screen opens.
        float switchT = (float)Math.Clamp((time - AnimationStartedAt) / SwitchFlipDuration, 0, 1);
        float switchFactor = MathF.Abs(MathF.Cos(switchT * MathF.PI));   // 1 -> 0 -> 1, swap face at the midpoint
        int rawIndex = switchT < 0.5f ? PreviousSelectedIndex : SelectedIndex;
        int shown = ((rawIndex % MenuItems.Count) + MenuItems.Count) % MenuItems.Count;
        float openBounce = Helper.EaseInOutElasticF(Math.Clamp(appear, 0f, 1f));   // overshoot-and-settle on open
        float flip = openBounce * switchFactor;

        if (DescriptionTextTextures[shown].Id == 0)
            DescriptionTextTextures[shown] = RenderDescription(Descriptions[shown]);
        RenderedTexture descTarget = DescriptionTextTextures[shown];
        // Centre the description block within the description panel (both axes), so it reads as centred and the
        // width-flip pivots on the panel's middle.
        float descW = descTarget.Texture.Width, descH = descTarget.Texture.Height;
        Rect descDest = new Rect(
            DescriptionDestination.X + (DescriptionDestination.Width - descW) / 2f,
            DescriptionDestination.Y + (DescriptionDestination.Height - descH) / 2f,
            descW, descH);
        DrawXAxisFlip(descTarget.Texture, Helper.GetFullSourceRenderTexture(descTarget), descDest, flip,
            Rgba.White with { A = Helper.TimeToTransparency(appear) });

        DrawTitle();
    }

    /// <summary>
    /// Draws a texture as if it were rotating around the vertical axis (an "X-axis" spin, in the sense the design
    /// asked for: the WIDTH swings). The 2D backend has no real 3D, so the turn is faked by squashing the
    /// destination width about its centre: <paramref name="flip"/> is the cosine of the tilt — 1 face-on, 0
    /// edge-on (an invisible vertical line). Can overshoot 1 (the elastic open bounce), which just makes the
    /// card briefly wider than its resting width.
    /// </summary>
    private static void DrawXAxisFlip(BasicTexture texture, Rect source, Rect dest, float flip, Rgba tint)
    {
        if (flip <= 0f)
            return;
        float centerX = dest.X + dest.Width / 2f;
        float width = dest.Width * flip;
        DrawTexturePro(texture, source, dest with { X = centerX - width / 2f, Width = width },
            Vector2.Zero, 0, tint);
    }

    /// <summary>
    /// Bakes a person's description into a render texture (once, on first use — so it can then be flipped like
    /// any other texture). The text comes from <c>translation.json</c> via <see cref="Helper.Translate"/> (the
    /// JSON field holds the translation KEY), which also transliterates it into the game font. The engine's text
    /// drawing does not wrap or honour '\n', so each line is measured and drawn on its own row — and CENTRED —
    /// exactly as the Music Room lays out its descriptions.
    /// </summary>
    private RenderedTexture RenderDescription(string key)
    {
        FontHandle font = Runtime.CurrentRuntime.Fonts["newsreader"];
        float scale = Runtime.CurrentRuntime.ScaleF;
        float fontSize = 15 * scale;
        float spacing = 1 * scale;
        float pad = 8 * scale;
        // Resolve the key through translation.json (and transliterate) once, here — Translate() picks random
        // variants each call, so it must not run every frame; baking it into the texture pins one result.
        string[] lines = Helper.Translate(key ?? "").Split('\n');
        float lineH = MeasureTextEx(font, "Ay", fontSize, spacing).Y + 3 * scale;
        float[] lineWidths = new float[lines.Length];
        float width = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            lineWidths[i] = MeasureTextEx(font, lines[i], fontSize, spacing).X;
            width = MathF.Max(width, lineWidths[i]);
        }

        int texW = Math.Max(1, (int)MathF.Ceiling(width + pad * 2));
        int texH = Math.Max(1, (int)MathF.Ceiling(lines.Length * lineH + pad * 2));
        RenderedTexture tex = LoadRenderTexture(texW, texH);
        BeginTextureMode(tex);
        ClearBackground(Rgba.Black with { A = 0 });
        float y = pad;
        for (int i = 0; i < lines.Length; i++)
        {
            float x = (texW - lineWidths[i]) / 2f;   // centre each line within the block
            DrawTextEx(font, lines[i], new Vector2(x, y), fontSize, spacing, Rgba.White);
            y += lineH;
        }
        EndTextureMode();
        return tex;
    }

    public override void Unload()
    {
        if (DescriptionTextTextures != null)
            foreach (RenderedTexture t in DescriptionTextTextures)
                if (t.Id != 0)
                    UnloadRenderTexture(t);
        base.Unload();
    }

    public override void Exiting()
    {
        TimeDisappear = (float)(0.5 + GetTime());
        base.Exiting();
    }

    /// <summary>
    /// Fade the art and description out when another screen (spell practice, practice, gameplay) opens on top
    /// of us. Without this only the title faded — ScreenWithTitle handles that — and the character art stayed
    /// at full opacity behind the screen above. Activated() on the way back resets TimeDisappear, so escaping
    /// back here fades it in again.
    /// </summary>
    public override void Deactivated()
    {
        TimeDisappear = (float)GetTime() + DisappearingTime;
        base.Deactivated();
    }

    void OpenNext()
    {
        string data = File.ReadAllText(Files[SelectedIndex]);
        var protogonistData = JsonSerializer.Deserialize<ProtogonistData>(data);
        if (protogonistData == null)
            throw new Exception();
        bool UseTLS = false;
        if (GameType == GameType.Practice)
            Runtime.CurrentRuntime.AddScreen(new PracticeScreen(protogonistData, Difficulty));
        else if (GameType == GameType.Default)
        {
            // The whole campaign, in order: the run advances stage-to-stage (see GameBox's stage-complete flow),
            // so load every stage up front rather than just the first. Filtered to .sid via the shared helper —
            // enumerating "*" and taking [0] would load a stray non-stage file and crash.
            var stages = FileStageInfo.CampaignStagePaths()
                .Select(FileStageInfo.LoadFromFile).ToArray();
            UseTLS = true;
            var gamePlayScreen = new GameplayScreen(protogonistData, Difficulty, stages, 0, false);
            Runtime.CurrentRuntime.AddScreen(gamePlayScreen);
        }
        else if (GameType == GameType.Extra)
        {
            // Extra is its own self-contained run — FileStageInfo.ExtraStagePaths() is the counterpart split to
            // CampaignStagePaths(), so this never pulls in (or from) the main story's stages.
            var stages = FileStageInfo.ExtraStagePaths()
                .Select(FileStageInfo.LoadFromFile).ToArray();
            UseTLS = true;
            var gamePlayScreen = new GameplayScreen(protogonistData, Difficulty, stages, 0, false, mode: GameType.Extra);
            Runtime.CurrentRuntime.AddScreen(gamePlayScreen);
        }
        else
        {
            // SetProtogonistData existed but was never called — the spell-practice screen had no idea who
            // the player had picked.
            SpellPracticeScreen.Instance.SetProtogonistData(protogonistData, Difficulty);
            Runtime.CurrentRuntime.AddScreen(SpellPracticeScreen.Instance);
        }

        if (UseTLS)
        {
            Helper.PlaySound(Runtime.CurrentRuntime.Sounds["swap"]);
            TiledLoadingScreen? tls = null;
            tls = new TiledLoadingScreen(3, 0.5, () =>
            {
                Runtime.CurrentRuntime.RemoveScreen(tls);
            }, true, 0);
            tls.CaptureFrom(this);   // wipe in over the character-select screen instead of hard-cutting to it
            Runtime.CurrentRuntime.AddScreen(tls);
        }
    }
}

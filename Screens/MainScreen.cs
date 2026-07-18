using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
#if DEBUG
using ImGuiNET;
#endif
using static DmitryAndDemid.Rendering.Gfx;

namespace DmitryAndDemid.Screens;

public class MainScreen : MenuScreen
{

    int TitleIndex = 0;
    int selectedIndex = 0;
    TextureHandle SelectedPerson;

    Rect
        RCPersonSource,
        RCPersonTarget1,
        RCPersonTarget2;

    static string[] PersonImageNames = { "demid_2.png", "dima.png" };

    public override void Unload()
    {

    }

    public MainScreen() : base()
    {
#if SWITCH
        Runtime.SwTrace("[ms] ctor body enter (base done)");
#endif
        AllowExitWithEscape = false;

        Size = (int)(Runtime.CurrentRuntime.Scale * 32);
        AppearTime = GetTime();
        LogoTargetLeft = new Rect(0, 0, (float)(130 * Runtime.CurrentRuntime.Scale), (float)(95 * Runtime.CurrentRuntime.Scale));
        LogoTargetRight = new Rect(Runtime.CurrentRuntime.Width - (float)(135 * Runtime.CurrentRuntime.Scale), 0, (float)(135 * Runtime.CurrentRuntime.Scale), (float)(52 * Runtime.CurrentRuntime.Scale));

        Configuration.Config.FastLoading = true;
#if SWITCH
        Runtime.SwTrace("[ms] before Config.Save");
#endif
        Configuration.Config.Save();
#if SWITCH
        Runtime.SwTrace("[ms] after Config.Save");
#endif

        TitleIndex = new Random().Next(0, PersonImageNames.Count());
        SelectedPerson = Runtime.CurrentRuntime.Textures[PersonImageNames[TitleIndex]];

        int titlePersonHeight = (int)(360 * Runtime.CurrentRuntime.Scale);
        int titlePersonY = (int)(120 * Runtime.CurrentRuntime.Scale);
        int titlePersonWidth = (int)(360 * SelectedPerson.Height / SelectedPerson.Height * Runtime.CurrentRuntime.Scale);

        RCPersonTarget1 = new Rect(Runtime.CurrentRuntime.Width, titlePersonY, titlePersonWidth, titlePersonHeight);
        RCPersonTarget2 = new Rect(Runtime.CurrentRuntime.Width - titlePersonWidth, titlePersonY, titlePersonWidth, titlePersonHeight);
        RCPersonSource = new Rect(0, 0, SelectedPerson.Width, SelectedPerson.Height);

        // The title logo is scaled to fill ~90% of the screen and drawn rotated 45° (see Render). A rotated
        // rectangle's axis-aligned bounding box is bigger than the rectangle itself, so the size is derived from
        // that bounding box — that way the *rotated* logo (not just its unrotated rect) fits inside 90% of the
        // screen. Uses the logo texture's real aspect ratio so it is not stretched.
        var logoTex = Runtime.CurrentRuntime.Textures["game_logo.png"];
        LogoSourceCenter = Helper.GetFullSource(logoTex);
        float logoAspect = (float)logoTex.Width / logoTex.Height;
        // bbox side after a 45° rotation = (w + h) * cos45°, with h = w / aspect  →  bbox = w * k
        float k = (1f + 1f / logoAspect) * 0.70710678f;
        float fitBudget = 0.9f * Math.Min(Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height);
        int logoCenterWidth = (int)(fitBudget / k);
        int logoCenterHeight = (int)(logoCenterWidth / logoAspect);
        int logoCenterX = Runtime.CurrentRuntime.Width / 2;
        int logoCenterY = Runtime.CurrentRuntime.Height / 2;
        int bboxHalf = (int)(logoCenterWidth * k / 2);
        // Drawn origin-centred (see Render), so these are the logo's CENTRE point. It rests dead-centre and
        // drops in from fully above the top edge, growing slightly into place.
        LogoTargetCenter1 = new Rect(logoCenterX, -bboxHalf, logoCenterWidth * 0.85f, logoCenterHeight * 0.85f);
        LogoTargetCenter2 = new Rect(logoCenterX, logoCenterY, logoCenterWidth, logoCenterHeight);

        // Pre-render the logo centred in a padded transparent target, so the neon halo bleeds into the margin
        // instead of being cropped at the art's edge. Margin ~12% of the art on each side (well over the halo's
        // ~12-texel reach).
        int padX = Math.Max(24, logoTex.Width / 8);
        int padY = Math.Max(24, logoTex.Height / 8);
        LogoPadded = LoadRenderTexture(logoTex.Width + padX * 2, logoTex.Height + padY * 2);
        BeginTextureMode(LogoPadded);
        ClearBackground(new Rgba(0, 0, 0, 0));
        DrawTexture(logoTex, padX, padY, Rgba.White);
        EndTextureMode();
        LogoPadScaleX = (float)LogoPadded.Texture.Width / logoTex.Width;
        LogoPadScaleY = (float)LogoPadded.Texture.Height / logoTex.Height;

        CurrentY = (int)(160 * Runtime.CurrentRuntime.Scale);
#if SWITCH
        Runtime.SwTrace("[ms] before new MusicRoomScreen()");
#endif
        MusicRoom = new MusicRoomScreen();
        SelectedItemOffset = new Vector2(8, 0) * Runtime.CurrentRuntime.ScaleF;
        SelectedItemScale = 1.2f;
#if SWITCH
        Runtime.SwTrace("[ms] before new TrophyScreen()");
#endif
        TrophyScreen = new TrophyScreen();
#if SWITCH
        Runtime.SwTrace("[ms] ctor body done");
#endif
    }
#if DEBUG
    private int LineX = 0, LineY = 0;
#endif
    private MusicRoomScreen MusicRoom;
    private TrophyScreen TrophyScreen;
    static Rgba DarkRed = new Rgba(178, 0, 0, 255); // was Color(0.7f,0,0,1) — Raylib's float ctor is 0..1
    static Rect LogoSourceLeft = new Rect(0, 0, 260, 190);
    static Rect LogoSourceRight = new Rect(810, 0, 270, 105);
    Rect LogoSourceCenter;   // the game_logo texture's real full-source rect; set in the constructor
    public bool IsOnTop = false;

    Rect LogoTargetLeft;
    Rect LogoTargetRight;
    Rect LogoTargetCenter1;
    Rect LogoTargetCenter2;

    // The title logo pre-rendered into a transparent, PADDED target so the neon_sign halo has room to bleed
    // instead of clipping at the sprite's rectangular edge. LogoPadScale is the padded size / art size.
    TargetHandle LogoPadded;
    float LogoPadScaleX = 1f, LogoPadScaleY = 1f;

    int Size;
    double AppearTime;

    double
        TimeAppearMenu = 5.5, TimeDisappearMenu = 999999999;

    public override void Activated()
    {
        TimeAppearMenu = Math.Max(5.5, GetTime() - AppearTime);
        TimeDisappearMenu = 99999999999;
        IsOnTop = true;
        LastActivityTime = GetTime();   // start counting idle time fresh whenever the title regains focus
    }

    public override void Deactivated()
    {
        TimeDisappearMenu = GetTime() - AppearTime + 0.5;
        IsOnTop = false;
    }

    // Attract mode: after a spell of inactivity on the title screen, play one of the saved replays as a demo,
    // cycling through them. The demo (a GameplayScreen with IsDemo) sits over the menu and returns here on any
    // input, on death, or when its run clears.
    private const double DemoIdleSeconds = 20;
    private double LastActivityTime;
    private int DemoIndex;

    public override void TopUpdate()
    {
        if (AttractInput.AnyInput())
            LastActivityTime = GetTime();
        else if (IsOnTop && GetTime() - LastActivityTime > DemoIdleSeconds)
            StartDemo();
        base.TopUpdate();
    }

    private void StartDemo()
    {
        LastActivityTime = GetTime();   // reset even if it fails, so a broken replay does not retry every frame
        string[] replays = ReplayLauncher.FindReplays();
        if (replays.Length == 0)
            return;
        GameplayScreen? demo = ReplayLauncher.Build(replays[DemoIndex % replays.Length], demo: true);
        DemoIndex++;
        if (demo == null)
            return;
        Runtime.CurrentRuntime.AddScreen(demo);
        // Cover the demo's start-up with a plain black fade + rotating fifo (a subtler treatment than the tiled
        // loading animation used when entering a real game), then reveal the demo when it fades out.
        Helper.PlaySound(Runtime.CurrentRuntime.Sounds["swap"]);
        BlackLoadingScreen? loader = null;
        loader = new BlackLoadingScreen(1.2, 0.4, () => Runtime.CurrentRuntime.RemoveScreen(loader), true, 0);
        Runtime.CurrentRuntime.AddScreen(loader);
    }

    private const int PizzaCount = 10;
    private static float Frac(float v) => v - MathF.Floor(v);

    /// <summary>
    /// Decorative pizzas drifting slowly up the title screen and lazily spinning, each bobbing side to side.
    /// Purely a function of time and index (a stable per-pizza pseudo-random spread), so there is no state to
    /// carry — mirrors the falling-forks backdrop other screens use.
    /// </summary>
    private void DrawFloatingPizzas(float time, float appear)
    {
        if (!Runtime.CurrentRuntime.Textures.TryGetValue("pizza.png", out TextureHandle pizza))
            return;
        float width = Runtime.CurrentRuntime.Width;
        float height = Runtime.CurrentRuntime.Height;
        float scale = Runtime.CurrentRuntime.ScaleF;
        Rect source = new(0, 0, pizza.Width, pizza.Height);
        for (int i = 0; i < PizzaCount; i++)
        {
            float r1 = Frac(MathF.Sin(i * 12.9898f) * 43758.55f);
            float r2 = Frac(MathF.Sin(i * 78.233f) * 12543.13f);
            float r3 = Frac(MathF.Sin(i * 39.425f) * 21783.19f);

            float size = (28f + r1 * 34f) * scale;
            float drawScale = size / pizza.Height;
            float floatSpeed = (12f + r1 * 16f) * scale;
            float span = height + size * 2;
            // drift upward (wrapping), sway sideways, spin lazily
            float y = span - ((time * floatSpeed + r2 * span) % span) - size;
            float x = r3 * width + MathF.Sin(time * (0.4f + r2 * 0.5f) + i) * 36f * scale;
            float rotation = time * (18f + r1 * 30f) + i * 53f;

            Rgba tint = Rgba.White with { A = (byte)(150 * appear) };
            DrawTexturePro(pizza, source,
                new Rect(x, y, pizza.Width * drawScale, pizza.Height * drawScale),
                new Vector2(pizza.Width * drawScale / 2, pizza.Height * drawScale / 2), rotation, tint);
        }
    }

    public override void Render()
    {
        var z = GetTime() - AppearTime;
        float
            appear1 = (float)Helper.ComputeObjectTimeStart(z, 5, 1),
            appear2 = (float)Helper.ComputeObjectTimeStart(z, 1, 1),
            appear25 = (float)Helper.ComputeObjectTimeStart(z, 2, 0.5),
            appear3 = (float)Helper.ComputeObjectTime(z, TimeAppearMenu, 0.9, TimeDisappearMenu, 0.5),
            appear4 = (float)Helper.ComputeObjectTime(z, TimeAppearMenu, 0.25, TimeDisappearMenu, 0.5),
            appear5 = (float)Helper.ComputeObjectTime(z, TimeAppearMenu, 0.5, TimeDisappearMenu, 0.5);
        ;
        float time = (float)GetTime();
#if DEBUG
        var bg = Rgba.White;
#else
        var bg = Helper.Mix(Rgba.Black, Rgba.White, (float)appear2);
#endif
        DrawRectangle(0, 0, Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height, bg);
        var color1 = Helper.Mix(Rgba.Black, Rgba.Red, (float)appear2);
        var color2 = Helper.Mix(Rgba.Black, DarkRed, (float)appear2);
        CurrentX = (int)((16 - (Helper.Pow2F(1 - appear5) * 384)) * Runtime.CurrentRuntime.Scale);
#if DEBUG
        CurrentX = IsOnTop ? 0 : -10000;
#endif
        Helper.DrawWave(color1, MathF.Sin(time) + 1.5f, -0.7f - Helper.EaseInOutElasticF(appear1) * 1.5f, 1.5f, 1.5f, Runtime.CurrentRuntime.FullScreenRect);
        Helper.DrawWave(color2, MathF.Sin(time + 0.1f) + 1.5f, -0.7f - Helper.EaseInOutElasticF(appear3) * 1.5f, 1.5f, 1.5f, Runtime.CurrentRuntime.FullScreenRect);
        DrawTexturePro(Runtime.CurrentRuntime.Textures["telecom.png"], LogoSourceLeft, LogoTargetLeft, Vector2.Zero, 0f, Rgba.White with { A = Helper.TimeToTransparency(appear25) });
        DrawTexturePro(Runtime.CurrentRuntime.Textures["telecom.png"], LogoSourceRight, LogoTargetRight, Vector2.Zero, 0f, Rgba.White with { A = Helper.TimeToTransparency(appear25) });
        // Floating pizzas drawn AFTER the top telecom logos so they pass in front of them (requested).
        DrawFloatingPizzas(time, (float)appear2);
        // The menu list is drawn AFTER the pizzas so its entries stay readable ABOVE them (requested).
        DrawMenu();
        DrawTexturePro(SelectedPerson, RCPersonSource, Helper.Mix(RCPersonTarget1, RCPersonTarget2, appear4), Vector2.Zero, 0f, Rgba.White);
        // The title logo, drawn AFTER the person so it sits ABOVE (in front of) them. Centred, rotated 45°, and
        // lit through the neon_sign shader so it glows and flickers like an old sign; origin at its own centre so
        // placement and rotation pivot are the logo's middle. Still uses the elastic drop-in animation.
        var logoDest = Helper.Mix(LogoTargetCenter1, LogoTargetCenter2, Helper.EaseInOutElasticF(appear3));
        // Once it has dropped in, the title floats gently up and down — the same bob the manual page uses in
        // its view mode.
        logoDest.Y += MathF.Sin(time * 1.6f) * 9f * Runtime.CurrentRuntime.ScaleF * appear3;
        // Draw the PADDED logo, enlarging the destination by the pad ratio so the art itself stays the same
        // on-screen size while the transparent margin (holding the halo) extends beyond it.
        var paddedDest = new Rect(logoDest.X, logoDest.Y, logoDest.Width * LogoPadScaleX, logoDest.Height * LogoPadScaleY);
        var logoOrigin = new Vector2(paddedDest.Width / 2, paddedDest.Height / 2);
        var neon = Runtime.CurrentRuntime.Shaders["neon_sign"];
        SetShaderValue(neon, GetShaderLocation(neon, "time"), time, UniformType.Float);
        SetShaderValue(neon, GetShaderLocation(neon, "resolution"),
            new Vector2(LogoPadded.Texture.Width, LogoPadded.Texture.Height), UniformType.Vec2);
        BeginShaderMode(neon);
        DrawTexturePro(LogoPadded.Texture, Helper.GetFullSourceRenderTexture(LogoPadded), paddedDest, logoOrigin, 45f, Rgba.White);
        EndShaderMode();
        var source = Helper.GetFullSource(Runtime.CurrentRuntime.Textures["Version"]);
        DrawTexturePro(
            Runtime.CurrentRuntime.Textures["Copyright"],
            source,
            new Rect(
                .5f * (Runtime.CurrentRuntime.Width - source.Width),
                Runtime.CurrentRuntime.Height - source.Height * appear3,
                source.Width,
                source.Height
            ),
            Vector2.Zero,
            0,
            Rgba.White with {A = Helper.TimeToTransparency(appear3)}
        );
        DrawTexturePro(
            Runtime.CurrentRuntime.Textures["Version"],
             source,
            new Rect(
                Runtime.CurrentRuntime.Width - source.Width,
                Runtime.CurrentRuntime.Height - source.Height - (MathF.Sin(time * 24) * 13 + 32) * Runtime.CurrentRuntime.ScaleF + 64 * Runtime.CurrentRuntime.ScaleF * (1-appear3),
                source.Width,
                source.Height
                ),
            Vector2.Zero,
            MathF.Sin(time * 16) * 4 ,
            Rgba.White with {A = Helper.TimeToTransparency(appear3)}
            );
    }
    
#if DEBUG
    public override void DrawImgui()
    {
        ImGui.Begin("Gamepad testing window");
        var hp = Helper.GetDirection(new Vector2(LineX, LineY), GetMousePosition());
        var angle = Helper.FindAngle(new Vector2(LineX, LineY), GetMousePosition());
        var angleDirection = Helper.GetDirection(angle);
        DrawLine(LineX, LineY, LineX+(int)(hp.X * 500), LineY+(int)(hp.Y * 500), Rgba.Blue);
        DrawTextureEx(Runtime.CurrentRuntime.Textures["pizza.png"], new Vector2(LineX, LineY), (float)angle * 180 / MathF.PI, 1, Rgba.White);
        ImGui.TextUnformatted($"Direction: {hp}");ImGui.TextUnformatted($"Direction: {hp}");
        ImGui.TextUnformatted($"Coords: {hp*500}");
        for (int i = 0; i < Runtime.CurrentRuntime.GamepadCount; i++)
            ImGui.TextUnformatted($"Gamepad {i}: {IsGamepadButtonDown(i, PadButton.LeftFaceUp)} {IsGamepadButtonDown(i, PadButton.RightFaceLeft)}");
        ImGui.ShowDemoWindow();
        ImGui.End();
    }
#endif

    public override void CreateMenu()
    {
#if SWITCH
        Runtime.SwTrace("[ms] CreateMenu enter");
#endif
        int j = 1;
#if DEBUG
        // The editor is an ImGui screen, and ImGui only runs on the Raylib backend (Engine.Backend
        // .SupportsDebugUi). Under Silk/OpenGL or Vulkan the entry would open a screen that draws nothing and
        // cannot be escaped, so it is not offered there at all.
        if (Engine.Backend.SupportsDebugUi)
        {
            j++;
            MenuItems.Add(new MenuItem("menu.editor", "", a => Runtime.CurrentRuntime.AddScreen(new GameplayEditorScreen())));
        }
#endif
        MenuItems.Add(new MenuItem("menu.start", "", a => Runtime.CurrentRuntime.AddScreen(new DifficultyScreen(GameType.Default))));
        MenuItems.Add(new MenuItem("menu.extra", "", a => Runtime.CurrentRuntime.AddScreen(new DifficultyScreen(GameType.Extra))));
        MenuItems.Add(new MenuItem("menu.practice", "", a => Runtime.CurrentRuntime.AddScreen(new DifficultyScreen(GameType.Practice))));
        MenuItems.Add(new MenuItem("menu.spell", "", a => Runtime.CurrentRuntime.AddScreen(new PersonSelectScreen(GameType.SpellPractice, 0))));
        MenuItems.Add(new MenuItem("menu.replay", "", a => Runtime.CurrentRuntime.AddScreen(new ReplayScreen())));
        MenuItems.Add(new MenuItem("menu.stats", "", a => Runtime.CurrentRuntime.AddScreen(new ScoreScreen())));
        MenuItems.Add(new MenuItem("menu.music", "", a => Runtime.CurrentRuntime.AddScreen(MusicRoom)));
        MenuItems.Add(new MenuItem("menu.trophy", "", a => Runtime.CurrentRuntime.AddScreen(TrophyScreen)));
        MenuItems.Add(new MenuItem("menu.settings", "", a => Runtime.CurrentRuntime.AddScreen(new SettingsScreen())));
        MenuItems.Add(new MenuItem("menu.manual", "", a => Runtime.CurrentRuntime.AddScreen(new ManualScreen())));
        MenuItems.Add(new MenuItem("menu.exit", "", a => Environment.Exit(0)));
#if SWITCH
        Runtime.SwTrace("[ms] CreateMenu: accessing PlayerData.Instance");
#endif
        MenuItems[j].Enabled = PlayerData.Instance.IsExtraUnlocked;
        MenuItems[j + 1].Enabled = PlayerData.Instance.IsStageUnlocked(0);
        MenuItems[j + 2].Enabled = true;   // the score menu is always viewable (it shows a per-character board)
#if SWITCH
        Runtime.SwTrace("[ms] CreateMenu done");
#endif
    }

    public override void PreRender(double delta)
    {
        #if DEBUG
        if (IsMouseButtonDown(MouseBtn.Left))
        {
            LineX = GetMouseX();
            LineY = GetMouseY();
        }
        #endif
        if (GetTime() - AppearTime < 6)
            return;
    }
}

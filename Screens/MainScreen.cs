using DmitryAndDemid.Rendering;
using System.Numerics;
using DmitryAndDemid.Common;
using DmitryAndDemid.Data;
using DmitryAndDemid.Utils;
using Gdk;
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

        // The two corner station logos are lit through the same neon_sign shader as the title, so they get the
        // same treatment: each is lifted out of the telecom sheet into its own padded transparent target. That
        // is doubly necessary here — besides giving the halo room, it stops the halo taps from reaching past
        // the crop and dragging in the rest of the sheet (the big red waves further down it).
        var telecomTex = Runtime.CurrentRuntime.Textures["telecom.png"];
        (SideLogoLeft, SideLogoLeftTarget) = PadForGlow(telecomTex, LogoSourceLeft, LogoTargetLeft);
        (SideLogoRight, SideLogoRightTarget) = PadForGlow(telecomTex, LogoSourceRight, LogoTargetRight);

        LogoGlowBuffer = LoadRenderTexture(LogoPadded.Texture.Width, LogoPadded.Texture.Height);
        SideLogoLeftGlowBuffer = LoadRenderTexture(SideLogoLeft.Texture.Width, SideLogoLeft.Texture.Height);
        SideLogoRightGlowBuffer = LoadRenderTexture(SideLogoRight.Texture.Width, SideLogoRight.Texture.Height);

        BackdropTexture = Runtime.CurrentRuntime.Textures["rediska-na-fon.png"];
        BackdropComposite = LoadRenderTexture(BackdropTexture.Width, BackdropTexture.Height);

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

    // The corner station logos, likewise padded (see PadForGlow), with the destination rects grown to match.
    TargetHandle SideLogoLeft, SideLogoRight;
    Rect SideLogoLeftTarget, SideLogoRightTarget;

    // neon_sign scratch buffers, one per lit sprite, sized to that sprite's own (small, fixed) padded resolution
    // rather than its on-screen destination. DrawNeonSign shades into these instead of straight onto the window,
    // so its ~80-tap kernel runs once per SOURCE pixel and never scales with the player's chosen resolution —
    // the same "render small, upscale for free" trick GameBox already uses for the playfield.
    TargetHandle LogoGlowBuffer, SideLogoLeftGlowBuffer, SideLogoRightGlowBuffer;

    // The backdrop + wallpaper glow shaders likewise render into this fixed, native-resolution composite (sized
    // to rediska-na-fon.png itself) instead of directly at window size, then get upscaled with one cheap,
    // shader-free blit.
    TextureHandle BackdropTexture;
    TargetHandle BackdropComposite;

    int Size;
    double AppearTime;

    double
        TimeAppearMenu = 5.5, TimeDisappearMenu = 999999999;

    public override void Activated()
    {
        if (Configuration.Config.IsMenuLagEnabled)
            Runtime.CurrentRuntime.ContinueWorkIn = GetTime() + 0.25;
        TimeAppearMenu = Math.Max(5.5, GetTime() - AppearTime);
        TimeDisappearMenu = 99999999999;
        IsOnTop = true;
        LastActivityTime = GetTime() + .5;   // start counting idle time fresh whenever the title regains focus
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

    // Glow tuning for the backdrop (see Assets/Shaders/ink_glow.fs): rgb is the colour the light glows in, w is
    // overall strength. The radius is in TEXELS OF THE SAMPLED TEXTURE — the backdrop is a 1280x960 image
    // stretched over the whole window, so it takes a wide reach to read as a soft wash of light on screen.
    static readonly Vector4 BackdropGlow = new(0.34f, 0.64f, 1.00f, 2.2f);   // electric blue, matching the paint
    const float BackdropGlowRadius = 64f;

    // The glowing wallpaper that creeps up over the backdrop's dark shapes (Assets/Shaders/soviet_wallpaper.fs).
    // Deep in the red channel on purpose: the shader overdrives these, and a tint whose channels are already
    // near 1 would just bloom to white instead of staying blue and purple.
    static readonly Vector4 WallpaperColorA = new(0.28f, 0.62f, 1.00f, 1f);   // light blue
    static readonly Vector4 WallpaperColorB = new(0.55f, 0.22f, 1.00f, 1f);   // purple
    const float WallpaperScroll = 0.05f;    // tiles per second, upward
    const float WallpaperStrength = 0.85f;
    const float WallpaperScale = 0.65f;     // tiles across the screen width; below 1 enlarges the flowers

    /// <summary>
    /// Lifts an atlas crop into its own transparent, PADDED render target so a glow shader's halo has room to
    /// bleed instead of clipping at the crop's edge (and so its taps cannot stray into neighbouring art on the
    /// sheet). Returns the target plus <paramref name="dest"/> grown by the same margin, which keeps the art
    /// itself at exactly the on-screen size and position it had before padding.
    /// </summary>
    private static (TargetHandle Target, Rect Dest) PadForGlow(TextureHandle atlas, Rect source, Rect dest)
    {
        const int pad = 20;   // comfortably over neon_sign's ~12-texel halo reach
        TargetHandle target = LoadRenderTexture((int)source.Width + pad * 2, (int)source.Height + pad * 2);
        BeginTextureMode(target);
        ClearBackground(new Rgba(0, 0, 0, 0));
        DrawTexturePro(atlas, source, new Rect(pad, pad, source.Width, source.Height), Vector2.Zero, 0f, Rgba.White);
        EndTextureMode();
        float scaleX = dest.Width / source.Width, scaleY = dest.Height / source.Height;
        return (target, new Rect(dest.X - pad * scaleX, dest.Y - pad * scaleY,
                                 dest.Width + 2 * pad * scaleX, dest.Height + 2 * pad * scaleY));
    }

    /// <summary>
    /// Draws a padded sprite target lit through the neon_sign shader — the glowing, flickering old-sign look the
    /// title logo wears, shared by the corner station logos.
    ///
    /// The shader pass itself renders into <paramref name="glowBuffer"/> — a scratch target the same (small,
    /// fixed) size as <paramref name="target"/> — with <paramref name="tint"/> applied there exactly as before
    /// (the shader reads it as fragColor and folds it into the glow math, so it has to stay on that pass, not
    /// move to the final blit). The lit result is then upscaled to <paramref name="dest"/> with one plain,
    /// shader-free blit, so the ~80-tap kernel runs at the sprite's own pixel count instead of scaling with
    /// however large <paramref name="dest"/> is on screen (up to most of the window, for the title logo).
    /// </summary>
    private static void DrawNeonSign(TargetHandle target, TargetHandle glowBuffer, Rect dest, Vector2 origin, float rotation, Rgba tint)
    {
        var neon = Runtime.CurrentRuntime.Shaders["neon_sign"];
        SetShaderValue(neon, GetShaderLocation(neon, "time"), (float)GetTime(), UniformType.Float);
        SetShaderValue(neon, GetShaderLocation(neon, "resolution"),
            new Vector2(target.Texture.Width, target.Texture.Height), UniformType.Vec2);

        BeginTextureMode(glowBuffer);
        BeginShaderMode(neon);
        DrawTexturePro(target.Texture, Helper.GetFullSourceRenderTexture(target),
            new Rect(0, 0, target.Texture.Width, target.Texture.Height), Vector2.Zero, 0f, tint);
        EndShaderMode();
        EndTextureMode();

        DrawTexturePro(glowBuffer.Texture, Helper.GetFullSourceRenderTexture(glowBuffer), dest, origin, rotation, Rgba.White);
    }

    /// <summary>
    /// Draws the backdrop lit through the ink_glow shader: its paint spills coloured light onto the paper around
    /// it and the vivid blues bloom. It is an opaque image, so it cannot use the alpha-keyed neon_sign.
    /// </summary>
    private static void DrawGlowingBackdrop(TextureHandle texture, Rect dest, Rgba tint)
    {
        var shader = Runtime.CurrentRuntime.Shaders["ink_glow"];
        SetShaderValue(shader, GetShaderLocation(shader, "time"), (float)GetTime(), UniformType.Float);
        SetShaderValue(shader, GetShaderLocation(shader, "resolution"),
            new Vector2(texture.Width, texture.Height), UniformType.Vec2);
        SetShaderValue(shader, GetShaderLocation(shader, "glow_color"), BackdropGlow, UniformType.Vec4);
        SetShaderValue(shader, GetShaderLocation(shader, "radius"), BackdropGlowRadius, UniformType.Float);
        BeginShaderMode(shader);
        DrawTexturePro(texture, Helper.GetFullSource(texture), dest, Vector2.Zero, 0f, tint);
        EndShaderMode();
    }

    /// <summary>
    /// Draws the floral wallpaper crawling up the screen, glowing blue-to-purple and blooming, showing only
    /// where the backdrop behind it is dark. The backdrop is handed in as the shader's mask, so this has to be
    /// drawn over the same rect the backdrop occupies for the two to line up.
    /// </summary>
    private static void DrawGlowingWallpaper(TextureHandle backdrop, Rect dest, Rgba tint)
    {
        var wallpaper = Runtime.CurrentRuntime.Textures["soviet-wallpaper.png"];
        var shader = Runtime.CurrentRuntime.Shaders["soviet_wallpaper"];
        // At most one tile spans the screen width, so the pattern never crosses a horizontal seam (only the
        // vertical wrap, which the shader cross-fades). The vertical tiling follows from the wallpaper's own
        // aspect, which keeps the flowers round rather than stretched.
        float tileY = WallpaperScale * dest.Height / dest.Width * wallpaper.Width / wallpaper.Height;
        SetShaderValue(shader, GetShaderLocation(shader, "time"), (float)GetTime(), UniformType.Float);
        SetShaderValue(shader, GetShaderLocation(shader, "resolution"),
            new Vector2(wallpaper.Width, wallpaper.Height), UniformType.Vec2);
        SetShaderValue(shader, GetShaderLocation(shader, "tiling"),
            new Vector2(WallpaperScale, tileY), UniformType.Vec2);
        SetShaderValue(shader, GetShaderLocation(shader, "scroll"), WallpaperScroll, UniformType.Float);
        SetShaderValue(shader, GetShaderLocation(shader, "strength"), WallpaperStrength, UniformType.Float);
        SetShaderValue(shader, GetShaderLocation(shader, "color_a"), WallpaperColorA, UniformType.Vec4);
        SetShaderValue(shader, GetShaderLocation(shader, "color_b"), WallpaperColorB, UniformType.Vec4);
        SetShaderValueTexture(shader, GetShaderLocation(shader, "backdropTexture"), backdrop);
        BeginShaderMode(shader);
        DrawTexturePro(wallpaper, Helper.GetFullSource(wallpaper), dest, Vector2.Zero, 0f, tint);
        EndShaderMode();
    }

    /// <summary>
    /// Decorative pizzas drifting slowly up the title screen and lazily spinning, each bobbing side to side.
    /// Purely a function of time and index (a stable per-pizza pseudo-random spread), so there is no state to
    /// carry — mirrors the falling-forks backdrop other screens use.
    ///
    /// <paramref name="burst"/> (0 → 1 → 0, the menu's activation flourish) blows them outward from the centre
    /// of the screen and spins them up while an entry is being chosen — the title reacting to the player rather
    /// than ignoring them. It returns to zero on its own, so the drift is left exactly as it was.
    /// </summary>
    private void DrawFloatingPizzas(float time, float appear, float burst)
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

            if (burst > 0f)
            {
                // Shoved straight out from the middle of the screen, each spinning the way its own seed says.
                float dx = x - width / 2f, dy = y - height / 2f;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance > 0.001f)
                {
                    x += dx / distance * burst * 46f * scale;
                    y += dy / distance * burst * 46f * scale;
                }
                rotation += burst * 260f * (r3 > 0.5f ? 1f : -1f);
                drawScale *= 1f + burst * 0.3f;
            }

            Rgba tint = Rgba.White with { A = (byte)(150 * appear) };
            DrawTexturePro(pizza, source,
                new Rect(x, y, pizza.Width * drawScale, pizza.Height * drawScale),
                new Vector2(pizza.Width * drawScale / 2, pizza.Height * drawScale / 2), rotation, tint);
        }
    }

    /// <summary>
    /// The character standing at the right of the title. They used to be a still image pasted at a rect; now
    /// they sway on the spot and — the point of it — flinch each time the menu cursor moves, hopping a little
    /// and leaning into the direction it went. The pivot is at their FEET, so a lean reads as a lean rather
    /// than the whole sprite sliding sideways.
    /// </summary>
    private void DrawTitlePerson(Rect dest, float time)
    {
        // Damped spring off the menu's own selection-change timestamp, signed by which way the cursor travelled.
        float since = (float)(GetTime() - AnimationStartedAt);
        float direction = SelectedIndex >= PreviousSelectedIndex ? 1f : -1f;
        float kick = MathF.Exp(-since * 7f) * MathF.Sin(since * 26f) * direction;
        float lean = MathF.Sin(time * 0.85f) * 1.4f + kick * 6f;
        float hop = MathF.Abs(kick) * 9f * Runtime.CurrentRuntime.ScaleF;
        Vector2 origin = new Vector2(dest.Width / 2f, dest.Height);
        Rect pivoted = dest with { X = dest.X + dest.Width / 2f, Y = dest.Y + dest.Height - hop };
        DrawTexturePro(SelectedPerson, RCPersonSource, pivoted, origin, lean, Rgba.White);
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
        var backdropRect = new Rect(0, 0, Runtime.CurrentRuntime.Width, Runtime.CurrentRuntime.Height);
        var backdropTint = Rgba.White with { A = Helper.TimeToTransparency(appear5) };
        // Both glow passes render into BackdropComposite at the backdrop art's own native size (1280x960),
        // exactly as they used to render straight onto the window — same tint, same math, same blending order —
        // just at a fixed pixel count instead of one that grows with the player's chosen resolution (up to
        // 1920x1440). The composite is then upscaled with a single plain, shader-free blit.
        var backdropNativeRect = new Rect(0, 0, BackdropTexture.Width, BackdropTexture.Height);
        BeginTextureMode(BackdropComposite);
        DrawGlowingBackdrop(BackdropTexture, backdropNativeRect, backdropTint);
        DrawGlowingWallpaper(BackdropTexture, backdropNativeRect, backdropTint);
        EndTextureMode();
        DrawTexturePro(BackdropComposite.Texture, Helper.GetFullSourceRenderTexture(BackdropComposite),
            backdropRect, Vector2.Zero, 0f, Rgba.White);
        var color1 = Helper.Mix(Rgba.Black, Rgba.Red, (float)appear2);
        var color2 = Helper.Mix(Rgba.Black, DarkRed, (float)appear2);
        CurrentX = (int)((16 - (Helper.Pow2F(1 - appear5) * 384)) * Runtime.CurrentRuntime.Scale);
#if DEBUG
        CurrentX = IsOnTop ? 0 : -10000;
#endif
        Helper.DrawWave(color1, MathF.Sin(time) + 1.5f, -0.7f - Helper.EaseInOutElasticF(appear1) * 1.5f, 1.5f, 1.5f, Runtime.CurrentRuntime.FullScreenRect);
        Helper.DrawWave(color2, MathF.Sin(time + 0.1f) + 1.5f, -0.7f - Helper.EaseInOutElasticF(appear3) * 1.5f, 1.5f, 1.5f, Runtime.CurrentRuntime.FullScreenRect);
        // The corner station logos glow and flicker through neon_sign, the same shader the title wears (they are
        // transparent sprites too, so the same alpha-keyed halo works on them unchanged).
        var logoTint = Rgba.White with { A = Helper.TimeToTransparency(appear25) };
        DrawNeonSign(SideLogoRight, SideLogoRightGlowBuffer, SideLogoRightTarget, Vector2.Zero, 0f, logoTint);
        DrawNeonSign(SideLogoLeft, SideLogoLeftGlowBuffer, SideLogoLeftTarget, Vector2.Zero, 0f, logoTint);
        // Floating pizzas drawn AFTER the top telecom logos so they pass in front of them (requested).
        DrawFloatingPizzas(time, (float)appear2, ActivationFlourish);
        // The menu list is drawn AFTER the pizzas so its entries stay readable ABOVE them (requested).
        DrawMenu();
        DrawTitlePerson(Helper.Mix(RCPersonTarget1, RCPersonTarget2, appear4), time);
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
        DrawNeonSign(LogoPadded, LogoGlowBuffer, paddedDest, logoOrigin, 45f, Rgba.White);
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

#if SWITCH
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using DmitryAndDemid.Utils;
using StbTrueTypeSharp;

namespace DmitryAndDemid.Rendering.Switch;

/// <summary>
/// SDL2 backend for the Nintendo Switch (mono-nx). This is the working-video path: SDL's 2D renderer draws
/// every sprite, render target, rectangle and glyph, so the game is visible and playable. The one thing SDL's
/// renderer cannot do is run the game's GLSL fragment shaders, so shader EFFECTS (glow, distortion, the screen
/// effects) are stubbed — <see cref="LoadShader"/> returns None and Begin/EndShader are no-ops, which the game
/// tolerates (a shader is an optional wrapper around a normal draw). A future SDL+GLES path can restore them.
///
/// Coordinate note: SDL render targets are top-down, but the game's targets follow OpenGL's bottom-up
/// convention and sample themselves flipped (negative-height source rects). <see cref="Blit"/> compensates by
/// toggling the vertical flip for target textures, so composited frames come out right-side up. See
/// docs/switch-port.md.
/// </summary>
public sealed unsafe class SdlBackend : IBackend
{
    public string Name => "SDL2 (Switch)";

    private IntPtr Window;
    private IntPtr Renderer;
    private IntPtr Controller;
    private readonly Stopwatch Clock = Stopwatch.StartNew();
    private bool quit;

    private int Width_ = 1280, Height_ = 720;
    private int NextId = 1;
    private int CurrentBlend = Sdl.BLENDMODE_BLEND;

    private sealed record SdlTexture(IntPtr Ptr, int W, int H, bool IsTarget);
    private readonly Dictionary<int, SdlTexture> Textures = new();
    private readonly Dictionary<int, IntPtr> Targets = new();          // target id -> SDL target texture
    private readonly Dictionary<int, TextureHandle> TargetTextures = new();
    private readonly Stack<IntPtr> TargetStack = new();               // bound render targets (IntPtr.Zero = window)
    private readonly Dictionary<int, SdlFont> Fonts = new();
    private TextureHandle WhitePixel;

    // =========================================================================================
    // IPlatform
    // =========================================================================================

    public void OpenWindow(int width, int height, string title)
    {
        // Step-by-step logging: a hang freezes the whole console, so the LAST line printed pinpoints the call.
        Console.WriteLine("[sdl] SDL_Init…");
        int initRc = Sdl.SDL_Init(Sdl.INIT_VIDEO | Sdl.INIT_AUDIO | Sdl.INIT_GAMECONTROLLER | Sdl.INIT_JOYSTICK);
        Console.WriteLine($"[sdl] SDL_Init rc={initRc}; IMG_Init…");
        Sdl.IMG_Init(2 /* IMG_INIT_PNG */);

        Console.WriteLine("[sdl] SDL_CreateWindow…");
        Window = Sdl.SDL_CreateWindow(title, Sdl.WINDOWPOS_CENTERED, Sdl.WINDOWPOS_CENTERED, width, height,
            Sdl.WINDOW_SHOWN);
        Console.WriteLine($"[sdl] window={Window != IntPtr.Zero} ({Marshal.PtrToStringUTF8(Sdl.SDL_GetError())}); SDL_CreateRenderer…");
        // No PRESENTVSYNC: a blocking vsync present is a classic hang on an emulator/unsynced display, and the
        // game paces itself. If the accelerated renderer itself hangs, we'll see it stop right after this line.
        Renderer = Sdl.SDL_CreateRenderer(Window, -1, Sdl.RENDERER_ACCELERATED);
        Console.WriteLine($"[sdl] renderer={Renderer != IntPtr.Zero} ({Marshal.PtrToStringUTF8(Sdl.SDL_GetError())})");
        Sdl.SDL_SetRenderDrawBlendMode(Renderer, Sdl.BLENDMODE_BLEND);
        Sdl.SDL_GetRendererOutputSize(Renderer, out Width_, out Height_);
        if (Width_ <= 0 || Height_ <= 0) { Width_ = width; Height_ = height; }
        Console.WriteLine($"[sdl] output={Width_}x{Height_}; init done");

        // A 1x1 white texture backs DrawRect / DrawLine (SDL_RenderFillRect can't rotate; a scaled quad can).
        WhitePixel = CreateSolidTexture(0xFFFFFFFF);

        if (Sdl.SDL_NumJoysticks() > 0 && Sdl.SDL_IsGameController(0) != 0)
            Controller = Sdl.SDL_GameControllerOpen(0);
    }

    public void CloseWindow()
    {
        if (Controller != IntPtr.Zero) { Sdl.SDL_GameControllerClose(Controller); Controller = IntPtr.Zero; }
        if (Renderer != IntPtr.Zero) { Sdl.SDL_DestroyRenderer(Renderer); Renderer = IntPtr.Zero; }
        if (Window != IntPtr.Zero) { Sdl.SDL_DestroyWindow(Window); Window = IntPtr.Zero; }
        Sdl.SDL_Quit();
    }

    public bool ShouldClose
    {
        get { PollEvents(); return quit; }
    }

    private void PollEvents()
    {
        while (Sdl.SDL_PollEvent(out SDL_Event ev) != 0)
            if (ev.type == Sdl.EVENT_QUIT)
                quit = true;
    }

    public void SetWindowIcon(string path) { }
    public void SetWindowSize(int width, int height) { }
    public void ApplyWindowMode(WindowMode mode, int windowedWidth, int windowedHeight) { }
    public WindowMode CurrentWindowMode => WindowMode.Exclusive;

    public int WindowWidth => Width_;
    public int WindowHeight => Height_;
    public int MonitorWidth => Width_;
    public int MonitorHeight => Height_;

    public void SetVSync(bool enabled) => Sdl.SDL_RenderSetVSync(Renderer, enabled ? 1 : 0);
    public void SetTargetFps(int fps) { }
    public void DisableExitKey() { }
    public double Time => Clock.Elapsed.TotalSeconds;

    private int fps, frameCount;
    private double lastReport;
    public int Fps => fps;
    public void DrawFpsCounter(int x, int y) { }

    // =========================================================================================
    // IInput
    // =========================================================================================

    public bool IsKeyDown(KeyCode key) => false;                 // no keyboard on Switch
    public bool IsMouseDown(MouseBtn button) => false;
    public Vector2 MousePosition => Vector2.Zero;
    public Vector2 MouseDelta => Vector2.Zero;
    public float MouseWheel => 0f;

    public int GamepadCount => Controller != IntPtr.Zero ? 1 : 0;
    public void RefreshGamepads()
    {
        PollEvents();
        // The controller may not be enumerated yet at OpenWindow; re-open it here once it appears.
        if (Controller == IntPtr.Zero && Sdl.SDL_NumJoysticks() > 0 && Sdl.SDL_IsGameController(0) != 0)
        {
            Controller = Sdl.SDL_GameControllerOpen(0);
            Console.WriteLine($"[sdl] gamecontroller opened: {Controller != IntPtr.Zero}");
        }
        Sdl.SDL_GameControllerUpdate();
    }

    public bool IsPadDown(PadButton button)
    {
        if (Controller == IntPtr.Zero) return false;
        // ZL/ZR are analog triggers on Switch, exposed as SDL axes rather than buttons.
        if (button == PadButton.LeftTrigger2)
            return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_TRIGGERLEFT) > 8000;
        if (button == PadButton.RightTrigger2)
            return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_TRIGGERRIGHT) > 8000;
        int b = SdlButton(button);
        return b >= 0 && Sdl.SDL_GameControllerGetButton(Controller, b) != 0;
    }

    public float GetPadAxis(PadAxis axis)
    {
        if (Controller == IntPtr.Zero) return 0f;
        switch (axis)
        {
            case PadAxis.LeftX:  return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_LEFTX) / 32767f;
            case PadAxis.LeftY:  return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_LEFTY) / 32767f;
            case PadAxis.RightX: return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_RIGHTX) / 32767f;
            case PadAxis.RightY: return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_RIGHTY) / 32767f;
            // Triggers rest at -1 like Raylib; SDL reports 0..32767.
            case PadAxis.LeftTrigger:  return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_TRIGGERLEFT) / 16383.5f - 1f;
            case PadAxis.RightTrigger: return Sdl.SDL_GameControllerGetAxis(Controller, Sdl.CONTROLLER_AXIS_TRIGGERRIGHT) / 16383.5f - 1f;
            default: return 0f;
        }
    }

    public PadButton? GetPressedPadButton()
    {
        if (Controller == IntPtr.Zero) return null;
        for (PadButton b = PadButton.LeftFaceUp; b <= PadButton.RightThumb; b++)
            if (IsPadDown(b)) return b;
        return null;
    }

    /// <summary>
    /// Positional map: the engine's <see cref="PadButton"/> follows Raylib's Xbox layout, and SDL's game
    /// controller abstraction is also positional (A = bottom, B = right, X = left, Y = top), so a slot maps
    /// straight across. Returns -1 for triggers (handled as axes) and unmapped slots.
    /// </summary>
    private static int SdlButton(PadButton b) => b switch
    {
        PadButton.LeftFaceUp     => Sdl.CONTROLLER_BUTTON_DPAD_UP,
        PadButton.LeftFaceRight  => Sdl.CONTROLLER_BUTTON_DPAD_RIGHT,
        PadButton.LeftFaceDown   => Sdl.CONTROLLER_BUTTON_DPAD_DOWN,
        PadButton.LeftFaceLeft   => Sdl.CONTROLLER_BUTTON_DPAD_LEFT,
        // Nintendo face-button LABELS, not Xbox positions — A<->B and X<->Y swapped vs desktop so the button
        // printed "A" confirms and "B" cancels on a Switch pad. See the matching note in SdlGlBackend.SdlButton.
        PadButton.RightFaceUp    => Sdl.CONTROLLER_BUTTON_X,
        PadButton.RightFaceRight => Sdl.CONTROLLER_BUTTON_A,
        PadButton.RightFaceDown  => Sdl.CONTROLLER_BUTTON_B,
        PadButton.RightFaceLeft  => Sdl.CONTROLLER_BUTTON_Y,
        PadButton.LeftTrigger1   => Sdl.CONTROLLER_BUTTON_LEFTSHOULDER,
        PadButton.RightTrigger1  => Sdl.CONTROLLER_BUTTON_RIGHTSHOULDER,
        PadButton.MiddleLeft     => Sdl.CONTROLLER_BUTTON_BACK,
        PadButton.Middle         => Sdl.CONTROLLER_BUTTON_GUIDE,
        PadButton.MiddleRight    => Sdl.CONTROLLER_BUTTON_START,
        PadButton.LeftThumb      => Sdl.CONTROLLER_BUTTON_LEFTSTICK,
        PadButton.RightThumb     => Sdl.CONTROLLER_BUTTON_RIGHTSTICK,
        _ => -1,
    };

    public int TouchCount => 0;
    public Vector2 GetTouchPosition(int index) => Vector2.Zero;

    // =========================================================================================
    // IAudio  (stubbed — video-first; SDL core audio comes in a follow-up)
    // =========================================================================================

    private float sfxVolume = 1f;
    // Audio is stubbed (silent) for now, but report SUCCESS: Runtime.Load treats a failed Initialize() as fatal
    // — it throws, sets ADPTriggered, prints "cannot initialize sound subsystem" and halts on the loading screen
    // (SwitchToMain never runs). Returning true lets the game proceed to the menu; sounds simply don't play.
    public bool Initialize() => true;
    public bool IsAvailable => true;
    public SoundHandle LoadSound(string path) => SoundHandle.None;
    public void UnloadSound(SoundHandle sound) { }
    public void Play(SoundHandle sound) { }
    public float SfxVolume { get => sfxVolume; set => sfxVolume = value; }

    // =========================================================================================
    // IRenderer — textures
    // =========================================================================================

    public TextureHandle LoadTexture(string path)
    {
        Console.WriteLine($"[sdl] loadtex {path}");   // logged BEFORE the call, so a hang names the culprit file
        IntPtr tex = Sdl.IMG_LoadTexture(Renderer, path);
        if (tex == IntPtr.Zero)
        {
            Console.WriteLine($"SDL: failed to load texture '{path}': {Marshal.PtrToStringUTF8(Sdl.SDL_GetError())}");
            return TextureHandle.None;
        }
        Sdl.SDL_SetTextureBlendMode(tex, Sdl.BLENDMODE_BLEND);
        Sdl.SDL_QueryTexture(tex, out _, out _, out int w, out int h);
        int id = NextId++;
        Textures[id] = new SdlTexture(tex, w, h, false);
        return new TextureHandle(id);
    }

    private TextureHandle CreateSolidTexture(uint rgba)
    {
        IntPtr tex = Sdl.SDL_CreateTexture(Renderer, Sdl.PIXELFORMAT_ABGR8888, Sdl.TEXTUREACCESS_STATIC, 1, 1);
        uint px = rgba;
        Sdl.SDL_UpdateTexture(tex, IntPtr.Zero, (IntPtr)(&px), 4);
        Sdl.SDL_SetTextureBlendMode(tex, Sdl.BLENDMODE_BLEND);
        int id = NextId++;
        Textures[id] = new SdlTexture(tex, 1, 1, false);
        return new TextureHandle(id);
    }

    public void UnloadTexture(TextureHandle texture)
    {
        if (Textures.Remove(texture.Id, out SdlTexture? t))
            Sdl.SDL_DestroyTexture(t.Ptr);
    }

    public bool IsValid(TextureHandle texture) => texture.Id != 0 && Textures.ContainsKey(texture.Id);

    public Vector2 GetTextureSize(TextureHandle texture) =>
        Textures.TryGetValue(texture.Id, out SdlTexture? t) ? new Vector2(t.W, t.H) : Vector2.Zero;

    public void SetTextureFilter(TextureHandle texture, FilterMode filter)
    {
        if (Textures.TryGetValue(texture.Id, out SdlTexture? t))
            Sdl.SDL_SetTextureScaleMode(t.Ptr, filter == FilterMode.Point ? Sdl.SCALEMODE_NEAREST : Sdl.SCALEMODE_LINEAR);
    }

    // =========================================================================================
    // IRenderer — render targets (they NEST)
    // =========================================================================================

    public TargetHandle CreateTarget(int width, int height)
    {
        IntPtr tex = Sdl.SDL_CreateTexture(Renderer, Sdl.PIXELFORMAT_ABGR8888, Sdl.TEXTUREACCESS_TARGET, width, height);
        Sdl.SDL_SetTextureBlendMode(tex, Sdl.BLENDMODE_BLEND);
        int texId = NextId++;
        Textures[texId] = new SdlTexture(tex, width, height, true);
        int targetId = NextId++;
        Targets[targetId] = tex;
        TargetTextures[targetId] = new TextureHandle(texId);
        return new TargetHandle(targetId);
    }

    public void DestroyTarget(TargetHandle target)
    {
        if (Targets.Remove(target.Id, out IntPtr tex))
        {
            if (TargetTextures.Remove(target.Id, out TextureHandle th))
                Textures.Remove(th.Id);
            Sdl.SDL_DestroyTexture(tex);
        }
    }

    public bool IsValid(TargetHandle target) => target.Id != 0 && Targets.ContainsKey(target.Id);
    public TextureHandle GetTargetTexture(TargetHandle target) =>
        TargetTextures.GetValueOrDefault(target.Id);

    public void BeginTarget(TargetHandle target)
    {
        IntPtr ptr = Targets.GetValueOrDefault(target.Id);
        TargetStack.Push(ptr);
        Sdl.SDL_SetRenderTarget(Renderer, ptr);
    }

    public void EndTarget()
    {
        if (TargetStack.Count <= TargetFloor)
            return;   // never pop below the frame's own target (see IRenderer.TargetFloor)
        TargetStack.Pop();
        Sdl.SDL_SetRenderTarget(Renderer, TargetStack.Count > 0 ? TargetStack.Peek() : IntPtr.Zero);
    }

    public int TargetFloor { get; set; }

    public void ResetTargets()
    {
        TargetStack.Clear();
        Sdl.SDL_SetRenderTarget(Renderer, IntPtr.Zero);
    }

    // =========================================================================================
    // IRenderer — shaders (stubbed; SDL_Renderer has no fragment-shader path)
    // =========================================================================================

    // SDL's renderer has no fragment-shader path, so shaders are DUMMIES: valid handles that do nothing. Handing
    // back a valid (non-zero) handle matters — Runtime.LoadShaders treats an Id==0 result as a compile failure
    // and halts on an error screen (ADPTriggered), which is what stopped the game at the loading screen. A draw
    // inside a shader scope simply renders without the effect.
    private readonly HashSet<int> ShaderIds = new();
    public ShaderHandle LoadShader(string? vertexPath, string fragmentPath) => NewDummyShader();
    public ShaderHandle LoadShaderFromSource(string? vertexSource, string fragmentSource) => NewDummyShader();
    private ShaderHandle NewDummyShader() { int id = NextId++; ShaderIds.Add(id); return new ShaderHandle(id); }
    public void UnloadShader(ShaderHandle shader) => ShaderIds.Remove(shader.Id);
    public bool IsValid(ShaderHandle shader) => ShaderIds.Contains(shader.Id);
    public void BeginShader(ShaderHandle shader) { }
    public void EndShader() { }
    public int GetUniformLocation(ShaderHandle shader, string name) => -1;
    public void SetUniform<T>(ShaderHandle shader, int location, T value, UniformType type) where T : unmanaged { }
    public void SetUniformTexture(ShaderHandle shader, int location, TextureHandle texture) { }
    public void SetUniformArray(ShaderHandle shader, int location, float[] values, UniformType type) { }
    public void SetUniform<T>(ShaderHandle shader, string name, T value, UniformType type) where T : unmanaged { }
    public IReadOnlyList<string> GetUniformNames(ShaderHandle shader) => Array.Empty<string>();

    // =========================================================================================
    // IRenderer — fonts (Stb rasterised into an SDL texture atlas; mirrors SilkGLBackend)
    // =========================================================================================

    private sealed class SdlFont
    {
        public int BaseSize;
        public TextureHandle Atlas;
        public readonly Dictionary<char, Glyph> Glyphs = new();
    }

    private struct Glyph { public int SrcX, SrcY, W, H; public float OffsetX, OffsetY, AdvanceX; }

    public FontHandle LoadFont(string path, int size)
    {
        byte[] ttf = Assets.ReadAllBytes(path);
        int cellSize = size + 2;
        int columns = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(95f)));
        int needed = columns * cellSize;
        int atlas = 256;
        while (atlas < needed) atlas *= 2;
        byte[] coverage = new byte[atlas * atlas];

        SdlFont font = new() { BaseSize = size };
        StbTrueType.stbtt_fontinfo info = new();
        fixed (byte* ttfPtr = ttf)
        {
            StbTrueType.stbtt_InitFont(info, ttfPtr, 0);
            float scale = StbTrueType.stbtt_ScaleForPixelHeight(info, size);
            int ascent, descent, lineGap;
            StbTrueType.stbtt_GetFontVMetrics(info, &ascent, &descent, &lineGap);

            int penX = 1, penY = 1, rowHeight = 0;
            for (char c = ' '; c < (char)127; c++)
            {
                int advance, leftBearing;
                StbTrueType.stbtt_GetCodepointHMetrics(info, c, &advance, &leftBearing);
                int x0, y0, x1, y1;
                StbTrueType.stbtt_GetCodepointBitmapBox(info, c, scale, scale, &x0, &y0, &x1, &y1);
                int w = x1 - x0, h = y1 - y0;
                if (penX + w + 1 >= atlas) { penX = 1; penY += rowHeight + 1; rowHeight = 0; }
                bool fits = w > 0 && h > 0 && penX + w <= atlas && penY + h <= atlas;
                if (fits)
                    fixed (byte* dst = coverage)
                        StbTrueType.stbtt_MakeCodepointBitmap(info, dst + penY * atlas + penX, w, h, atlas, scale, scale, c);
                else if (w > 0 && h > 0) { w = h = 0; }

                font.Glyphs[c] = new Glyph
                {
                    SrcX = penX, SrcY = penY, W = w, H = h,
                    OffsetX = x0, OffsetY = y0 + ascent * scale, AdvanceX = advance * scale,
                };
                penX += w + 1;
                rowHeight = Math.Max(rowHeight, h);
            }
        }

        byte[] rgba = new byte[atlas * atlas * 4];
        for (int i = 0; i < coverage.Length; i++)
        {
            rgba[i * 4 + 0] = 255; rgba[i * 4 + 1] = 255; rgba[i * 4 + 2] = 255; rgba[i * 4 + 3] = coverage[i];
        }
        font.Atlas = CreateTextureFromRgba(rgba, atlas, atlas);
        SetTextureFilter(font.Atlas, FilterMode.Bilinear);

        int id = NextId++;
        Fonts[id] = font;
        return new FontHandle(id);
    }

    private TextureHandle CreateTextureFromRgba(byte[] rgba, int w, int h)
    {
        IntPtr tex = Sdl.SDL_CreateTexture(Renderer, Sdl.PIXELFORMAT_ABGR8888, Sdl.TEXTUREACCESS_STATIC, w, h);
        fixed (byte* p = rgba)
            Sdl.SDL_UpdateTexture(tex, IntPtr.Zero, (IntPtr)p, w * 4);
        Sdl.SDL_SetTextureBlendMode(tex, Sdl.BLENDMODE_BLEND);
        int id = NextId++;
        Textures[id] = new SdlTexture(tex, w, h, false);
        return new TextureHandle(id);
    }

    public void UnloadFont(FontHandle font)
    {
        if (Fonts.Remove(font.Id, out SdlFont? f))
            UnloadTexture(f.Atlas);
    }

    private FontHandle DefaultFontHandle;
    public FontHandle GetDefaultFont()
    {
        if (DefaultFontHandle.IsValid) return DefaultFontHandle;
        string? any = Assets.DirectoryExists("Assets/Fonts") ? Assets.Files("Assets/Fonts").FirstOrDefault() : null;
        DefaultFontHandle = any != null ? LoadFont(any, 32) : FontHandle.None;
        return DefaultFontHandle;
    }

    public Vector2 MeasureText(FontHandle font, string text, float fontSize, float spacing)
    {
        if (!Fonts.TryGetValue(font.Id, out SdlFont? f) || string.IsNullOrEmpty(text))
            return Vector2.Zero;
        float scale = fontSize / f.BaseSize;
        float width = 0;
        foreach (char c in text)
            if (f.Glyphs.TryGetValue(c, out Glyph g))
                width += g.AdvanceX * scale + spacing;
        return new Vector2(width - spacing, fontSize);
    }

    public void DrawText(FontHandle font, string text, Vector2 position, float fontSize, float spacing, Rgba tint) =>
        DrawTextPro(font, text, position, Vector2.Zero, 0, fontSize, spacing, tint);

    public void DrawTextPro(FontHandle font, string text, Vector2 position, Vector2 origin, float rotation,
        float fontSize, float spacing, Rgba tint)
    {
        if (!Fonts.TryGetValue(font.Id, out SdlFont? f) || string.IsNullOrEmpty(text)) return;
        if (!Textures.TryGetValue(f.Atlas.Id, out SdlTexture? atlas)) return;
        // Rotation of a whole text run is uncommon in this game; draw glyphs axis-aligned from an origin-shifted
        // pen (rotation is applied to the pen offset only). Good enough for the HUD; refine if a rotated label needs it.
        float scale = fontSize / f.BaseSize;
        float penX = position.X - origin.X;
        float baseY = position.Y - origin.Y;
        foreach (char c in text)
        {
            if (!f.Glyphs.TryGetValue(c, out Glyph g)) continue;
            if (g.W > 0 && g.H > 0)
            {
                var src = new Rect(g.SrcX, g.SrcY, g.W, g.H);
                var dst = new Rect(penX + g.OffsetX * scale, baseY + g.OffsetY * scale, g.W * scale, g.H * scale);
                Blit(atlas, src, dst, Vector2.Zero, 0, tint);
            }
            penX += g.AdvanceX * scale + spacing;
        }
    }

    // =========================================================================================
    // IRenderer — drawing
    // =========================================================================================

    public void Clear(Rgba color)
    {
        Sdl.SDL_SetRenderDrawColor(Renderer, color.R, color.G, color.B, color.A);
        Sdl.SDL_RenderClear(Renderer);
    }

    public void DrawTexture(TextureHandle texture, Vector2 position, Rgba tint) =>
        DrawTexture(texture, position, 0, 1, tint);

    public void DrawTexture(TextureHandle texture, Vector2 position, float rotation, float scale, Rgba tint)
    {
        Vector2 size = GetTextureSize(texture);
        DrawTexture(texture, new Rect(0, 0, size.X, size.Y),
            new Rect(position.X, position.Y, size.X * scale, size.Y * scale), Vector2.Zero, rotation, tint);
    }

    public void DrawTexture(TextureHandle texture, Rect source, Rect destination, Vector2 origin, float rotation, Rgba tint)
    {
        if (Textures.TryGetValue(texture.Id, out SdlTexture? t))
            Blit(t, source, destination, origin, rotation, tint);
    }

    public void DrawNinePatch(TextureHandle texture, NinePatch patch, Rect destination, Vector2 origin, float rotation, Rgba tint) =>
        DrawTexture(texture, patch.Source, destination, origin, rotation, tint);   // straight stretch (as SilkGL)

    public void DrawRect(Rect rect, Rgba color) => DrawRect(rect, Vector2.Zero, 0, color);

    public void DrawRect(Rect rect, Vector2 origin, float rotation, Rgba color)
    {
        if (Textures.TryGetValue(WhitePixel.Id, out SdlTexture? white))
            Blit(white, new Rect(0, 0, 1, 1), rect, origin, rotation, color);
    }

    public void DrawLine(Vector2 from, Vector2 to, Rgba color)
    {
        Vector2 d = to - from;
        float len = d.Length();
        if (len < 0.0001f) return;
        float angle = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;
        DrawRect(new Rect(from.X, from.Y, len, 1), Vector2.Zero, angle, color);
    }

    /// <summary>The single funnel every textured draw goes through. Applies tint via colour/alpha mod, maps the
    /// game's negative-height (flip) source rects to SDL flip flags, and compensates for SDL's top-down targets.</summary>
    private void Blit(SdlTexture t, Rect source, Rect dest, Vector2 origin, float rotation, Rgba tint)
    {
        Sdl.SDL_SetTextureColorMod(t.Ptr, tint.R, tint.G, tint.B);
        Sdl.SDL_SetTextureAlphaMod(t.Ptr, tint.A);
        Sdl.SDL_SetTextureBlendMode(t.Ptr, CurrentBlend);

        var src = new SDL_Rect
        {
            x = (int)MathF.Round(source.X), y = (int)MathF.Round(source.Y),
            w = (int)MathF.Round(source.Width), h = (int)MathF.Round(source.Height),
        };
        int flip = Sdl.FLIP_NONE;
        if (src.w < 0) { src.x += src.w; src.w = -src.w; flip |= Sdl.FLIP_HORIZONTAL; }
        if (src.h < 0) { src.y += src.h; src.h = -src.h; flip |= Sdl.FLIP_VERTICAL; }
        // Game targets are bottom-up and sampled flipped; SDL targets are top-down — undo the extra flip.
        if (t.IsTarget) flip ^= Sdl.FLIP_VERTICAL;

        var dst = new SDL_FRect { x = dest.X - origin.X, y = dest.Y - origin.Y, w = dest.Width, h = dest.Height };
        var center = new SDL_FPoint { x = origin.X, y = origin.Y };
        Sdl.SDL_RenderCopyExF(Renderer, t.Ptr, ref src, ref dst, rotation, ref center, flip);
    }

    public void BeginBlend(BlendMode mode) => CurrentBlend = mode switch
    {
        BlendMode.Additive => Sdl.BLENDMODE_ADD,
        BlendMode.Multiplied => Sdl.BLENDMODE_MOD,
        BlendMode.CopyRgb => Sdl.BLENDMODE_NONE,
        _ => Sdl.BLENDMODE_BLEND,
    };

    public void EndBlend() => CurrentBlend = Sdl.BLENDMODE_BLEND;

    // =========================================================================================
    // IRenderer — frame
    // =========================================================================================

    private int frameLog;
    public void BeginFrame()
    {
        // A hang freezes the console, so bracket the first frames: "begin N" with no matching "end N" means the
        // freeze is inside frame N's draw calls (a hanging SDL op), not in game logic between frames.
        if (frameLog < 20) Console.WriteLine($"[sdl] begin frame {frameLog}");
        ResetTargets();
        Sdl.SDL_SetRenderDrawColor(Renderer, 0, 0, 0, 255);
        Sdl.SDL_RenderClear(Renderer);
    }

    public void EndFrame()
    {
        if (frameLog < 20) Console.WriteLine($"[sdl] end frame {frameLog}");
        frameLog++;
        Sdl.SDL_RenderPresent(Renderer);
        frameCount++;
        double now = Clock.Elapsed.TotalSeconds;
        if (now - lastReport >= 1.0)
        {
            fps = (int)(frameCount / (now - lastReport));
            Console.WriteLine($"[sdl] loop alive: {fps} fps");
            frameCount = 0;
            lastReport = now;
        }
    }

    // =========================================================================================
    // IBackend — debug UI (not supported on Switch)
    // =========================================================================================

    public bool SupportsDebugUi => false;
    public void SetupDebugUi() { }
    public void BeginDebugUi() { }
    public void EndDebugUi() { }
    public void DebugUiImage(TextureHandle texture) { }
    public void DebugUiImage(TargetHandle target) { }

    public void Dispose() => CloseWindow();
}
#endif

#if METAL
using System.Numerics;
using System.Text.Json;
using CoreAnimation;
using Foundation;
using Metal;
using ObjCRuntime;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Rendering.Metal;

/// <summary>
/// Native Apple Metal backend (macOS / iOS). See <c>docs/metal-backend.md</c>.
///
/// <para><b>Phase-1 scaffold.</b> Like the Switch <c>Deko3dBackend</c>, this establishes the seam and the
/// plumbing that can be reasoned about off-device — Metal object setup, the frame/command-buffer lifecycle,
/// textures, render targets, the shader-pipeline cache and the uniform-block bookkeeping — and brings the draw
/// path up incrementally (clear -> quads -> textures -> shaders). It has <b>never been compiled</b>: the
/// <c>Metal</c>/<c>CoreAnimation</c> bindings only exist under a <c>net10.0-ios</c>/<c>-macos</c> TFM, not the
/// desktop build, so the whole file is under <c>#if METAL</c> and dead everywhere it can currently be built.
/// A first Mac compile pass will need to reconcile exact binding signatures (marked <c>TODO(metal)</c>).</para>
///
/// <para><b>Shaders.</b> No GLSL or SPIR-V at runtime: <c>Tools/compile_metal_shaders.py</c> translates the
/// committed Vulkan SPIR-V to MSL and copies the same reflection sidecar. So this backend loads
/// <c>Assets/Shaders/metal/&lt;name&gt;.frag.metal</c> (+ <c>.json</c>) and reuses the identical uniform byte
/// offsets the Vulkan backend uses — a uniform written at offset N lands in the MSL <c>constant</c> struct at
/// N. The vertex/fragment entry point is <c>main0</c> (SPIRV-Cross renames <c>main</c>).</para>
///
/// <para><b>Host-driven.</b> On iOS/macOS the window, run-loop and input belong to the UIKit/AppKit host (a
/// view controller with a <c>CAMetalLayer</c> stepped by a <c>CADisplayLink</c>). So <see cref="OpenWindow"/>
/// is not used; the host calls <see cref="StartMetal"/> with the layer, feeds touches/keys, and drives frames.
/// Audio is injected (the host supplies an <see cref="IAudio"/>), mirroring how the Android host injects one.</para>
/// </summary>
public sealed class MetalBackend : IBackend
{
    // ---- Metal objects ---------------------------------------------------------------------
    private IMTLDevice Device = null!;
    private IMTLCommandQueue Queue = null!;
    private CAMetalLayer Layer = null!;
    private const MTLPixelFormat SurfaceFormat = MTLPixelFormat.BGRA8Unorm;

    // Per-frame, valid only between BeginFrame and EndFrame.
    private ICAMetalDrawable? Drawable;
    private IMTLCommandBuffer? CommandBuffer;
    private IMTLRenderCommandEncoder? Encoder;

    // ---- injected + host-fed state ---------------------------------------------------------
    // Audio is supplied by the host through StartMetal (as the Android host hands AndroidAudio to StartAndroid),
    // NOT the constructor — so the backend stays constructible parameterlessly via Engine.Create.
    private IAudio Audio = null!;
    private int PixelWidth, PixelHeight;
    private readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly HashSet<KeyCode> KeysDown = new();
    private readonly List<Vector2> Touches = new();

    public string Name => "Metal";
    public bool SupportsDebugUi => false;   // no rlImGui under Metal; DEBUG editor overlays are skipped

    /// <summary>
    /// Host attach point (analogue of Runtime.StartAndroid). <paramref name="metalLayer"/> is a native pointer
    /// to the view's CAMetalLayer; the host also reports the drawable pixel size and pumps frames itself.
    /// </summary>
    public void StartMetal(nint metalLayer, int width, int height, IAudio audio)
    {
        Audio = audio;
        Device = MTLDevice.SystemDefault ?? throw new InvalidOperationException("Metal: no default device");
        Queue = Device.CreateCommandQueue() ?? throw new InvalidOperationException("Metal: no command queue");

        Layer = Runtime.GetNSObject<CAMetalLayer>(metalLayer)
                ?? throw new InvalidOperationException("Metal: layer pointer is not a CAMetalLayer");
        Layer.Device = Device;
        Layer.PixelFormat = SurfaceFormat;
        Layer.FramebufferOnly = true;

        PixelWidth = width;
        PixelHeight = height;
        Audio.Initialize();
    }

    /// <summary>Host calls this from viewDidLayoutSubviews / on rotation; the swapchain is the layer, so we
    /// only need the new drawable size.</summary>
    public void Resize(int width, int height)
    {
        PixelWidth = width;
        PixelHeight = height;
        // TODO(metal): Layer.DrawableSize is set by the host's view controller; nothing to recreate here since
        // CAMetalLayer manages its own drawable pool. Render-target textures sized to the window are rebuilt by
        // the game when Runtime.Scale changes.
    }

    // ========================================================================================
    //  frame lifecycle
    // ========================================================================================

    public void BeginFrame()
    {
        Drawable = Layer.NextDrawable();
        CommandBuffer = Queue.CommandBuffer();
        if (Drawable == null || CommandBuffer == null)
            return;   // drawable can be null under memory pressure / when occluded — skip the frame cleanly

        // The frame composites into the drawable. The game clears explicitly (Clear()), so LoadAction here is
        // DontCare; the first Clear of the frame sets the real background.
        var pass = new MTLRenderPassDescriptor();
        pass.ColorAttachments[0].Texture = Drawable.Texture;
        pass.ColorAttachments[0].LoadAction = MTLLoadAction.DontCare;
        pass.ColorAttachments[0].StoreAction = MTLStoreAction.Store;
        Encoder = CommandBuffer.CreateRenderCommandEncoder(pass);
        TargetStack.Clear();
    }

    public void EndFrame()
    {
        Encoder?.EndEncoding();
        Encoder = null;
        if (CommandBuffer != null && Drawable != null)
        {
            CommandBuffer.PresentDrawable(Drawable);
            CommandBuffer.Commit();
        }
        Drawable = null;
        CommandBuffer = null;
    }

    public void Clear(Rgba color)
    {
        // Metal clears at render-pass start, not mid-pass. To clear the current target we re-open its pass with
        // LoadAction.Clear. For the drawable that means restarting the encoder with a clear load.
        Encoder?.EndEncoding();
        var pass = new MTLRenderPassDescriptor();
        pass.ColorAttachments[0].Texture = CurrentColorTexture();
        pass.ColorAttachments[0].LoadAction = MTLLoadAction.Clear;
        pass.ColorAttachments[0].ClearColor = new MTLClearColor(color.R / 255.0, color.G / 255.0,
            color.B / 255.0, color.A / 255.0);
        pass.ColorAttachments[0].StoreAction = MTLStoreAction.Store;
        Encoder = CommandBuffer?.CreateRenderCommandEncoder(pass);
    }

    private IMTLTexture? CurrentColorTexture() =>
        TargetStack.Count > 0 ? Targets[TargetStack.Peek()].Texture : Drawable?.Texture;

    // ========================================================================================
    //  textures
    // ========================================================================================

    private sealed class MtlTexture
    {
        public IMTLTexture Texture = null!;
        public int Width, Height;
        public bool Linear = true;
    }

    private readonly Dictionary<int, MtlTexture> Textures = new();
    private int NextId = 1;

    public TextureHandle LoadTexture(string path)
    {
        // Decode with StbImageSharp (same as the Silk backend), then upload. Assets.OpenRead goes through the
        // asset seam so the app-bundle path resolves.
        using Stream s = Assets.OpenRead(path);
        var image = StbImageSharp.ImageResult.FromStream(s, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        return CreateTexture(image.Width, image.Height, image.Data);
    }

    private TextureHandle CreateTexture(int width, int height, byte[] rgba)
    {
        var desc = MTLTextureDescriptor.CreateTexture2DDescriptor(MTLPixelFormat.RGBA8Unorm,
            (nuint)width, (nuint)height, false);
        desc.Usage = MTLTextureUsage.ShaderRead;
        IMTLTexture tex = Device.CreateTexture(desc);
        unsafe
        {
            fixed (byte* p = rgba)
                tex.ReplaceRegion(MTLRegion.Create2D(0, 0, (nuint)width, (nuint)height), 0,
                    (nint)p, (nuint)(width * 4));
        }
        int id = NextId++;
        Textures[id] = new MtlTexture { Texture = tex, Width = width, Height = height };
        return new TextureHandle(id);
    }

    public void UnloadTexture(TextureHandle texture)
    {
        if (Textures.Remove(texture.Id, out MtlTexture? t))
            t.Texture.Dispose();
    }

    public bool IsValid(TextureHandle texture) => Textures.ContainsKey(texture.Id);

    public Vector2 GetTextureSize(TextureHandle texture) =>
        Textures.TryGetValue(texture.Id, out MtlTexture? t) ? new Vector2(t.Width, t.Height) : Vector2.Zero;

    public void SetTextureFilter(TextureHandle texture, FilterMode filter)
    {
        if (Textures.TryGetValue(texture.Id, out MtlTexture? t))
            t.Linear = filter != FilterMode.Point;   // realised as a sampler-state choice at draw time
    }

    // ========================================================================================
    //  render targets  (offscreen textures; nested, bottom-up like the GL/Vulkan targets)
    // ========================================================================================

    private sealed class MtlTarget
    {
        public IMTLTexture Texture = null!;
        public int Width, Height;
    }

    private readonly Dictionary<int, MtlTarget> Targets = new();
    private readonly Stack<int> TargetStack = new();
    public int TargetFloor { get; set; }

    public TargetHandle CreateTarget(int width, int height)
    {
        var desc = MTLTextureDescriptor.CreateTexture2DDescriptor(SurfaceFormat, (nuint)width, (nuint)height, false);
        desc.Usage = MTLTextureUsage.ShaderRead | MTLTextureUsage.RenderTarget;
        IMTLTexture tex = Device.CreateTexture(desc);
        int id = NextId++;
        Targets[id] = new MtlTarget { Texture = tex, Width = width, Height = height };
        return new TargetHandle(id);
    }

    public void DestroyTarget(TargetHandle target)
    {
        if (Targets.Remove(target.Id, out MtlTarget? t))
            t.Texture.Dispose();
    }

    public bool IsValid(TargetHandle target) => Targets.ContainsKey(target.Id);

    public TextureHandle GetTargetTexture(TargetHandle target)
    {
        // Surface the target's colour texture as a sampleable texture handle. Register it in the texture map
        // (idempotently) so DrawTexture can bind it.
        if (!Targets.TryGetValue(target.Id, out MtlTarget? t))
            return TextureHandle.None;
        int id = NextId++;
        Textures[id] = new MtlTexture { Texture = t.Texture, Width = t.Width, Height = t.Height };
        return new TextureHandle(id);
    }

    public void BeginTarget(TargetHandle target)
    {
        if (!Targets.TryGetValue(target.Id, out MtlTarget? t))
            return;
        Encoder?.EndEncoding();
        TargetStack.Push(target.Id);
        var pass = new MTLRenderPassDescriptor();
        pass.ColorAttachments[0].Texture = t.Texture;
        pass.ColorAttachments[0].LoadAction = MTLLoadAction.Load;   // targets accumulate across draws
        pass.ColorAttachments[0].StoreAction = MTLStoreAction.Store;
        Encoder = CommandBuffer?.CreateRenderCommandEncoder(pass);
    }

    public void EndTarget()
    {
        if (TargetStack.Count <= TargetFloor)
            return;   // guard: never pop below the frame floor (matches the other backends)
        Encoder?.EndEncoding();
        TargetStack.Pop();
        // Re-open the enclosing target (or the drawable) with a Load so its prior contents survive.
        var pass = new MTLRenderPassDescriptor();
        pass.ColorAttachments[0].Texture = CurrentColorTexture();
        pass.ColorAttachments[0].LoadAction = MTLLoadAction.Load;
        pass.ColorAttachments[0].StoreAction = MTLStoreAction.Store;
        Encoder = CommandBuffer?.CreateRenderCommandEncoder(pass);
    }

    public void ResetTargets()
    {
        while (TargetStack.Count > 0) TargetStack.Pop();
    }

    // ========================================================================================
    //  shaders + uniform blocks   (MSL library + reused Vulkan reflection sidecar)
    // ========================================================================================

    private sealed record Reflected(string name, int offset, string type);
    private sealed record ReflectedSampler(string name, int binding);
    private sealed class Reflection
    {
        public int blockSize { get; set; }
        public int blockBinding { get; set; } = -1;
        public Reflected[] uniforms { get; set; } = Array.Empty<Reflected>();
        public ReflectedSampler[] samplers { get; set; } = Array.Empty<ReflectedSampler>();
    }

    private sealed class MtlShader
    {
        public IMTLFunction Vertex = null!;
        public IMTLFunction Fragment = null!;
        public Reflection FragmentReflection = new();
        public byte[] FragmentBlock = Array.Empty<byte>();
        public int ColDiffuseOffset = -1;
        public string[] UniformNames = Array.Empty<string>();
        // One pipeline state per (blend mode, colour format) combination, built on demand.
        public readonly Dictionary<(BlendMode, MTLPixelFormat), IMTLRenderPipelineState> Pipelines = new();
    }

    private readonly Dictionary<int, MtlShader> Shaders = new();
    private ShaderHandle DefaultShader;

    public ShaderHandle LoadShader(string? vertexPath, string fragmentPath)
    {
        string frag = Path.GetFileNameWithoutExtension(fragmentPath);
        string vert = vertexPath != null ? Path.GetFileNameWithoutExtension(vertexPath) : "base";
        return LoadMsl(vert, frag);
    }

    public ShaderHandle LoadShaderFromSource(string? vertexSource, string fragmentSource) =>
        // Runtime GLSL isn't supported (no cross-compiler on device); the text shaders also exist on disk, so
        // fall back to the default textured-quad pipeline rather than failing the draw. Mirrors the Vulkan path.
        DefaultShader;

    private ShaderHandle LoadMsl(string vertexName, string fragmentName)
    {
        string dir = "Assets/Shaders/metal";
        string vertMetal = $"{dir}/{vertexName}.vert.metal";
        string fragMetal = $"{dir}/{fragmentName}.frag.metal";
        if (!Assets.Exists(vertMetal) || !Assets.Exists(fragMetal))
        {
            Console.WriteLine($"Metal: no MSL for {vertexName}/{fragmentName} — run Tools/compile_metal_shaders.py");
            return ShaderHandle.None;
        }

        var shader = new MtlShader
        {
            Vertex = CompileFunction(vertMetal),
            Fragment = CompileFunction(fragMetal),
            FragmentReflection = ReadReflection($"{dir}/{fragmentName}.frag.json"),
        };
        shader.FragmentBlock = new byte[Math.Max(16, shader.FragmentReflection.blockSize)];
        shader.ColDiffuseOffset = Array.Find(shader.FragmentReflection.uniforms, u => u.name == "colDiffuse")?.offset ?? -1;
        shader.UniformNames = Array.ConvertAll(shader.FragmentReflection.uniforms, u => u.name);
        // colDiffuse defaults to white — every shader multiplies by it, and a zeroed block would paint black
        // (the exact trap the Vulkan backend documents).
        WriteColDiffuseWhite(shader);

        int id = NextId++;
        Shaders[id] = shader;
        return new ShaderHandle(id);
    }

    private IMTLFunction CompileFunction(string metalPath)
    {
        // Prefer a precompiled .metallib (produced on macOS by the tool); else compile the .metal source at
        // runtime. Both resolve the SPIRV-Cross entry point name "main0".
        string libPath = Path.ChangeExtension(metalPath, ".metallib");
        IMTLLibrary lib;
        if (Assets.Exists(libPath))
        {
            // TODO(metal): construct a DispatchData from Assets.ReadAllBytes(libPath) and call
            // Device.CreateLibrary(data, out err). Kept as source-compile below until confirmed on-device.
            lib = Device.CreateLibrary(Assets.ReadAllText(metalPath), new MTLCompileOptions(), out NSError err1);
        }
        else
        {
            lib = Device.CreateLibrary(Assets.ReadAllText(metalPath), new MTLCompileOptions(), out NSError err2);
        }
        return lib.CreateFunction("main0") ?? throw new InvalidOperationException($"Metal: no main0 in {metalPath}");
    }

    private static Reflection ReadReflection(string path) =>
        Assets.Exists(path)
            ? JsonSerializer.Deserialize<Reflection>(Assets.ReadAllText(path)) ?? new Reflection()
            : new Reflection();

    private void WriteColDiffuseWhite(MtlShader shader)
    {
        if (shader.ColDiffuseOffset < 0) return;
        for (int i = 0; i < 4; i++)
            BitConverter.GetBytes(1f).CopyTo(shader.FragmentBlock, shader.ColDiffuseOffset + i * 4);
    }

    public void UnloadShader(ShaderHandle shader)
    {
        if (Shaders.Remove(shader.Id, out MtlShader? s))
            foreach (var p in s.Pipelines.Values) p.Dispose();
    }

    public bool IsValid(ShaderHandle shader) => Shaders.ContainsKey(shader.Id);

    private int ActiveShaderId = -1;

    public void BeginShader(ShaderHandle shader) => ActiveShaderId = shader.Id;
    public void EndShader() => ActiveShaderId = -1;

    // Uniform "location" is just the byte offset into the block; -1 means "not present".
    public int GetUniformLocation(ShaderHandle shader, string name)
    {
        if (!Shaders.TryGetValue(shader.Id, out MtlShader? s)) return -1;
        Reflected? u = Array.Find(s.FragmentReflection.uniforms, x => x.name == name);
        return u?.offset ?? -1;
    }

    public void SetUniform<T>(ShaderHandle shader, int location, T value, UniformType type) where T : unmanaged
    {
        if (location < 0 || !Shaders.TryGetValue(shader.Id, out MtlShader? s)) return;
        WriteBlock(s.FragmentBlock, location, value);
    }

    public void SetUniform<T>(ShaderHandle shader, string name, T value, UniformType type) where T : unmanaged =>
        SetUniform(shader, GetUniformLocation(shader, name), value, type);

    public void SetUniformArray(ShaderHandle shader, int location, float[] values, UniformType type)
    {
        if (location < 0 || !Shaders.TryGetValue(shader.Id, out MtlShader? s)) return;
        for (int i = 0; i < values.Length; i++)
            BitConverter.GetBytes(values[i]).CopyTo(s.FragmentBlock, location + i * 4);
    }

    public void SetUniformTexture(ShaderHandle shader, int location, TextureHandle texture)
    {
        // Extra sampler beyond texture0 (e.g. a mask). Recorded for the next draw's encoder.BindTexture.
        if (Textures.TryGetValue(texture.Id, out MtlTexture? t))
            ExtraSamplers[location] = t.Texture;
    }

    private readonly Dictionary<int, IMTLTexture> ExtraSamplers = new();

    private static unsafe void WriteBlock<T>(byte[] block, int offset, T value) where T : unmanaged
    {
        int size = sizeof(T);
        if (offset < 0 || offset + size > block.Length) return;
        fixed (byte* p = &block[offset])
            *(T*)p = value;
    }

    public IReadOnlyList<string> GetUniformNames(ShaderHandle shader) =>
        Shaders.TryGetValue(shader.Id, out MtlShader? s) ? s.UniformNames : Array.Empty<string>();

    // ========================================================================================
    //  drawing   (immediate-mode quads/lines into a dynamic vertex buffer)
    // ========================================================================================
    //
    // TODO(metal): the draw calls below are the incremental bring-up target (Deko3d-style). Each Draw* builds
    // textured/coloured vertices, selects/creates the pipeline state for (ActiveShader|default, CurrentBlend,
    // target format), binds texture0 (+ ExtraSamplers) and the fragment uniform block, and issues a
    // DrawPrimitives triangle-strip on the current Encoder. Left structured but not yet emitting geometry so
    // the loop can run (clear works) before the vertex path is wired and validated on a device.

    public void DrawTexture(TextureHandle texture, Vector2 position, Rgba tint) =>
        DrawTexture(texture, position, 0, 1, tint);

    public void DrawTexture(TextureHandle texture, Vector2 position, float rotation, float scale, Rgba tint)
    {
        if (!Textures.TryGetValue(texture.Id, out MtlTexture? t)) return;
        DrawTexture(texture, new Rect(0, 0, t.Width, t.Height),
            new Rect(position.X, position.Y, t.Width * scale, t.Height * scale), Vector2.Zero, rotation, tint);
    }

    public void DrawTexture(TextureHandle texture, Rect source, Rect destination, Vector2 origin, float rotation, Rgba tint)
    {
        // TODO(metal): emit a textured quad (source→dest, rotated about origin) and issue the draw.
    }

    public void DrawNinePatch(TextureHandle texture, NinePatch patch, Rect destination, Vector2 origin, float rotation, Rgba tint)
    {
        // TODO(metal): nine textured quads (corners fixed, edges/centre stretched). Rare in this game.
    }

    public void DrawRect(Rect rect, Rgba color) => DrawRect(rect, Vector2.Zero, 0, color);

    public void DrawRect(Rect rect, Vector2 origin, float rotation, Rgba color)
    {
        // TODO(metal): untextured quad through the default pipeline (texture0 = 1x1 white).
    }

    public void DrawLine(Vector2 from, Vector2 to, Rgba color)
    {
        // TODO(metal): a thin quad along from→to, or a Line primitive.
    }

    public void BeginBlend(BlendMode mode) => CurrentBlend = mode;
    public void EndBlend() => CurrentBlend = BlendMode.Alpha;
    private BlendMode CurrentBlend = BlendMode.Alpha;

    // Blend factor table, applied to a pipeline's colour attachment when it is (lazily) built.
    // TODO(metal): map each BlendMode to (SourceRgb, DestRgb) using MTLBlendFactor when constructing the
    // MTLRenderPipelineColorAttachmentDescriptor. Alpha: SourceAlpha / OneMinusSourceAlpha. Additive:
    // SourceAlpha / One. Multiplied/Premultiplied as the GL backend defines them.

    // ========================================================================================
    //  fonts   (StbTrueType atlas -> textured glyph quads, same approach as the Silk/SDL backends)
    // ========================================================================================

    public FontHandle LoadFont(string path, int size)
    {
        // TODO(metal): bake a glyph atlas with StbTrueTypeSharp (as Rendering/Switch/SdlBackend does), upload
        // it as an MtlTexture, and keep per-glyph metrics + UVs keyed by this FontHandle.
        int id = NextId++;
        return new FontHandle(id);
    }

    public void UnloadFont(FontHandle font) { /* TODO(metal): drop the atlas texture + metrics */ }
    public FontHandle GetDefaultFont() => default;

    public Vector2 MeasureText(FontHandle font, string text, float fontSize, float spacing)
    {
        // TODO(metal): sum glyph advances from the baked metrics. Returns zero until fonts are wired.
        return Vector2.Zero;
    }

    public void DrawText(FontHandle font, string text, Vector2 position, float fontSize, float spacing, Rgba tint)
    {
        // TODO(metal): one textured quad per glyph from the atlas.
    }

    public void DrawTextPro(FontHandle font, string text, Vector2 position, Vector2 origin, float rotation,
        float fontSize, float spacing, Rgba tint)
    {
        // TODO(metal): as DrawText, rotated about origin.
    }

    // ========================================================================================
    //  diagnostics
    // ========================================================================================

    public GpuInfo? QueryGpuInfo()
    {
        try
        {
            string name = Device?.Name ?? "";
            if (string.IsNullOrWhiteSpace(name)) return null;
            long vram = (long)(Device?.RecommendedMaxWorkingSetSize ?? 0);   // best proxy Metal exposes
            return new GpuInfo(name, "Metal", vram, Array.Empty<string>());
        }
        catch { return null; }
    }

    // ========================================================================================
    //  IPlatform  (host-owned: most of these are no-ops or read the layer)
    // ========================================================================================

    public void OpenWindow(int width, int height, string title) =>
        throw new NotSupportedException("Metal is host-driven; call StartMetal(layer, w, h) from the view controller.");

    public void CloseWindow() { /* the OS owns the app lifecycle */ }
    public bool ShouldClose => false;
    public void SetWindowIcon(string path) { /* set in the app bundle, not at runtime */ }
    public void SetWindowSize(int width, int height) => Resize(width, height);
    public void ApplyWindowMode(WindowMode mode, int windowedWidth, int windowedHeight) { /* always fullscreen */ }
    public WindowMode CurrentWindowMode => WindowMode.Fullscreen;
    public int WindowWidth => PixelWidth;
    public int WindowHeight => PixelHeight;
    public int MonitorWidth => PixelWidth;
    public int MonitorHeight => PixelHeight;
    public void SetVSync(bool enabled) { /* CAMetalLayer presents on the display refresh */ }
    public void SetTargetFps(int fps) { /* the CADisplayLink cadence is host-set */ }
    public void DisableExitKey() { }
    public double Time => Clock.Elapsed.TotalSeconds;
    public int Fps { get; private set; }
    public void DrawFpsCounter(int x, int y) { /* the game draws its own */ }

    // ========================================================================================
    //  IInput  (host feeds touches/keys; no mouse/pad on iOS, some on macOS)
    // ========================================================================================

    public void SetTouches(IReadOnlyList<Vector2> points)
    {
        Touches.Clear();
        Touches.AddRange(points);
    }

    public void SetKeyState(KeyCode key, bool down)
    {
        if (down) KeysDown.Add(key); else KeysDown.Remove(key);
    }

    public bool IsKeyDown(KeyCode key) => KeysDown.Contains(key);
    public bool IsMouseDown(MouseBtn button) => false;
    public Vector2 MousePosition => Touches.Count > 0 ? Touches[0] : Vector2.Zero;
    public Vector2 MouseDelta => Vector2.Zero;
    public float MouseWheel => 0;
    public int GamepadCount => 0;
    public bool IsPadDown(PadButton button) => false;
    public float GetPadAxis(PadAxis axis) => 0;
    public PadButton? GetPressedPadButton() => null;
    public void RefreshGamepads() { }
    public int TouchCount => Touches.Count;
    public Vector2 GetTouchPosition(int index) => index < Touches.Count ? Touches[index] : Vector2.Zero;

    // ========================================================================================
    //  IAudio  (delegated to the injected implementation — e.g. an AVAudioEngine host audio)
    // ========================================================================================

    public bool Initialize() => Audio.Initialize();
    public bool IsAvailable => Audio.IsAvailable;
    public SoundHandle LoadSound(string path) => Audio.LoadSound(path);
    public void UnloadSound(SoundHandle sound) => Audio.UnloadSound(sound);
    public void Play(SoundHandle sound) => Audio.Play(sound);
    public float SfxVolume { get => Audio.SfxVolume; set => Audio.SfxVolume = value; }

    // ========================================================================================
    //  debug UI  (unsupported under Metal — no rlImGui)
    // ========================================================================================

    public void SetupDebugUi() { }
    public void BeginDebugUi() { }
    public void EndDebugUi() { }
    public void DebugUiImage(TextureHandle texture) { }
    public void DebugUiImage(TargetHandle target) { }

    public void Dispose()
    {
        foreach (var s in Shaders.Values)
            foreach (var p in s.Pipelines.Values) p.Dispose();
        foreach (var t in Textures.Values) t.Texture.Dispose();
        foreach (var t in Targets.Values) t.Texture.Dispose();
        Audio?.Dispose();
        Queue?.Dispose();
        Device?.Dispose();
    }
}
#endif

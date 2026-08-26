using DmitryAndDemid.Data.Archive;
using System.Numerics;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// Likhanov32D, the graphics half of the Nikitos Engine — the surface every screen, background and bullet
/// actually draws through.
/// Method names mirror Raylib's on purpose: game files previously did
/// `using static Raylib_cs.Raylib;` and called DrawTexturePro/SetShaderValue/... unqualified. Swapping that
/// single using for `using static DmitryAndDemid.Rendering.Gfx;` re-points every one of those calls at the
/// active backend, with no other edit — which is what makes a second renderer possible at all.
///
/// Everything here is a thin forward to <see cref="Engine"/>. No backend type appears in any signature.
/// </summary>
public static class Gfx
{
    private static IRenderer R => Engine.Renderer;

    // ---- textures -------------------------------------------------------------------------

    public static BasicTexture LoadTexture(string path) => R.LoadTexture(path);

    /// <summary>Uploads tightly packed RGBA pixels (top row first) as a texture — see
    /// <see cref="IRenderer.LoadTextureFromPixels"/>. <see cref="CpuImage.ToTexture"/> is the usual caller.</summary>
    public static BasicTexture LoadTextureFromPixels(byte[] rgba, int width, int height) =>
        R.LoadTextureFromPixels(rgba, width, height);

    public static void UnloadTexture(BasicTexture texture) => R.UnloadTexture(texture);
    public static bool IsTextureValid(BasicTexture texture) => R.IsValid(texture);
    public static void SetTextureFilter(BasicTexture texture, FilterMode filter) => R.SetTextureFilter(texture, filter);

    public static void DrawTexture(BasicTexture texture, int x, int y, Rgba tint) =>
        R.DrawTexture(texture, new Vector2(x, y), tint);

    public static void DrawTextureV(BasicTexture texture, Vector2 position, Rgba tint) =>
        R.DrawTexture(texture, position, tint);

    public static void DrawTextureEx(BasicTexture texture, Vector2 position, float rotation, float scale, Rgba tint) =>
        R.DrawTexture(texture, position, rotation, scale, tint);

    public static void DrawTexturePro(BasicTexture texture, Rect source, Rect dest, Vector2 origin,
        float rotation, Rgba tint) =>
        R.DrawTexture(texture, source, dest, origin, rotation, tint);

    public static void DrawTextureNPatch(BasicTexture texture, NinePatch patch, Rect dest, Vector2 origin,
        float rotation, Rgba tint) =>
        R.DrawNinePatch(texture, patch, dest, origin, rotation, tint);

    // ---- render targets -------------------------------------------------------------------

    public static RenderedTexture LoadRenderTexture(int width, int height) => R.CreateTarget(width, height);
    public static void UnloadRenderTexture(RenderedTexture target) => R.DestroyTarget(target);
    public static bool IsRenderTextureValid(RenderedTexture target) => R.IsValid(target);

    /// <summary>Nesting-safe: EndTextureMode returns to the enclosing target, not to the window.</summary>
    public static void BeginTextureMode(RenderedTexture target) => R.BeginTarget(target);

    public static void EndTextureMode() => R.EndTarget();

    // ---- shaders --------------------------------------------------------------------------

    public static ShaderHandle LoadShader(string? vertexPath, string fragmentPath) => R.LoadShader(vertexPath, fragmentPath);

    public static ShaderHandle LoadShaderFromMemory(string? vertexSource, string fragmentSource) =>
        R.LoadShaderFromSource(vertexSource, fragmentSource);

    public static void UnloadShader(ShaderHandle shader) => R.UnloadShader(shader);
    public static bool IsShaderValid(ShaderHandle shader) => R.IsValid(shader);
    public static void BeginShaderMode(ShaderHandle shader) => R.BeginShader(shader);
    public static void EndShaderMode() => R.EndShader();

    public static int GetShaderLocation(ShaderHandle shader, string name) => R.GetUniformLocation(shader, name);

    public static void SetShaderValue<T>(ShaderHandle shader, int location, T value, UniformType type)
        where T : unmanaged => R.SetUniform(shader, location, value, type);

    public static void SetShaderValue<T>(ShaderHandle shader, string name, T value, UniformType type)
        where T : unmanaged => R.SetUniform(shader, name, value, type);

    public static void SetShaderValueTexture(ShaderHandle shader, int location, BasicTexture texture) =>
        R.SetUniformTexture(shader, location, texture);

    /// <summary>Array form — the game passes float[] literals like [192f, 96f] for vec2 uniforms.</summary>
    public static void SetShaderValue(ShaderHandle shader, int location, float[] values, UniformType type) =>
        R.SetUniformArray(shader, location, values, type);

    // ---- fonts and text -------------------------------------------------------------------

    public static FontHandle LoadFontEx(string path, int fontSize, int[]? codepoints, int codepointCount) =>
        R.LoadFont(path, fontSize);

    public static void UnloadFont(FontHandle font) => R.UnloadFont(font);
    public static FontHandle GetFontDefault() => R.GetDefaultFont();

    public static Vector2 MeasureTextEx(FontHandle font, string text, float fontSize, float spacing) =>
        R.MeasureText(font, text, fontSize, spacing);

    public static Vector2 MeasureMultilineText(FontHandle font, string text, float fontSize, float spacing)
    {
        Vector2 tmp;
        Vector2 measure = Vector2.Zero;
        string[] strs = text.Split("\n");
        foreach (var str in strs)
        {
            tmp = MeasureTextEx(font, str, fontSize, spacing);
            measure.X = MathF.Max(tmp.X, measure.X);
            measure.Y += measure.Y;
        }
        return measure;
    }
    
    /// <summary>Raylib's simple DrawText — default font, integer position.</summary>
    public static void DrawText(string text, int x, int y, int fontSize, Rgba color) =>
        R.DrawText(R.GetDefaultFont(), text, new Vector2(x, y), fontSize, fontSize / 10f, color);

    public static void DrawMultilineText(FontHandle font, string text, Vector2 position, float fontSize, float spacing,
        Rgba tint, TextAlign textAlign = TextAlign.Start, Rgba background = default)
    {
        var strings = text.Split("\n");
        float yOffset = 0;
        Vector2 measure, offset;
        foreach (var str in strings)
        {
            measure = MeasureTextEx(font, str, fontSize, spacing);
            offset = new Vector2(textAlign switch
            {
                TextAlign.Start => 0,
                TextAlign.Center => -measure.X / 2,
                _ => -measure.X
            }, yOffset);
            DrawRectanglePro(new Rect(position, measure), -offset, 0, background);
            DrawTextEx(font, str, position + offset, fontSize, spacing, tint);
            yOffset += measure.Y;
        }
    }
    
    public enum TextAlign
    {
        Start, Center, Stop
    }
    
    public static void DrawTextEx(FontHandle font, string text, Vector2 position, float fontSize, float spacing, Rgba tint) =>
        R.DrawText(font, text, position, fontSize, spacing, tint);
    
    public static void DrawTextPro(FontHandle font, string text, Vector2 position, Vector2 origin, float rotation,
        float fontSize, float spacing, Rgba tint) =>
        R.DrawTextPro(font, text, position, origin, rotation, fontSize, spacing, tint);

    // ---- shapes ---------------------------------------------------------------------------

    public static void ClearBackground(Rgba color) => R.Clear(color);

    public static void DrawRectangle(int x, int y, int width, int height, Rgba color) =>
        R.DrawRect(new Rect(x, y, width, height), color);

    public static void DrawRectangleRec(Rect rect, Rgba color) => R.DrawRect(rect, color);

    public static void DrawRectanglePro(Rect rect, Vector2 origin, float rotation, Rgba color) =>
        R.DrawRect(rect, origin, rotation, color);

    public static void DrawLine(int startX, int startY, int endX, int endY, Rgba color) =>
        R.DrawLine(new Vector2(startX, startY), new Vector2(endX, endY), color);

    public static void BeginBlendMode(BlendMode mode) => R.BeginBlend(mode);
    public static void EndBlendMode() => R.EndBlend();

    // ---- frame / window / time ------------------------------------------------------------

    public static void BeginDrawing() => R.BeginFrame();
    public static void EndDrawing() => R.EndFrame();
    public static double GetTime() => Engine.Platform.Time;
    public static int GetFPS() => Engine.Platform.Fps;
    public static void DrawFPS(int x, int y) => Engine.Platform.DrawFpsCounter(x, y);
    public static void SetTargetFPS(int fps) => Engine.Platform.SetTargetFps(fps);
    public static bool WindowShouldClose() => Engine.Platform.ShouldClose;
    public static int GetScreenWidth() => Engine.Platform.WindowWidth;
    public static int GetScreenHeight() => Engine.Platform.WindowHeight;

    private static readonly Random Rng = new();
    public static int GetRandomValue(int min, int max) => Rng.Next(min, max + 1);

    // ---- input ----------------------------------------------------------------------------

    public static bool IsKeyDown(KeyCode key) => Engine.Input.IsKeyDown(key);
    public static bool IsMouseButtonDown(MouseBtn button) => Engine.Input.IsMouseDown(button);
    public static Vector2 GetMousePosition() => Engine.Input.MousePosition;
    public static Vector2 GetMouseDelta() => Engine.Input.MouseDelta;
    public static float GetMouseWheelMove() => Engine.Input.MouseWheel;
    public static int GetMouseX() => (int)Engine.Input.MousePosition.X;
    public static int GetMouseY() => (int)Engine.Input.MousePosition.Y;
    public static PadButton? GetGamepadButtonPressed() => Engine.Input.GetPressedPadButton();
    public static bool IsGamepadButtonDown(int pad, PadButton button) => Engine.Input.IsPadDown(button);
    public static int GetGamepadCount() => Engine.Input.GamepadCount;

    // ---- audio ----------------------------------------------------------------------------

    /// <summary>
    /// Loads a sound, decoding the formats the backend cannot read itself. Only FLAC needs that today: no
    /// shipped raylib native is built with SUPPORT_FILEFORMAT_FLAC, so it is decoded in managed code (see
    /// <see cref="Utils.FlacAudio"/>) and handed over as PCM. A file that fails to decode returns
    /// <see cref="SoundHandle.None"/> and says so, rather than becoming a sound that plays silence.
    /// </summary>
    public static SoundHandle LoadSound(string path)
    {
        if (!Utils.FlacAudio.IsFlac(path))
            return Engine.Audio.LoadSound(path);
        try
        {
            Utils.FlacAudio.PcmSound pcm = Utils.FlacAudio.Decode(Utils.Assets.ReadAllBytes(path));
            return Engine.Audio.LoadSoundFromPcm(pcm.Samples, pcm.SampleRate, pcm.Channels);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[audio] could not decode {path}: {e.Message}");
            return SoundHandle.None;
        }
    }
    public static void UnloadSound(SoundHandle sound) => Engine.Audio.UnloadSound(sound);
    public static void PlaySound(SoundHandle sound) => Engine.Audio.Play(sound);
}

using System.Numerics;

namespace DmitryAndDemid.Rendering;

/// <summary>Window, main-loop timing and the frame counter.</summary>
public interface IPlatform : IDisposable
{
    void OpenWindow(int width, int height, string title);
    void CloseWindow();
    bool ShouldClose { get; }

    /// <summary>Sets the window/taskbar icon from a PNG on disk. No-op where the platform has no window icon.</summary>
    void SetWindowIcon(string path);

    void SetWindowSize(int width, int height);

    /// <summary>
    /// Switches presentation mode. <paramref name="windowedWidth"/>/<paramref name="windowedHeight"/> are the
    /// size to restore to in <see cref="WindowMode.Windowed"/>; the fullscreen modes size themselves to the
    /// monitor. Safe to call repeatedly, including with the mode already active.
    /// </summary>
    void ApplyWindowMode(WindowMode mode, int windowedWidth, int windowedHeight);

    WindowMode CurrentWindowMode { get; }

    /// <summary>Size of the window's drawable area, in real pixels — NOT the game's internal resolution.</summary>
    int WindowWidth { get; }

    int WindowHeight { get; }

    /// <summary>Size of the monitor the window is currently on.</summary>
    int MonitorWidth { get; }

    int MonitorHeight { get; }

    void SetVSync(bool enabled);
    void SetTargetFps(int fps);

    /// <summary>Stop the backend from closing the window on Escape; the game handles it itself.</summary>
    void DisableExitKey();

    /// <summary>Seconds since the window opened. Monotonic; the whole 60 TPS tick clock is derived from it.</summary>
    double Time { get; }

    int Fps { get; }
    void DrawFpsCounter(int x, int y);
}

/// <summary>Keyboard, mouse and gamepad polling.</summary>
public interface IInput
{
    bool IsKeyDown(KeyCode key);

    bool IsMouseDown(MouseBtn button);
    Vector2 MousePosition { get; }
    Vector2 MouseDelta { get; }
    float MouseWheel { get; }

    /// <summary>Number of connected pads; refreshed by the platform once per frame.</summary>
    int GamepadCount { get; }

    /// <summary>True if <paramref name="button"/> is held on ANY connected pad.</summary>
    bool IsPadDown(PadButton button);

    /// <summary>The largest-magnitude value of <paramref name="axis"/> across all connected pads.</summary>
    float GetPadAxis(PadAxis axis);

    /// <summary>The button most recently pressed on any pad, for the rebinding screen. Null if none.</summary>
    PadButton? GetPressedPadButton();

    /// <summary>Re-scan for connected pads. Called once per frame by the main loop.</summary>
    void RefreshGamepads();

    /// <summary>
    /// Active touch points, in WINDOW pixels. Raylib reports real multi-touch; the Silk backends have no
    /// touch API, so they report the mouse as a single point while the left button is held — which makes the
    /// touch controls testable on a desktop without a touchscreen.
    /// </summary>
    int TouchCount { get; }

    Vector2 GetTouchPosition(int index);
}

/// <summary>
/// Sound effects. Overlapping playback of the same sample is the backend's problem, not the caller's:
/// <see cref="Play"/> may be called many times in a frame for the same handle.
/// </summary>
public interface IAudio : IDisposable
{
    /// <summary>Returns false if the audio device could not be opened; the game still runs, silently.</summary>
    bool Initialize();

    bool IsAvailable { get; }

    SoundHandle LoadSound(string path);

    /// <summary>
    /// Loads a sound from already-decoded interleaved signed 16-bit PCM. The seam exists for formats the
    /// backend's own loader cannot read — FLAC is decoded in managed code by <see cref="Utils.FlacAudio"/>
    /// and handed over here (see <see cref="Gfx.LoadSound"/>). Returns <see cref="SoundHandle.None"/> on a
    /// backend with no way to accept raw PCM.
    /// </summary>
    SoundHandle LoadSoundFromPcm(short[] samples, int sampleRate, int channels);

    void UnloadSound(SoundHandle sound);

    /// <summary>Plays a one-shot at the current SFX volume, overlapping any already-playing copy.</summary>
    void Play(SoundHandle sound);

    /// <summary>Master SFX volume, 0..1, applied to every subsequent <see cref="Play"/>.</summary>
    float SfxVolume { get; set; }
}

/// <summary>A complete platform implementation. <see cref="Engine"/> is pointed at one of these at startup.</summary>
public interface IBackend : IRenderer, IPlatform, IInput, IAudio
{
    string Name { get; }

    /// <summary>
    /// Whether this backend can host the ImGui debug/editor UI. rlImGui is bound to Raylib, so the Silk
    /// backend reports false and the DEBUG editor overlays are simply skipped rather than crashing.
    /// </summary>
    bool SupportsDebugUi { get; }

    void SetupDebugUi();
    void BeginDebugUi();
    void EndDebugUi();

    /// <summary>Draws a texture inside the current ImGui window (used by the stage/gameplay editors).</summary>
    void DebugUiImage(TextureHandle texture);

    void DebugUiImage(TargetHandle target);
}

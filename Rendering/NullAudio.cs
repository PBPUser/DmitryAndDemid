namespace DmitryAndDemid.Rendering;

/// <summary>
/// Silence. Audio on desktop goes through Raylib's mixer, which has no Android build; rather than block the
/// port on an AAudio/OpenSL backend, Android runs the game mute. Every call is a no-op, so the game's sound
/// calls stay on their normal path and nothing has to know audio is missing.
/// </summary>
public sealed class NullAudio : IAudio
{
    private int NextId = 1;

    // Reports success: "no audio device" is a normal, non-fatal state on Android (the game otherwise stops on
    // its "cannot initialise audio" screen and never reaches the menu). Playback is simply a no-op.
    public bool Initialize() => true;
    public bool IsAvailable => true;
    public float SfxVolume { get; set; } = 1f;

    public SoundHandle LoadSound(string path) => new(NextId++);
    public void UnloadSound(SoundHandle sound) { }
    public void Play(SoundHandle sound) { }
    public void Dispose() { }
}

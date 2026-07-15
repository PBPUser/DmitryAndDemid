using Android.Media;
using DmitryAndDemid.Rendering;

namespace DmitryAndDemid.Android;

/// <summary>
/// Sound on Android. The game's audio is one-shot SFX (Helper.PlaySound → IAudio.Play), which is exactly what
/// <see cref="SoundPool"/> is for; the background-music path is unimplemented on every platform
/// (Helper.UpdatePlayingMusic throws), so nothing longer than a sound effect passes through here.
///
/// Replaces the silent NullAudio the shared backend defaults to — the desktop mixer is Raylib's, which has no
/// Android build.
/// </summary>
public sealed class AndroidAudio : IAudio
{
    private SoundPool? Pool;

    // Our handle id -> SoundPool sample id, and which samples have finished their (asynchronous) load.
    private readonly Dictionary<int, int> Samples = new();
    private readonly HashSet<int> Ready = new();
    private int NextId = 1;

    public float SfxVolume { get; set; } = 1f;
    public bool IsAvailable => Pool != null;

    public bool Initialize()
    {
        AudioAttributes attributes = new AudioAttributes.Builder()!
            .SetUsage(AudioUsageKind.Game)!
            .SetContentType(AudioContentType.Sonification)!
            .Build()!;

        Pool = new SoundPool.Builder()
            .SetMaxStreams(16)!
            .SetAudioAttributes(attributes)!
            .Build();

        // A sample can only be played once its async load finishes; remember which are ready so an early
        // Play() is a silent no-op rather than a failed one.
        Pool.LoadComplete += (_, e) =>
        {
            if (e.Status == 0)
                Ready.Add(e.SampleId);
        };
        return true;
    }

    public SoundHandle LoadSound(string path)
    {
        int id = NextId++;
        if (Pool == null)
            return new SoundHandle(id);
        Samples[id] = Pool.Load(path, 1);
        return new SoundHandle(id);
    }

    public void UnloadSound(SoundHandle sound)
    {
        if (!Samples.Remove(sound.Id, out int sample))
            return;
        Ready.Remove(sample);
        Pool?.Unload(sample);
    }

    public void Play(SoundHandle sound)
    {
        if (Pool != null && Samples.TryGetValue(sound.Id, out int sample) && Ready.Contains(sample))
            Pool.Play(sample, SfxVolume, SfxVolume, 1, 0, 1f);
    }

    public void Dispose()
    {
        Pool?.Release();
        Pool = null;
    }
}

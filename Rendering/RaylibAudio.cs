using Raylib_cs;

namespace DmitryAndDemid.Rendering;

/// <summary>
/// Sound effects via Raylib's mixer, which is independent of the window/graphics context — so the Silk
/// backend can use it too. This is the one seam where the Silk renderer still touches Raylib; replacing it
/// with Silk.NET.OpenAL is self-contained and changes nothing outside this file.
///
/// It owns the alias ring buffer so callers just say Play(). The game's old Helper.PlaySound rolled its own
/// and had a bug: it stored the original Sound instead of the alias it created, so UnloadSoundAlias was
/// later handed a non-alias.
/// </summary>
public sealed class RaylibAudio : IAudio
{
    private const int AliasSlots = 64;

    private readonly Dictionary<int, Sound> Sounds = new();
    private readonly Sound?[] Aliases = new Sound?[AliasSlots];
    private int AliasCursor;
    private int NextId = 1;

    public bool IsAvailable { get; private set; }

    public float SfxVolume { get; set; } = 1f;

    public bool Initialize()
    {
        try
        {
            Raylib.InitAudioDevice();
            IsAvailable = Raylib.IsAudioDeviceReady();
        }
        catch (Exception)
        {
            IsAvailable = false;
        }
        return IsAvailable;
    }

    public SoundHandle LoadSound(string path)
    {
        if (!IsAvailable)
            return SoundHandle.None;
        Sound sound = Raylib.LoadSound(path);
        // Raylib returns a zero-frame Sound for a format it was not built to decode (it only logs to its own
        // console). Registering that gives a handle that loads "fine" and plays silence forever — which is
        // exactly how the FLAC assets went unnoticed. Refuse it here so the caller can report the file.
        if (!Raylib.IsSoundValid(sound))
            return SoundHandle.None;
        int id = NextId++;
        Sounds[id] = sound;
        return new SoundHandle(id);
    }

    /// <summary>
    /// Wraps caller-decoded PCM in a Wave and uploads it. Raylib copies the samples into the Sound during
    /// LoadSoundFromWave (converting to the device format), so the pin only has to survive that call and the
    /// Wave is unloaded immediately after.
    /// </summary>
    public unsafe SoundHandle LoadSoundFromPcm(short[] s, int sRate, int chn)
    {
        if (!IsAvailable || s.Length == 0 || chn < 1)
            return SoundHandle.None;

        fixed (short* p = s)
        {
            Wave wave = new()
            {
                // Raylib-cs still calls this SampleCount, but it sits over native raylib's `frameCount`, so
                // it counts FRAMES, not interleaved samples. Passing samples.Length here plays a stereo clip
                // at half speed for twice as long.
                SampleCount = (uint)(s.Length / chn),
                SampleRate = (uint)sRate,
                SampleSize = 16,
                Channels = (uint)chn,
                Data = p,
            };
            Sound sound = Raylib.LoadSoundFromWave(wave);
            if (!Raylib.IsSoundValid(sound))
                return SoundHandle.None;
            int id = NextId++;
            Sounds[id] = sound;
            return new SoundHandle(id);
        }
    }

    public void UnloadSound(SoundHandle sound)
    {
        if (Sounds.Remove(sound.Id, out Sound native))
            Raylib.UnloadSound(native);
    }

    /// <summary>
    /// Plays through a ring of aliases so a sample can overlap with itself. The slot about to be reused is
    /// unloaded first — by the time the cursor wraps, that alias has long finished.
    /// </summary>
    public void Play(SoundHandle sound)
    {
        if (!IsAvailable || !Sounds.TryGetValue(sound.Id, out Sound native))
            return;

        if (Aliases[AliasCursor] is { } expired)
            Raylib.UnloadSoundAlias(expired);

        Sound alias = Raylib.LoadSoundAlias(native);
        Raylib.SetSoundVolume(alias, SfxVolume);
        Raylib.PlaySound(alias);

        Aliases[AliasCursor] = alias;
        AliasCursor = (AliasCursor + 1) % AliasSlots;
    }

    public void Dispose()
    {
        foreach (Sound? alias in Aliases)
            if (alias is { } native)
                Raylib.UnloadSoundAlias(native);
        Array.Clear(Aliases);

        foreach (Sound sound in Sounds.Values)
            Raylib.UnloadSound(sound);
        Sounds.Clear();

        if (IsAvailable)
            Raylib.CloseAudioDevice();
        IsAvailable = false;
    }
}

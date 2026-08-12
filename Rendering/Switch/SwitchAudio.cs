#if SWITCH
using System.Runtime.InteropServices;
using DmitryAndDemid.Utils;

namespace DmitryAndDemid.Rendering.Switch;

/// <summary>
/// SDL2-core audio for the Switch with software mixing. mono-nx shims neither SDL2_mixer (no ready-made mixer)
/// nor a managed mp3/ogg decoder, so: the Switch ships offline-converted WAVs (44100 Hz, s16, stereo — matching
/// the opened device), decoded once into PCM via SDL_LoadWAV_RW; <see cref="Play"/> adds a voice; and
/// <see cref="Pump"/> (called once per frame from the backend's EndFrame) sums all active voices into a small
/// buffer and pushes it with SDL_QueueAudio. Summing is what makes overlapping one-shots actually overlap —
/// SDL_QueueAudio on its own only plays clips back-to-back.
///
/// Single-threaded: Play and Pump both run on the game's main thread (no audio callback), so no locking. Reads
/// use fixed-offset Marshal calls (no dynamic-code marshaling stubs, which the interpreter can't JIT).
///
/// Guarded: if the device cannot open, <see cref="Initialize"/> reports unavailable and every call is a no-op —
/// but still returns TRUE, since the loader treats a false as a fatal audio error (ADP halt screen).
/// </summary>
internal sealed class SwitchAudio : IDisposable
{
    private uint device;
    private bool available;
    // SoundHandle.Id -> PCM. sdlOwned distinguishes SDL_LoadWAV_RW buffers (freed with SDL_FreeWAV) from
    // ones this class allocated for caller-decoded PCM (freed with Marshal.FreeHGlobal).
    private readonly List<(IntPtr buf, int len, bool sdlOwned)> sounds = new();
    private float sfxVolume = 1f;

    // One playing voice: which sound, byte offset into it, and the gain it was started at (snapshot of volume).
    private struct Voice { public int sound; public int pos; public int vol256; }
    private readonly List<Voice> voices = new();
    private const int MaxVoices = 32;

    private const int Freq = 44100, Channels = 2, BytesPerFrame = Freq * Channels * 2 / 60;   // ~2940 B / video frame
    private const int TargetBytes = BytesPerFrame * 4;   // keep ~4 frames (~66 ms) queued to ride interpreter hitches
    private readonly int[] acc = new int[TargetBytes / 2];   // reused mix accumulator (int16 samples)
    private readonly byte[] outBuf = new byte[TargetBytes];  // reused output (clamped s16 LE)

    public bool IsAvailable => available;
    public float SfxVolume { get => sfxVolume; set => sfxVolume = Math.Clamp(value, 0f, 1f); }

    private static string Err() => Marshal.PtrToStringUTF8(Sdl.SDL_GetError()) ?? "";

    public bool Initialize()
    {
        try
        {
            var want = new Sdl.SDL_AudioSpec { freq = Freq, format = Sdl.AUDIO_S16LSB, channels = (byte)Channels, samples = 1024 };
            device = Sdl.SDL_OpenAudioDevice(IntPtr.Zero, 0, ref want, out Sdl.SDL_AudioSpec have, 0);
            if (device == 0) { Console.WriteLine("[audio] SDL_OpenAudioDevice failed: " + Err()); available = false; }
            else
            {
                Sdl.SDL_PauseAudioDevice(device, 0);
                available = true;
                Console.WriteLine($"[audio] SDL audio open: dev={device} {have.freq}Hz {have.channels}ch fmt=0x{have.format:X} (software mixing)");
            }
        }
        catch (Exception e) { Console.WriteLine("[audio] SDL audio unavailable, running silent: " + e.Message); available = false; }
        return true;
    }

    public SoundHandle LoadSound(string path)
    {
        if (!available) return SoundHandle.None;
        if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return SoundHandle.None;   // Switch ships WAVs
        try
        {
            byte[] data = Assets.ReadAllBytes(path);
            GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
            try
            {
                IntPtr rw = Sdl.SDL_RWFromMem(pin.AddrOfPinnedObject(), data.Length);
                if (rw == IntPtr.Zero) return SoundHandle.None;
                IntPtr res = Sdl.SDL_LoadWAV_RW(rw, 1, out Sdl.SDL_AudioSpec _, out IntPtr buf, out uint len);
                if (res == IntPtr.Zero || buf == IntPtr.Zero) { Console.WriteLine($"[audio] LoadWAV failed {path}: {Err()}"); return SoundHandle.None; }
                sounds.Add((buf, (int)len, true));
                return new SoundHandle(sounds.Count - 1);
            }
            finally { pin.Free(); }
        }
        catch (Exception e) { Console.WriteLine($"[audio] LoadSound {path} threw: {e.Message}"); return SoundHandle.None; }
    }

    /// <summary>
    /// Takes caller-decoded PCM (FLAC arrives this way — see Utils.FlacAudio). The mixer in <see cref="Pump"/>
    /// does no resampling or channel mapping: it reads sounds as s16 straight into the device's own stream, so
    /// anything that is not 44100 Hz stereo is refused rather than played at the wrong pitch.
    /// </summary>
    public SoundHandle LoadSoundFromPcm(short[] samples, int sampleRate, int channels)
    {
        if (!available || samples.Length == 0) return SoundHandle.None;
        if (sampleRate != Freq || channels != Channels)
        {
            Console.WriteLine($"[audio] refusing PCM at {sampleRate}Hz {channels}ch; device is {Freq}Hz {Channels}ch");
            return SoundHandle.None;
        }
        int bytes = samples.Length * 2;
        IntPtr buf = Marshal.AllocHGlobal(bytes);
        Marshal.Copy(samples, 0, buf, samples.Length);
        sounds.Add((buf, bytes, false));
        return new SoundHandle(sounds.Count - 1);
    }

    public void Play(SoundHandle sound)
    {
        if (!available || sound.Id < 0 || sound.Id >= sounds.Count) return;
        if (sounds[sound.Id].buf == IntPtr.Zero || sounds[sound.Id].len < 2) return;
        if (voices.Count >= MaxVoices) return;   // hard cap; drop the quietest-to-add rather than unbounded growth
        voices.Add(new Voice { sound = sound.Id, pos = 0, vol256 = (int)(sfxVolume * 256) });
    }

    /// <summary>Called once per rendered frame. Mixes active voices into the queue, keeping ~TargetBytes buffered.</summary>
    public void Pump()
    {
        if (!available || voices.Count == 0) return;   // nothing to mix -> device drains to silence on its own

        uint queued = Sdl.SDL_GetQueuedAudioSize(device);
        if (queued >= TargetBytes) return;
        int need = (TargetBytes - (int)queued) & ~3;   // whole s16-stereo frames (4 bytes)
        if (need <= 0) return;
        int samples = need / 2;                         // int16 samples across both channels
        Array.Clear(acc, 0, samples);

        for (int v = voices.Count - 1; v >= 0; v--)
        {
            Voice voice = voices[v];
            (IntPtr src, int srcLen, _) = sounds[voice.sound];
            int pos = voice.pos, i = 0;
            for (; i < samples && pos + 1 < srcLen; i++, pos += 2)
                acc[i] += (Marshal.ReadInt16(src, pos) * voice.vol256) >> 8;
            if (pos + 1 >= srcLen) voices.RemoveAt(v);   // voice finished
            else { voice.pos = pos; voices[v] = voice; }
        }

        for (int i = 0; i < samples; i++)
        {
            int s = acc[i];
            if (s > short.MaxValue) s = short.MaxValue; else if (s < short.MinValue) s = short.MinValue;
            outBuf[i * 2] = (byte)s;
            outBuf[i * 2 + 1] = (byte)(s >> 8);
        }

        GCHandle h = GCHandle.Alloc(outBuf, GCHandleType.Pinned);
        try { Sdl.SDL_QueueAudio(device, h.AddrOfPinnedObject(), (uint)need); }
        finally { h.Free(); }
    }

    public void UnloadSound(SoundHandle sound) { }

    public void Dispose()
    {
        if (!available) return;
        if (device != 0) { try { Sdl.SDL_CloseAudioDevice(device); } catch { } }
        foreach (var (buf, _, sdlOwned) in sounds)
            if (buf != IntPtr.Zero)
                try { if (sdlOwned) Sdl.SDL_FreeWAV(buf); else Marshal.FreeHGlobal(buf); } catch { }
        sounds.Clear();
        available = false;
    }
}
#endif

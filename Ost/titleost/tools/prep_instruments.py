# Sample instruments for titleost, the same way ost_remake/tools/prep_instruments.py makes them: the steadiest voiced
# chunk of a source, tuned to the nearest semitone, root written to samples/roots.txt for the sampler channel.
#   python tools/prep_instruments.py
import librosa, numpy as np, soundfile as sf, pedalboard as pb, warnings
from pathlib import Path
warnings.filterwarnings("ignore")

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "samples"; OUT.mkdir(exist_ok=True)
SLONIK_SONG = r"D:/Profile/DWN/y2mate.com - Зеленый слоник песня.mp3"
GACHIMOUSE = r"D:/files-of-user/Music/пукнул_гачимаус.ogg"    # the one your 17h28 project had among its 808 drums
VITEC = "D:/samples/vitec/"
SR, HOP = 44100, 256

def fade(x, fi, fo):
    x = x.copy(); a, b = max(1, int(fi * SR)), max(1, int(fo * SR))
    x[:, :a] *= np.linspace(0, 1, a); x[:, -b:] *= np.linspace(1, 0, b); return x

def sung_note(src, dst, seconds, stretch, fi, fo, fmin=70, fmax=1000, within=None):
    """The steadiest `seconds` of singing in `src` (only inside the `within` (start, end) span, if given), tuned to
    the nearest semitone and stretched `stretch` times longer (pitch kept) so it can hold a note."""
    y, _ = librosa.load(src, sr=SR, mono=False)
    y = np.stack([y, y]) if y.ndim == 1 else y
    f0, vf, _ = librosa.pyin(y.mean(0), fmin=fmin, fmax=fmax, sr=SR, frame_length=2048, hop_length=HOP)
    W = int(seconds * SR / HOP); best = None
    lo, hi = (int(within[0] * SR / HOP), int(within[1] * SR / HOP)) if within else (0, len(f0))
    for i in range(lo, min(hi, len(f0)) - W, 2):
        v = vf[i:i + W]
        if v.mean() < 0.95: continue
        c = np.std(librosa.hz_to_midi(f0[i:i + W][v]))
        if best is None or c < best[0]: best = (c, i)
    c, i = best
    m = librosa.hz_to_midi(np.nanmedian(f0[i:i + W][vf[i:i + W]])); root = int(round(m))
    x = y[:, i * HOP:i * HOP + int(seconds * SR)]
    x = pb.time_stretch(np.ascontiguousarray(x, dtype=np.float32), SR, stretch_factor=1 / stretch,
                        pitch_shift_in_semitones=float(root - m), high_quality=True)
    x = fade(x / np.abs(x).max() * 0.9, fi, fo)
    sf.write(OUT / dst, x.T, SR, subtype="PCM_24")
    print(f"{dst:20s} from {i * HOP / SR:6.2f}s, wobble {c:.2f} st, root {root} ({librosa.midi_to_note(root, unicode=False)}), "
          f"tuned {root - m:+.2f} st, {x.shape[1] / SR:.2f}s")
    return root

def one_shot(src, dst, length, pre=0.005, fo=0.08, gate=0.1):
    """An unpitched hit: cut from just before the first sample louder than `gate` x the peak, `length` long,
    normalised. Played on a sampler at C5, its original pitch."""
    y, _ = librosa.load(src, sr=SR, mono=False)
    y = np.stack([y, y]) if y.ndim == 1 else y
    a = max(0, int(np.argmax(np.abs(y).mean(0) > gate * np.abs(y).max())) - int(pre * SR))
    x = fade(y[:, a:a + int(length * SR)] / np.abs(y).max() * 0.9, 0.002, fo)
    sf.write(OUT / dst, x.T, SR, subtype="PCM_24")
    print(f"{dst:20s} from {a / SR:6.2f}s, {x.shape[1] / SR:.2f}s one-shot")

def chop(src, dst, start, end, fi=0.01, fo=0.08):
    """A spoken phrase cut by time (word timings from Whisper, checked against the loudness), normalised."""
    y, _ = librosa.load(src, sr=SR, mono=False)
    y = np.stack([y, y]) if y.ndim == 1 else y
    x = y[:, int(start * SR):int(end * SR)]
    x = fade(x / np.abs(x).max() * 0.9, fi, fo)
    sf.write(OUT / dst, x.T, SR, subtype="PCM_24")
    print(f"{dst:20s} {start:5.2f}-{end:5.2f}s of {Path(src).name}, {x.shape[1] / SR:.2f}s")

roots = {"slonik_vox": sung_note(SLONIK_SONG, "slonik_vox.wav", seconds=0.5, stretch=3.0, fi=0.02, fo=0.35)}
one_shot(GACHIMOUSE, "gachimouse.wav", length=0.45)

# D:\samples\vitec: six voice messages. Picked for what works in the track, the explicit lines left out.
V = lambda name: VITEC + name
roots["vitec_vox"] = sung_note(V("audio_2025-01-14_19-42-08.ogg"), "vitec_vox.wav", seconds=0.5, stretch=3.0,
                               fi=0.02, fo=0.35, within=(4.4, 13.8))          # the held sung notes, not the talking
chop(V("audio_2025-01-14_19-56-56.ogg"), "vit_zdorovo.wav", 0.0, 6.80, fo=0.12)    # "...только вверх, будет здорово x3"
chop(V("audio_2025-01-16_18-14-18.ogg"), "vit_don.wav", 10.36, 10.85)              # "Дон." (the isolated third one)
chop(V("audio_2025-01-14_21-37-06.ogg"), "vit_oi_horosho.wav", 0.45, 1.32)        # "Ой, хорошо!" (stops before the next word)
chop(V("audio_2025-01-14_19-42-26.ogg"), "vit_stat.wav", 5.30, 7.40, fo=0.25)     # "Staaaaaat"
open(OUT / "roots.txt", "w").write("".join(f"{k} {v}\n" for k, v in roots.items()))

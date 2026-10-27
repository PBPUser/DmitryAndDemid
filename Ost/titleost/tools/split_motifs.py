# Cut the source tracks into motifs ("patches") for the main-menu theme (titleost).
#   python tools/split_motifs.py
# Reads sources/*.mid, writes motifs/NN <Name>.mid (one track per part, drag onto a channel / into the piano roll)
# and motifs/motifs.json (same note format as cmaket/ost_remake/tools/akob_notes.json: [start_beat, dur_beat, pitch, vel]).
import json, mido
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC, OUT = ROOT / "sources", ROOT / "motifs"
OUT.mkdir(exist_ok=True)
PPQ = 384          # IndiHome's own resolution; its string glissando uses 1/96-beat notes, 96 PPQ would smear them
BPM = 130          # IndiHome's tempo, the target for the theme
NAMES = "C C# D D# E F F# G G# A A# B".split()

def load(path, shift_beats=0.0):
    """{track name: (channel, program, [[start_beat, dur_beat, pitch, vel], ...])}, start shifted by shift_beats."""
    mf = mido.MidiFile(path); tpb = mf.ticks_per_beat
    parts = {}
    for t in mf.tracks:
        now, on, notes, ch, prog = 0, {}, [], None, 0
        for m in t:
            now += m.time
            if m.type == "program_change": prog = m.program
            elif m.type == "note_on" and m.velocity > 0: on[(m.channel, m.note)] = (now, m.velocity); ch = m.channel
            elif m.type in ("note_on", "note_off") and (m.channel, m.note) in on:
                s, v = on.pop((m.channel, m.note))
                notes.append([s / tpb - shift_beats, (now - s) / tpb, m.note, v])
        if notes: parts[t.name] = (ch, prog, sorted(notes))
    return parts

def cut(notes, bar_from, bars, keep=lambda n: True, octave_dedupe=False, snap=None):
    """Notes starting inside [bar_from, bar_from + bars) (1-based bars of 4 beats), re-based to the motif start."""
    a, b = (bar_from - 1) * 4, (bar_from - 1 + bars) * 4
    out = []
    for s, d, p, v in notes:
        if snap: s = round(s / snap) * snap
        if a - 1e-6 <= s < b - 1e-6 and keep(p):
            out.append([round(s - a, 6), round(min(d, b - s), 6), p, v])
    if octave_dedupe:   # the lead is written as two parallel octaves; keep the lower line
        below = {(s, p) for s, d, p, v in out}
        out = [n for n in out if (n[0], n[2] - 12) not in below]
    return out

def to_zero(notes):
    t = min(n[0] for n in notes)
    return [[round(s - t, 6), d, p, v] for s, d, p, v in notes]

def norm(notes, top=100):
    """Scale velocities so the loudest note is `top` (the sources sit at 39-68, too quiet for FL's default mapping)."""
    hi = max(v for *_, v in notes)
    return [[s, d, p, max(1, min(127, round(v * top / hi)))] for s, d, p, v in notes]

def transpose(notes, k): return [[s, d, p + k, v] for s, d, p, v in notes]

MOTIFS = []   # (file name, info dict, {part: (channel, program, notes)})
def motif(name, info, parts):
    MOTIFS.append((f"{len(MOTIFS) + 1:02} {name}", info, parts))

# ======================================================================= IndiHome (Paket Phoenix)
# The MIDI starts one beat early: the bass, the crash and the lead all land on beat 2 of its bar 1, and the triangle
# on its beat 1 is a pickup. Shifting by one beat makes bar 1 the real downbeat; every bar number below is that grid.
IH = load(SRC / "indihome.mid", shift_beats=1.0)
bass, lead, drums, strings, rhodes = (IH[k] for k in ("Bass Guitar", "Synth Bass", "Electric Drum Kit", "Strings", "Rhodes"))
IH_INFO = dict(source="IndiHome Paket Phoenix", bpm=130, key="A minor")
KICK, CLAP, SNARE, CRASH, TRIANGLE = 35, 39, 40, 49, 81

motif("IndiHome Riff Lead", dict(IH_INFO, bars=4, song_bars="1-4",
      note="verse lead; the source doubles it an octave up (A5), dropped here - stack it back with the chord tool if wanted. "
           "Bars 1-2 and 3-4 differ only in the turnaround (A A A C E D C B)"),
      {"Riff Lead": (1, lead[1], norm(cut(lead[2], 1, 4, octave_dedupe=True)))})
motif("IndiHome Hook", dict(IH_INFO, bars=4, song_bars="9-12",
      note="the jingle melody. 2-bar call + 2 answers: bar 2 ends F-E-E-D (open), bar 4 ends F-F-E-D-C (closed). Same octave doubling dropped"),
      {"Hook": (1, lead[1], norm(cut(lead[2], 9, 4, octave_dedupe=True)))})
motif("IndiHome Bass Riff", dict(IH_INFO, bars=2, song_bars="1-2",
      note="A . C . C D E | A . C . C E -- the same 2 bars under the whole song, verse and hook"),
      {"Bass Riff": (0, bass[1], norm(cut(bass[2], 1, 2)))})
motif("IndiHome Offbeat Chords", dict(IH_INFO, bars=1, song_bars="2",
      note="Am (C5 E5 A5) stab on every off-beat 8th, never moves. GM program 2 in the source, named 'Rhodes'"),
      {"Offbeat Chords": (3, rhodes[1], norm(cut(rhodes[2], 2, 1)))})
motif("IndiHome Drum Groove", dict(IH_INFO, bars=4, song_bars="1-4",
      note="kick 35 / clap 39 (GM drum map, channel 10). Kick on 1, 2-and, 3-and; clap on 2 and 4; extra kick on the "
           "4-e of bar 2. Opening crash left out (see Break Bar / Ending Hit)"),
      {"Drums": (9, 0, norm(cut(drums[2], 1, 4, keep=lambda p: p != CRASH), 110))})
motif("IndiHome Break Bar", dict(IH_INFO, bars=1, song_bars="8",
      note="the bar between verse and hook: crash, two claps, a triangle pickup, string swoop A3->D3 then chord hits and a "
           "long chromatic fall F4->F#2 into the hook. The fall is ~1/96-beat notes, a rendered glissando"),
      {"Drums": (9, 0, norm(cut(drums[2], 8, 1), 110)),
       "Strings": (2, strings[1], norm(cut(strings[2], 8, 1))),
       "Riff Lead": (1, lead[1], norm(cut(lead[2], 8, 1, octave_dedupe=True)))})
motif("IndiHome String Fall", dict(IH_INFO, bars=1, song_bars="8 (beat 4)",
      note="just the long chromatic fall as a one-shot FX, starts on beat 1 here (was 8:4.17 in the song)"),
      {"Strings": (2, strings[1], norm(to_zero([n for n in cut(strings[2], 8, 2) if n[0] >= 3.1])))})
motif("IndiHome Snare Roll", dict(IH_INFO, bars=1, song_bars="16",
      note="the fill into the ending: 8 x 16ths then 4 x 32nds, on GM snare 40. The source plays every hit 1/24 beat early "
           "(the first one even leaks into bar 15); snapped to the 32nd grid here"),
      {"Drums": (9, 0, norm(cut(drums[2], 16, 1, keep=lambda p: p == SNARE, snap=0.125), 110))})
motif("IndiHome Ending Hit", dict(IH_INFO, bars=1, song_bars="17",
      note="last downbeat: crash + the lead's low A (A3/A4)"),
      {"Drums": (9, 0, norm(cut(drums[2], 17, 1, snap=0.125), 110)),
       "Riff Lead": (1, lead[1], norm(cut(lead[2], 17, 1)))})

# ======================================================================= super-akob-kart
# 4 bars of 8ths at 135 BPM, a descending chromatic broken-thirds line centred on C. ost_remake moves it -3 into A,
# which is also IndiHome's key, so the A version is the one that drops straight in next to the motifs above.
KART = load(SRC / "super-akob-kart.mid")["MIDI Out"]
KART_INFO = dict(source="super-akob-kart", bpm=135, bars=4, song_bars="1-4")
motif("Kart Riff (original)", dict(KART_INFO, key="C-centred chromatic",
      note="as written, 135 BPM; same notes as cmaket/ost_remake/tools/akob_notes.json"),
      {"Kart Riff": (0, 80, norm(KART[2]))})
motif("Kart Riff (in A)", dict(KART_INFO, key="A minor (transposed -3)", bpm=130,
      note="down 3 semitones like ost_remake's Kart Riff, so it sits on IndiHome's A; tempo map set to 130 to match"),
      {"Kart Riff": (0, 80, norm(transpose(KART[2], -3)))})

# ======================================================================= write
def write_mid(path, title, bpm, key, parts):
    mf = mido.MidiFile(type=1, ticks_per_beat=PPQ)
    t0 = mido.MidiTrack(); mf.tracks.append(t0)
    t0 += [mido.MetaMessage("track_name", name=title, time=0),
           mido.MetaMessage("set_tempo", tempo=mido.bpm2tempo(bpm), time=0),
           mido.MetaMessage("time_signature", numerator=4, denominator=4, time=0)]
    if key.startswith("A minor"): t0.append(mido.MetaMessage("key_signature", key="Am", time=0))
    for name, (ch, prog, notes) in parts.items():
        tr = mido.MidiTrack(); mf.tracks.append(tr)
        tr.append(mido.MetaMessage("track_name", name=name, time=0))
        if ch != 9: tr.append(mido.Message("program_change", channel=ch, program=prog, time=0))
        ev = []
        for s, d, p, v in notes:
            a = round(s * PPQ); b = max(a + 1, round((s + d) * PPQ))   # zero-length source notes still sound
            ev += [(a, 1, mido.Message("note_on", channel=ch, note=p, velocity=v)),
                   (b, 0, mido.Message("note_off", channel=ch, note=p, velocity=0))]
        now = 0
        for t, _, m in sorted(ev, key=lambda e: (e[0], e[1])):
            tr.append(m.copy(time=t - now)); now = t
    mf.save(path)

index = {}
for fname, info, parts in MOTIFS:
    write_mid(OUT / f"{fname}.mid", fname[3:], info["bpm"], info["key"], parts)
    index[fname] = dict(info, parts={k: v[2] for k, v in parts.items()})
json.dump(index, open(OUT / "motifs.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)

nm = lambda p: f"{NAMES[p % 12]}{p // 12 - 1}"
for fname, info, parts in MOTIFS:
    n = sum(len(v[2]) for v in parts.values())
    rng = [p for v in parts.values() if v[0] != 9 for _, _, p, _ in v[2]]
    print(f"{fname:32} {info['bars']} bar(s)  {n:3} notes  {', '.join(parts)}" +
          (f"  [{nm(min(rng))}-{nm(max(rng))}]" if rng else ""))

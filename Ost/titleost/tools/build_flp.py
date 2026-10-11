# Arrange the motifs into the main-menu theme and write it as an FL Studio 2024 project.
#   python tools/build_flp.py            -> arrangement.json + titleost.flp
# The .flp writer is the one ost_remake was built with (cmaket/ost_remake/tools/flpwrite.py): it splices globals and
# mixer from one of your FL 24 projects and copies instrument channels out of your own saves, so FL opens the result
# as if you had made it. Re-running overwrites titleost.flp; save your edits in FL under another name first.
#
# Form, tempo, key plan and textures follow the TH17 title theme, 「物言わぬ獣の霊」 (Wily Beast and Weakest Creature).
# Measured from the soundtrack FLAC: one pass is exactly 41 bars at 160 BPM (61.52 s), and the whole pass loops,
# intro included:
#    1-4   intro over an E pedal          5-16  theme A in A minor, a 4-bar phrase x3, layers entering
#   17-20  pivot on G# (V of C#m)        21-24  build on A major (VI of C#m)
#   25-41  theme B in C# minor, a major third above theme A: the climax
# The melodies are all IndiHome / kart; what comes from ZUN is the form, the key plan, the 16th-hat drum groove,
# the guitar dyads on the beat, strings entering on the 9th bar of theme A and the lead/pad intro.
import json, sys
from pathlib import Path

FLP_TOOLS = r"D:/Profile/source/repos/cmaket/ost_remake/tools"
sys.path.insert(0, FLP_TOOLS)
from flpwrite import build

ROOT = Path(__file__).resolve().parent.parent
M = json.load(open(ROOT / "motifs" / "motifs.json", encoding="utf-8"))
BPM, BARS = 160, 41
LOOP_FROM = 1          # like the reference, the whole pass loops; bar 41 leads back into the intro's E pedal

def motif(name): return next(v for k, v in M.items() if k[3:] == name)["parts"]

PARTS = ("Lead", "Brass", "Piano", "Celeste", "Guitar", "Kart Riff", "Chords", "Strings", "Bass", "Drums",
         "Badass Guitar", "Slonik", "Gachimouse", "Vitec Choir", "Vit Zdorovo", "Vit Don", "Vit Oi", "Vit Stat")
TRACKS = {p: [] for p in PARTS}
def at(bar, step=0.0): return (bar - 1) * 4 + step / 4
def put(part, notes, bar, beat=0.0, bars=None, vel=None, transpose=0, octaves=(0,), remap=None):
    """Place motif notes at (bar, beat). `bars` keeps the motif's first n bars, `octaves` stacks copies,
    `remap` swaps pitch classes ({0: 1} turns every C into C#)."""
    base = at(bar) + beat
    for s, d, p, v in notes:
        if bars is not None and s >= bars * 4 - 1e-6: continue
        if remap and p % 12 in remap: p += remap[p % 12] - p % 12
        for o in octaves:
            TRACKS[part].append([round(base + s, 6), d, p + transpose + 12 * o, vel if vel is not None else v])
def hit(part, bar, step, length_steps, pitches, vel):
    for p in pitches: TRACKS[part].append([round(at(bar, step), 6), length_steps / 4, p, vel])

LEAD, HOOK = motif("IndiHome Riff Lead")["Riff Lead"], motif("IndiHome Hook")["Hook"]
BASS, CHORDS = motif("IndiHome Bass Riff")["Bass Riff"], motif("IndiHome Offbeat Chords")["Offbeat Chords"]
ROLL, BREAK = motif("IndiHome Snare Roll")["Drums"], motif("IndiHome Break Bar")
FALL = motif("IndiHome String Fall")["Strings"]
KART = motif("Kart Riff (in A)")["Kart Riff"]
FALL_AT = min(s for s, d, p, v in BREAK["Strings"] if s >= 3.1)

UP = 4                                        # theme B / pivot: a major third above theme A (A minor -> C# minor)
KICK, SNARE, PHAT, CRASH = 36, 40, 44, 49     # GM: kick, snare, pedal hi-hat, crash

# ---------------------------------------------------------------- drums
def groove(first, last):
    """ZUN's 2-bar groove from the reference: pedal hat on every 16th except the backbeats, snare on 2 and 4,
    a busy syncopated kick that differs between the two bars."""
    kicks = ([0, 6, 7, 8, 15], [0, 3, 7, 8, 9, 10, 14, 15])
    for b in range(first, last + 1):
        for st in range(16):
            if st in (4, 12): hit("Drums", b, st, 1, [SNARE], 100)
            else: hit("Drums", b, st, 1, [PHAT], 62 + 14 * (st % 4 == 0))
        for st in kicks[(b - first) % 2]: hit("Drums", b, st, 1, [KICK], 105)
def fill(bar):
    """Groove for two beats, then 16th snares rising into the next downbeat."""
    for st in (0, 3): hit("Drums", bar, st, 1, [KICK], 105)
    for st in range(0, 8): hit("Drums", bar, st, 1, [PHAT], 62 + 14 * (st % 4 == 0))
    hit("Drums", bar, 4, 1, [SNARE], 100)
    for st in range(8, 16): hit("Drums", bar, st, 1, [SNARE], 70 + 5 * (st - 8))
def crash(bar): hit("Drums", bar, 0, 8, [CRASH], 105)

# ---------------------------------------------------------------- 1-4 intro: E pedal, the hook previewed on piano
for b in range(1, 5):
    hit("Bass", b, 0, 16, [40], 90)                                   # E2
    hit("Celeste", b, 0, 16, [64, 72], 70)                            # E4 + C5, the reference's intro pad notes
for b in (1, 2, 3):
    for st, p, v in ((0, KICK, 100), (6, SNARE, 85), (8, KICK, 100), (12, SNARE, 95)): hit("Drums", b, st, 1, [p], v)
put("Drums", ROLL, 4)
put("Piano", HOOK, 1, vel=72)

# ---------------------------------------------------------------- 5-16 theme A, A minor: the riff lead x3
crash(5)
for b in (5, 9, 13): put("Lead", LEAD, b, vel=90, octaves=(0, 1))
for b in range(5, 17, 2): put("Bass", BASS, b)
for b in range(5, 17):
    # the reference's guitar figure: a dyad on every beat, i i VI V. The V is an open fifth (E+B), not E+G#,
    # because the riff lead sings G natural on beat 4.
    for st, ps in ((0, [48, 52]), (4, [48, 52]), (8, [48, 53]), (12, [52, 59])): hit("Guitar", b, st, 2.5, ps, 92)
for b in range(9, 17): hit("Strings", b, 0, 16, [52, 57, 60], 72)      # strings enter on theme A's 9th bar
for b in range(13, 17): put("Chords", CHORDS, b, vel=78)
groove(5, 15); fill(16)

# ---------------------------------------------------------------- 17-20 pivot on G#: the kart riff, moved into C#
put("Kart Riff", KART, 17, transpose=UP, vel=95)
for b in range(17, 21):
    for st in range(0, 16, 2): hit("Bass", b, st, 2, [44 if st % 4 == 0 else 56], 95)   # G#2 / G#3 eighths
    hit("Strings", b, 0, 16, [44, 51], 75)                                              # G#2 + D#3
groove(17, 19); fill(20)

# ---------------------------------------------------------------- 21-24 build on A major (VI of C#m)
MAJOR = {0: 1, 7: 8, 5: 6}                                  # C->C#, G->G#, F->F#: the riff lead turned major
put("Lead", LEAD, 21, bars=2, vel=88, octaves=(0, 1), remap=MAJOR)
put("Lead", LEAD, 23, bars=2, vel=96, octaves=(1,), remap=MAJOR)   # second time an octave up, not doubled
for b in range(21, 25):
    for st in range(0, 16, 2): hit("Bass", b, st, 2, [33 if st % 4 == 0 else 45], 98)   # A1 / A2 eighths
    hit("Strings", b, 0, 16, [45, 52, 57, 61], 66 + 8 * (b - 21))                       # A major, swelling
    put("Chords", CHORDS, b, vel=76, remap={0: 1})                                      # C#5 E5 A5
for b in (21, 22, 23):                                       # half-time: the drop before the climax
    for st in range(0, 16, 2): hit("Drums", b, st, 1, [PHAT], 70)
    for st, p in ((0, KICK), (8, KICK), (12, SNARE)): hit("Drums", b, st, 1, [p], 100)
put("Drums", ROLL, 24)
put("Strings", FALL, 24, beat=FALL_AT, vel=80, transpose=UP)

# ---------------------------------------------------------------- 25-40 theme B, C# minor: the hook x4 on brass
for b in (25, 29, 33, 37): put("Brass", HOOK, b, transpose=UP, vel=100)
for b in (33, 37): put("Lead", HOOK, b, transpose=UP, vel=80, octaves=(1,))   # the lead doubles it an octave up
for b in range(25, 41, 2): put("Bass", BASS, b, transpose=UP)
for b in range(25, 41):
    put("Chords", CHORDS, b, transpose=UP, vel=78)
    for st, ps in ((0, [52, 56]), (4, [52, 56]), (8, [52, 57]), (12, [56, 63])): hit("Guitar", b, st, 2.5, ps, 95)
    hit("Strings", b, 0, 16, [52, 56, 61] + ([64, 68, 73] if b >= 33 else []), 78)
crash(25); crash(33)
groove(25, 31); fill(32); groove(33, 39)
put("Drums", ROLL, 40)

# ---------------------------------------------------------------- badass guitar: palm-muted power chords
# Root + fifth + octave, low. Muted chugs are short (under a 16th) and softer, accents ring a little and hit harder.
A5, E5, F5, GS5, CS5 = [45, 52, 57], [40, 47, 52], [41, 48, 53], [44, 51, 56], [37, 44, 49]
def chug(bar, chord, steps, accents=(0, 8)):
    for st in steps: hit("Badass Guitar", bar, st, 1.6 if st in accents else 0.8, chord, 112 if st in accents else 88)
GALLOP = (0, 2, 3, 4, 6, 7, 8, 10, 11, 12, 14, 15)          # 8th + two 16ths, the metal gallop
for b in range(13, 16): chug(b, A5, GALLOP)
chug(16, A5, (0, 2, 3, 4, 6, 7)); chug(16, F5, (8, 10, 11), (8,)); chug(16, E5, (12, 13, 14, 15), (12,))   # i VI V
for b in range(17, 21): chug(b, GS5, range(0, 16, 2), (0, 6, 12))                                         # pivot
for b in (21, 22): hit("Badass Guitar", b, 0, 12, A5, 108)                                                # build: ring out
for b in (23, 24): chug(b, A5, range(16), (0, 4, 8, 12))                                                  # ...then 16ths
for b in range(25, 41):     # theme B follows the dyad guitar's i i VI V
    chug(b, CS5, (0, 2, 3, 4, 6, 7)); chug(b, A5, (8, 10, 11), (8,)); chug(b, GS5, (12, 14, 15), (12,))
hit("Badass Guitar", 41, 0, 16, CS5, 115)

# ---------------------------------------------------------------- green slonik: the song's held note as a choir
# samples/slonik_vox.wav is exactly one bar long at 160 BPM, so every note is a whole note. Voicings stay within
# a fifth or so of the sample's own F#3, where the voice still sounds like itself.
SLONIK_ROOT = int(dict(l.split() for l in open(ROOT / "samples" / "roots.txt"))["slonik_vox"])
for b in range(17, 21): hit("Slonik", b, 0, 16, [56], 80)                                   # G#3: eerie, alone
for b in range(21, 25): hit("Slonik", b, 0, 16, [52, 57, 61], 70 + 6 * (b - 21))           # A major, swelling
for b in range(25, 41): hit("Slonik", b, 0, 16, [52, 56, 61], 84)                          # C#m under the hook
hit("Slonik", 41, 0, 16, [49, 56, 61], 95)

# ---------------------------------------------------------------- gachimouse: the one-shot as a percussion accent
# Played at C5, the sample's own pitch, except where a pitch is given. Mostly on the "and" of beat 4 at a phrase end.
SEC = 60 / BPM / 4                                           # one 16th step, in seconds
C5 = 60
def oneshot(part, bar, step, seconds, vel=100, key=C5): hit(part, bar, step, seconds / SEC, [key], vel)
GACHI = 0.45
oneshot("Gachimouse", 4, 15, GACHI)
for b in (8, 12): oneshot("Gachimouse", b, 14, GACHI)
oneshot("Gachimouse", 20, 6, GACHI)
for st, k in ((0, C5), (4, C5 + 4), (8, C5 + 7)): oneshot("Gachimouse", 24, st, GACHI, 105, k)   # rising, into the climax
for b in (28, 32, 36, 40): oneshot("Gachimouse", b, 14, GACHI)
oneshot("Gachimouse", 41, 8, GACHI, 105); oneshot("Gachimouse", 41, 10, GACHI, 105, C5 - 5)

# ---------------------------------------------------------------- D:\samples\vitec
VITEC_ROOT = int(dict(l.split() for l in open(ROOT / "samples" / "roots.txt"))["vitec_vox"])
for b in range(13, 17): hit("Vitec Choir", b, 0, 16, [57, 60, 64], 78)     # held sung note as an Am choir, theme A's last phrase
# "...только вверх, будет здорово x3" over the build, placed so the last "здорово!" (sound ends 6.69 s in) stops
# exactly on the downbeat of bar 25, where the climax starts.
zd_start = at(25) * 4 * SEC - 6.69
TRACKS["Vit Zdorovo"].append([round(zd_start / (4 * SEC), 6), 6.80 / (4 * SEC), C5, 105])
for b in (25, 29, 37): oneshot("Vit Don", b, 0, 0.49, 110)                 # "Дон." on each hook statement's downbeat...
oneshot("Vit Stat", 33, 0, 2.10, 110)                                       # ...except bar 33: "Staaaaaat" with the crash
oneshot("Vit Oi", 4, 0, 0.87, 105)                                          # "Ой, хорошо!" into theme A

# ---------------------------------------------------------------- 41 break bar, then back to the intro
put("Drums", BREAK["Drums"], 41)
put("Strings", BREAK["Strings"], 41, vel=78, transpose=UP)
put("Lead", BREAK["Riff Lead"], 41, vel=90, transpose=UP, octaves=(0, 1))

# The FLEX "Drum Standard Kit" is GM-mapped; key 36 is the kick ost_remake already uses, the source's 35 is swapped
# for it so the kick is the one known to sound.
TRACKS["Drums"] = [[s, d, 36 if p == 35 else p, v] for s, d, p, v in TRACKS["Drums"]]

arr = dict(bpm=BPM, end_beats=BARS * 4, loop_from_bar=LOOP_FROM,
           tracks={k: sorted(v) for k, v in TRACKS.items()})
json.dump(arr, open(ROOT / "arrangement.json", "w"), indent=0)

# ---------------------------------------------------------------- channels: FLEX GM presets out of your own saves
IH = "Backup/untitled (autosaved at 13h58).flp"     # the IndiHome MIDI imported onto FLEX GM presets
TH = "Backup/untitled (autosaved at 17h01).flp"     # the TH17 title-theme MIDI imported onto FLEX GM presets
SQ = "Backup/untitled (autosaved at 12h06)_2.flp"   # FLEX Square Wave, ost_remake's Kart Riff sound
BR = "Backup/untitled (autosaved at 20h26).flp"     # FLEX Synth Brass 1, ost_remake's Stabs sound
CH = [
    dict(part="Lead", name="Lead (Synth Bass 2)", kind="block", src=IH, idx=6, color=0x3AA0E0, insert=1),
    dict(part="Brass", name="Brass (Synth Brass 1)", kind="block", src=BR, idx=5, color=0x2A70F0, insert=2),
    dict(part="Piano", name="Piano (Bright)", kind="block", src=TH, idx=3, color=0xE0E0E0, insert=3),
    dict(part="Celeste", name="Pad (Celeste)", kind="block", src=TH, idx=10, color=0xF0D0A0, insert=4),
    dict(part="Guitar", name="Guitar (Overdriven)", kind="block", src=TH, idx=9, color=0x3A3AA0, insert=5),
    dict(part="Kart Riff", name="Kart Riff", kind="block", src=SQ, idx=12, color=0xE0C020, insert=6),
    dict(part="Chords", name="Chords (E.Grand)", kind="block", src=IH, idx=9, color=0x5AC85A, insert=7),
    dict(part="Strings", name="Strings", kind="block", src=IH, idx=8, color=0xC85AB4, insert=8),
    dict(part="Bass", name="Bass (Finger)", kind="block", src=IH, idx=5, color=0x3A5AC8, insert=9),
    dict(part="Drums", name="Drums (Std Kit)", kind="block", src=IH, idx=7, color=0x3A3AC8, insert=10),
    dict(part="Badass Guitar", name="Badass Guitar", kind="block", src=TH, idx=8, color=0x1A1AE0, insert=11),
    dict(part="Slonik", name="Green Slonik", kind="sampler", sample=str(ROOT / "samples" / "slonik_vox.wav"),
         root=SLONIK_ROOT, color=0x30C030, insert=12),
    dict(part="Gachimouse", name="Gachimouse", kind="sampler", sample=str(ROOT / "samples" / "gachimouse.wav"),
         root=C5, color=0x8040C0, insert=13),
    dict(part="Vitec Choir", name="Vitec Choir", kind="sampler", sample=str(ROOT / "samples" / "vitec_vox.wav"),
         root=VITEC_ROOT, color=0x40A0A0, insert=14),
    dict(part="Vit Zdorovo", name="Vitec: будет здорово", kind="sampler", sample=str(ROOT / "samples" / "vit_zdorovo.wav"),
         root=C5, color=0x30B0B0, insert=15),
    dict(part="Vit Don", name="Vitec: Дон", kind="sampler", sample=str(ROOT / "samples" / "vit_don.wav"),
         root=C5, color=0x30B0B0, insert=16),
    dict(part="Vit Oi", name="Vitec: Ой, хорошо", kind="sampler", sample=str(ROOT / "samples" / "vit_oi_horosho.wav"),
         root=C5, color=0x30B0B0, insert=17),
    dict(part="Vit Stat", name="Vitec: Staaat", kind="sampler", sample=str(ROOT / "samples" / "vit_stat.wav"),
         root=C5, color=0x30B0B0, insert=18),
]
out = ROOT / "titleost.flp"
n = build(str(ROOT / "arrangement.json"), str(out), "titleost", CH, str(ROOT) + "\\")
print(f"wrote {out} ({n} events): {BARS} bars at {BPM} BPM ({BARS * 240 / BPM:.1f} s), loop from bar {LOOP_FROM}")
for k, v in TRACKS.items(): print(f"  {k:10} {len(v):4} notes")

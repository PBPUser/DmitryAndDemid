# Arrange the motifs into the main-menu theme and write it as an FL Studio 2024 project.
#   python tools/build_flp.py            -> arrangement.json + titleost.flp
# The .flp writer is the one ost_remake was built with (cmaket/ost_remake/tools/flpwrite.py): it splices globals and
# mixer from one of your FL 24 projects and copies instrument channels out of your own saves, so FL opens the result
# as if you had made it. Re-running overwrites titleost.flp; save your edits in FL under another name first.
import json, sys
from pathlib import Path

FLP_TOOLS = r"D:/Profile/source/repos/cmaket/ost_remake/tools"
sys.path.insert(0, FLP_TOOLS)
from flpwrite import build

ROOT = Path(__file__).resolve().parent.parent
M = json.load(open(ROOT / "motifs" / "motifs.json", encoding="utf-8"))
BPM, BARS = 130, 36
LOOP_FROM = 5          # bar the game should loop back to; bar 36 is a Break Bar that leads into it

def motif(name): return next(v for k, v in M.items() if k[3:] == name)["parts"]

TRACKS = {p: [] for p in ("Lead", "Kart Riff", "Bass", "Chords", "Strings", "Drums")}
def put(part, notes, bar, beat=0.0, bars=None, vel=None, transpose=0, double=False):
    """Place motif notes at (bar, beat); `bars` keeps only the motif's first n bars."""
    at = (bar - 1) * 4 + beat
    for s, d, p, v in notes:
        if bars is not None and s >= bars * 4 - 1e-6: continue
        v = vel if vel is not None else v
        TRACKS[part].append([round(at + s, 6), d, p + transpose, v])
        if double: TRACKS[part].append([round(at + s, 6), d, p + transpose + 12, v])

LEAD, HOOK = motif("IndiHome Riff Lead")["Riff Lead"], motif("IndiHome Hook")["Hook"]
BASS, CHORDS = motif("IndiHome Bass Riff")["Bass Riff"], motif("IndiHome Offbeat Chords")["Offbeat Chords"]
GROOVE, ROLL = motif("IndiHome Drum Groove")["Drums"], motif("IndiHome Snare Roll")["Drums"]
BREAK, FALL = motif("IndiHome Break Bar"), motif("IndiHome String Fall")["Strings"]
KART = motif("Kart Riff (in A)")["Kart Riff"]
FALL_AT = min(s for s, d, p, v in BREAK["Strings"] if s >= 3.1)   # where the fall sits inside the break bar
CRASH = 49
LEAD_V, CHORD_V, STR_V = 90, 80, 75

def band(first, last, drums_last=None, chords=True):
    """Bass riff and off-beat chords from bar `first` to `last` inclusive, the drum groove up to `drums_last`
    (a fill bar or the intro leaves it out)."""
    drums_last = last if drums_last is None else drums_last
    for b in range(first, last + 1, 2): put("Bass", BASS, b, bars=min(2, last - b + 1))
    if chords:
        for b in range(first, last + 1): put("Chords", CHORDS, b, vel=CHORD_V)
    for b in range(first, drums_last + 1, 4): put("Drums", GROOVE, b, bars=min(4, drums_last - b + 1))

def break_bar(bar):
    put("Drums", BREAK["Drums"], bar)
    put("Strings", BREAK["Strings"], bar, vel=STR_V)
    put("Lead", BREAK["Riff Lead"], bar, vel=LEAD_V, double=True)

def pedal(first, last):
    """Strings holding A2 + A3 a bar at a time; an A octave is the only thing that sits under the kart's chromatics
    and the hook's F-over-Am appoggiaturas without rubbing."""
    for b in range(first, last + 1):
        put("Strings", [[0, 4, 45, STR_V], [0, 4, 57, STR_V]], b)

# ---- 1-4 intro: bass + chords alone, the string fall drops into the verse
band(1, 4, drums_last=0)
put("Strings", FALL, 4, beat=FALL_AT, vel=STR_V)

# ---- 5-12 verse: the source's bars 1-8 (7 bars of riff, then the break)
put("Drums", [[0, 4, CRASH, 100]], 5)
put("Lead", LEAD, 5, vel=LEAD_V, double=True)
put("Lead", LEAD, 9, bars=3, vel=LEAD_V, double=True)
band(5, 11)
break_bar(12)

# ---- 13-20 hook: the source's bars 9-16 (the jingle twice, snare roll in the last bar)
put("Drums", [[0, 4, CRASH, 100]], 13)
put("Lead", HOOK, 13, vel=LEAD_V, double=True)
put("Lead", HOOK, 17, vel=LEAD_V, double=True)
band(13, 20, drums_last=19)
put("Drums", ROLL, 20)

# ---- 21-28 kart: the riff twice over the bass, the groove and an A pedal; no chords (Am stabs fight the chromatics)
put("Drums", [[0, 4, CRASH, 100]], 21)
put("Kart Riff", KART, 21)
put("Kart Riff", KART, 25)
band(21, 28, drums_last=27, chords=False)
pedal(21, 28)
put("Drums", ROLL, 28)

# ---- 29-36 hook reprise: hook + pedal, 7 bars, then the break bar that loops back to bar 5
put("Drums", [[0, 4, CRASH, 100]], 29)
put("Lead", HOOK, 29, vel=LEAD_V, double=True)
put("Lead", HOOK, 33, bars=3, vel=LEAD_V, double=True)
band(29, 35)
pedal(29, 35)
break_bar(36)

# The FLEX "Drum Standard Kit" is GM-mapped; key 36 is the kick ost_remake already uses, the source's 35 is swapped
# for it so the kick is the one known to sound.
TRACKS["Drums"] = [[s, d, 36 if p == 35 else p, v] for s, d, p, v in TRACKS["Drums"]]

arr = dict(bpm=BPM, end_beats=BARS * 4, loop_from_bar=LOOP_FROM,
           tracks={k: sorted(v) for k, v in TRACKS.items()})
json.dump(arr, open(ROOT / "arrangement.json", "w"), indent=0)

# ---- channels: FLEX GM presets out of your own saves
IH = "Backup/untitled (autosaved at 13h58).flp"     # the IndiHome MIDI imported onto FLEX GM presets
SQ = "Backup/untitled (autosaved at 12h06)_2.flp"   # FLEX Square Wave, ost_remake's Kart Riff sound
CH = [
    dict(part="Lead", name="Lead (Synth Bass 2)", kind="block", src=IH, idx=6, color=0x3AA0E0, insert=1),
    dict(part="Kart Riff", name="Kart Riff", kind="block", src=SQ, idx=12, color=0xE0C020, insert=2),
    dict(part="Bass", name="Bass (Finger)", kind="block", src=IH, idx=5, color=0x3A5AC8, insert=3),
    dict(part="Chords", name="Chords (E.Grand)", kind="block", src=IH, idx=9, color=0x5AC85A, insert=4),
    dict(part="Strings", name="Strings", kind="block", src=IH, idx=8, color=0xC85AB4, insert=5),
    dict(part="Drums", name="Drums (Std Kit)", kind="block", src=IH, idx=7, color=0x3A3AC8, insert=6),
]
out = ROOT / "titleost.flp"
n = build(str(ROOT / "arrangement.json"), str(out), "titleost", CH, str(ROOT) + "\\")
print(f"wrote {out} ({n} events): {BARS} bars at {BPM} BPM, loop from bar {LOOP_FROM}")
for k, v in TRACKS.items(): print(f"  {k:10} {len(v):4} notes")

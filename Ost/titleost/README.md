# titleost — main menu theme (work in progress)

The main menu track: `titleost` in the game, "No. 0 Магия Индийского Интернет-Провайдера".
Step 1 is a box of motifs ("patches") cut from the two source tunes, so the arrangement can be built in FL Studio from them.

## Sources
- `sources/indihome.mid`: IndiHome "Paket Phoenix", **130 BPM, A minor**, 17 bars, a single Am chord the whole way.
- `sources/super-akob-kart.mid`: the kart riff, **135 BPM**, a 4-bar descending chromatic line in 8ths. It is the same file
  as `D:\Profile\source\repos\cmaket\ost_remake\samples\super-akob-kart.mid`.

IndiHome's MIDI starts one beat early: its bar 1 beat 1 is a triangle pickup, and the song really starts on beat 2.
Every bar number here uses the corrected grid, so the verse is bars 1–8 and the hook bars 9–16.

## Motifs (`motifs/`)
| File | Bars | From | What it is |
|---|---|---|---|
| 01 IndiHome Riff Lead | 4 | 1–4 | verse lead, the A–C–C–C–A–A–G–E cell twice; bar 4 turnaround A A A C E D C B |
| 02 IndiHome Hook | 4 | 9–12 | the jingle melody: a 2-bar call, then an open answer (…F E E D) and a closed answer (…F F E D C) |
| 03 IndiHome Bass Riff | 2 | 1–2 | A . C . C D E \| A . C . C E, the same two bars under the whole song |
| 04 IndiHome Offbeat Chords | 1 | 2 | Am (C5 E5 A5) on every off-beat 8th |
| 05 IndiHome Drum Groove | 4 | 1–4 | kick 35 / clap 39, GM drum map on channel 10, with an extra kick on the 4-e of bar 2 |
| 06 IndiHome Break Bar | 1 | 8 | the bar between verse and hook: crash, claps, triangle pickup, string swoop and the fall |
| 07 IndiHome String Fall | 1 | 8 | only the long chromatic fall F4→F#2, a glissando written as 1/96-beat notes, as a one-shot |
| 08 IndiHome Snare Roll | 1 | 16 | 8 × 16ths + 4 × 32nds on snare 40, snapped to the grid (the source plays each hit 1/24 beat early) |
| 09 IndiHome Ending Hit | 1 | 17 | crash + low A |
| 10 Kart Riff (original) | 4 | — | as written, 135 BPM |
| 11 Kart Riff (in A) | 4 | — | −3 semitones, the same shift `ost_remake` uses, so it sits in IndiHome's A; tempo set to 130 |

- The IndiHome lead is doubled an octave up in the source; the motifs keep only the lower line.
- Velocities are normalised: the loudest note in each motif is 100 (110 for drums). The source sat at 39–68.
- Each `.mid` has one track per part, with the source's GM program and channel.
- `motifs/motifs.json` holds the same notes in the `akob_notes.json` format (`[start_beat, dur_beat, pitch, velocity]`)
  for patch scripts like the ones in `cmaket/ost_remake/tools/`.

## Using them in FL Studio 2024
Drag a motif `.mid` from the browser onto a channel in the Channel rack to load it into that channel's piano roll for the
current pattern. Or use the piano roll's *File → Import MIDI file*. The files are 384 PPQ. FL converts them to the
project's timebase, and only the String Fall needs more than 96: if its notes smear together, raise
*Options → Project general settings → Timebase (PPQ)* to 384.

## Reference: TH17 title theme
The form follows 「物言わぬ獣の霊」, the title theme of TH17 *Wily Beast and Weakest Creature*. It was measured from
the soundtrack FLAC (`T:\TLMC v6\…\(01) [ZUN] 物言わぬ獣の霊.flac`) and read from the two MIDIs in Downloads
(`物言わぬ獣の霊.mid`, `物言わぬ獣の霊 2.mid`):
- **160 BPM.** One pass is exactly **41 bars (61.5 s)**, and the whole pass loops, intro included: twice through, then a fade.
- **Key plan:** an intro over an E pedal (V of A minor), then theme A in A minor. A pivot on G♯ and a build on A major
  move the song **a major third up** to C♯ minor, where theme B is the climax.
- **Textures borrowed:**
  - drums: the 16th pedal-hat groove with a busy, syncopated kick
  - guitar: dyads on every beat (i i VI V)
  - strings: entering on the 9th bar of theme A
  - intro: a held E4/C5 pad under the lead
  - instruments: the FLEX presets you picked when you imported the MIDI into FL (the `17h01` autosave)

None of ZUN's melodies are used. Every tune is IndiHome or the kart riff.

## The project: `titleost.flp`
41 bars at 160 BPM (61.5 s), the same length as one pass of the reference. The game should **loop the whole track
from bar 1**. Bar 41 is the Break Bar, and its string fall leads back into the intro's E pedal.

| Bars | Key | Section | What plays |
|---|---|---|---|
| 1–4 | over E | Intro | the IndiHome hook previewed softly on piano; E2 bass pedal, E4/C5 Celeste pad, half-time drums, snare roll in bar 4 |
| 5–16 | A minor | Theme A | the riff lead ×3 (in octaves) on ZUN's groove, with the bass riff and guitar dyads; strings join at bar 9, off-beat chords at bar 13; fill in bar 16 |
| 17–20 | over G♯ | Pivot | the kart riff, moved up a major third to sit in C♯ (from the A version); G♯ bass eighths, G♯/D♯ strings |
| 21–24 | A major | Build | the riff lead turned major (C→C♯, G→G♯, F→F♯), the second time an octave up; A-major chords, swelling strings, half-time drums, snare roll and the string fall into… |
| 25–40 | C♯ minor | Theme B | **the climax**: the IndiHome hook ×4, a major third up, on Synth Brass (ZUN's trumpet role), with bass riff, chords and guitar all transposed. From bar 33 the lead doubles the hook an octave up and the strings double too |
| 41 | C♯ minor | Break | the Break Bar → loop to bar 1 |

Eighteen channels, each on its own mixer insert (1–18). All are FLEX General MIDI presets copied out of your own saves:

| Channel | Preset | Copied from |
|---|---|---|
| Lead | Synth Bass 2 | IndiHome import (`13h58`) |
| Brass | Synth Brass 1 | `20h26`, `ost_remake`'s Stabs sound |
| Piano | Piano Bright | TH17 import (`17h01`) |
| Pad | Celeste | TH17 import (`17h01`) |
| Guitar | Overdriven Guitar | TH17 import (`17h01`) |
| Kart Riff | Square Wave | `12h06_2`, `ost_remake`'s Kart Riff sound |
| Chords | Electric Grand Piano | IndiHome import (`13h58`) |
| Strings | Strings 1 | IndiHome import (`13h58`) |
| Bass | Electric Finger Bass | IndiHome import (`13h58`) |
| Drums | Drum Standard Kit | IndiHome import (`13h58`) |
| Badass Guitar | Distortion Guitar | TH17 import (`17h01`) |
| Green Slonik | Sampler: `samples/slonik_vox.wav` | made by `tools/prep_instruments.py` |
| Gachimouse | Sampler: `samples/gachimouse.wav` | `D:\files-of-user\Music\пукнул_гачимаус.ogg` |
| Vitec Choir | Sampler: `samples/vitec_vox.wav` | `D:\samples\vitec` |
| Vitec: будет здорово / Дон / Ой, хорошо / Staaat | Samplers: `samples/vit_*.wav` | `D:\samples\vitec` |

- **Badass Guitar:** low palm-muted power chords (root + fifth + octave). Muted chugs are short and soft; accents
  ring and hit harder.
  - Theme A (from bar 13): an A5 gallop (8th + two 16ths), with i–VI–V in bar 16.
  - Pivot: G♯5 8ths.
  - Build: A5 rings out, then 16ths.
  - Theme B: the gallop through C♯5 → A5 → G♯5, following the dyad guitar's i i VI V.
  - Bar 41: one big ringing C♯5.
- **Green Slonik:** the steadiest held note of the Green Elephant song
  (`D:\Profile\DWN\y2mate.com - Зеленый слоник песня.mp3`, 64.8 s in), tuned to F♯3 and stretched to 1.5 s.
  That is one bar at 160 BPM, so it is played as a choir of whole notes:
  - Pivot: a lone G♯3.
  - Build: A major, swelling.
  - Theme B: C♯m (E3 G♯3 C♯4) under the hook, and a final chord in bar 41.

  Voicings stay within a fifth or so of the sample's own pitch, where the voice still sounds like itself.
- **Gachimouse:** the 0.2 s unpitched hit from the clip your `17h28` project kept among its 808 drums, trimmed to
  0.45 s and played as a percussion accent at C5 (its own pitch).
  - Phrase ends on the "and" of beat 4: bars 4, 8, 12 and 28/32/36/40.
  - Bar 20's fill, and a rising triple (C5, E5, G5) through bar 24 into the climax.
  - Two hits in the break bar.
- **Vitec** (`D:\samples\vitec`): six voice messages, transcribed with Whisper for word timings. Picked for what works in
  the track, with the explicit lines left out.
  - *Vitec Choir:* the steadiest held sung note of the 24 s clip (12.3 s in), tuned to E4 and stretched to one bar.
    It sings A-minor whole notes in theme A's last phrase (bars 13–16).
  - *будет здорово:* "…только вверх, будет здорово, будет здорово, будет здорово!" runs over the build. It is placed so the
    last "здорово!" ends exactly on the bar-25 downbeat, where the climax starts.
  - *Дон:* the isolated third "Дон." from the 15 s clip, on the downbeats of the hook statements in bars 25, 29 and 37.
  - *Staaat:* the 2 s "Staaaaaat" shout from the whispered clip, on bar 33 with the crash, where the hook doubles up.
  - *Ой, хорошо:* on bar 4's downbeat, into theme A. It is cut before the word that follows it.
- **The Telecaster take was not used** (`Telegram Desktop/tele1caster slonik.wav`). It is fast playing: 228 onsets in 46 s,
  and even its few long hits read as clashing note clusters, not clean chords, so a tuned chop of it would fight the
  harmony. Badass Guitar is the Distortion preset playing a written part instead.
- **Drums:** the TH17 import's "Batery electronic" channel actually loaded a piano preset, so the drums stay on the
  IndiHome kit (GM: kick 36, snare 40, pedal hat 44, crash 49).
- **Layout:** one pattern per part, each placed as a single 41-bar clip.
- **First version:** the 130 BPM IndiHome-only arrangement is kept in `backups/`, with its arrangement and its build script.

## Regenerating
```
python tools/split_motifs.py     # sources/*.mid -> motifs/
python tools/prep_instruments.py # slonik song, gachimouse, vitec clips -> samples/*.wav + samples/roots.txt
python tools/build_flp.py        # motifs/ -> arrangement.json + titleost.flp
```
- Motif boundaries, transposition and velocity scaling are at the top of `split_motifs.py`.
- The arrangement is in `build_flp.py`. It uses the FLP writer from `cmaket/ost_remake/tools/flpwrite.py`, and
  the channels come from the `FL Studio/Projects/Backup/` autosaves listed above.
- **`build_flp.py` overwrites `titleost.flp`.** Once you start editing in FL, save under another name or stop re-running it.

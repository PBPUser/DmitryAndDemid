Header:
[0x0] Bit mask{
    [0x01] Is Player's dialog
    [0x02] Switch reaction
    [0x04] Switch Music
    [0x08] Show Name
    [0x10] Unskippable (no press-to-advance, no hold-to-skip)
}
[0x1] Player ID
[0x2] Reaction ID
[0x3] Music ID
Structure:
HEADER
TEXT
CHARACTER_TEXTURE
EMOTION           — one symbol (UTF-8 string) from Noto Sans Symbols 2, e.g. "☠"; empty = no emotion. Baked by
                    Utils/EmotionGlyph when the chapter loads and drawn on the speaker's side of the window.
                    Added after the three fields above: every .sid must be recompiled from its JSON
                    (--compile-stages), an older .sid has no string here and will not load.
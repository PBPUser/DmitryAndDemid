# Nikitos Engine Graphics Record (.negr)
# The in-project image container: written by CpuImage.Save, read by CpuImage.Load.
# This file is the specification. Change the format, change this file.
#
# Every varint here is a BitPackage unsigned varint (WriteVarULong / ReadVarULong):
# 7 bits of payload per byte, MOST significant group first, 0x80 set on every byte
# but the last. It is NOT the signed WriteVarLong encoding.

# FILE
SIGNATURE   4 bytes, ASCII "CIM1" -- the only bytes in the file that are not part of a block
BLOCK*                            -- blocks, in order, up to and including END

# BLOCK
[0x0] Type      1 byte  -- tells the reader how to read the rest, see TYPE BYTE
[0x1] Length    varint  -- payload bytes ONLY; counts neither Type nor Length itself
[...] Payload   Length bytes

# TYPE BYTE
[0x80] Required {
    set     -- a reader that does not know this Type must FAIL: the block is load-bearing
    clear   -- a reader that does not know this Type must SKIP Length bytes and continue
}
[0x7F] Id
# Length is what makes the skip possible, so it is mandatory even for a block whose payload
# has a fixed or zero size. A new optional block can therefore be added at any time and old
# readers keep working; a new required block is a format break, and old readers say so
# instead of decoding garbage.

# TYPES
# One class each in Rendering/ImageBlocks.cs, which declares its id with [ImageBlock(id)] and
# is found by reflection -- so this table and those attributes are the same list, kept honest
# by CpuImageFormatTests.BlockTypeBytes_MatchTheSpec. Nothing else maps ids to code.
[0x80] END          Id 0, required. Length is 0. Ends the block stream; bytes after it are ignored.
[0x81] RESOLUTION   Id 1, required. Exactly one, before any PIXELS_* block.
[0x82] PIXELS_RAW   Id 2, required. Pixel bytes, verbatim.
[0x83] PIXELS_RLE   Id 3, required. Pixel bytes, run-length coded -- see RLE.
[0x04] METADATA     Id 4, optional. One key/value string pair. Any count, any position.

# RESOLUTION PAYLOAD
varint Width
varint Height
# Two varints and nothing else -- 2 bytes for anything up to 127x127, 4 for anything the game
# actually draws. A reader MUST see this before any PIXELS_* block, because it is what says how
# many pixel bytes the PIXELS_* blocks have to add up to; a file that puts pixels first is
# rejected rather than guessed at. Neither dimension may exceed a signed 32-bit int, and Width *
# Height * 4 must fit in one too.
#
# There is deliberately no pixel-format field: the format has exactly one pixel layout, RGBA8888,
# stated below where the pixels are. A field would be a second place for that to be written down
# and a way for a file to claim something no encoder can produce. If a second layout is ever
# wanted, it arrives as its own new REQUIRED block -- old readers will then correctly refuse the
# files that use it, and pay nothing for the ones that do not.

# METADATA PAYLOAD
string Key      -- varint byte length, then UTF-8 (BitPackage.WriteString)
string Value

# PIXELS_RAW / PIXELS_RLE PAYLOAD
# Pixels are RGBA8888: 4 bytes per pixel, R G B A, top row first, rows tightly packed (no
# padding). That is the format's only pixel layout -- see RESOLUTION PAYLOAD.
#
# Each PIXELS_* block decodes to a run of pixel bytes. Concatenated in file order, across
# both block types, they are the whole image: exactly Width * Height * 4 bytes. Fewer or
# more than that is a corrupt file.
#
# A writer splits the image into horizontal strips on row boundaries and picks RAW or RLE
# per strip, whichever comes out smaller -- so the two types interleave freely. A reader
# does not need to know any of that: it appends whatever each block decodes to. Strip size
# is a writer's choice, not part of the format.

# RLE
# Runs count PIXELS, not bytes. A pixel is 4 bytes and never straddles a run.
[0x00-0x7F] LITERAL  (Control & 0x7F) + 1 pixels follow, uncoded          -- 1..128 pixels
[0x80-0xFF] REPEAT   (Control & 0x7F) + 2 copies of the ONE pixel that follows -- 2..129 pixels

# EXAMPLE
# A 1x1 opaque red image, 16 bytes. (One pixel codes to 5 bytes as a literal but 4 raw, so
# the writer picks PIXELS_RAW.)
43 49 4D 31     SIGNATURE "CIM1"
81              RESOLUTION
   02           Length 2
   01           Width 1
   01           Height 1
82              PIXELS_RAW
   04           Length 4
   FF 00 00 FF  one pixel: R G B A
80              END
   00           Length 0

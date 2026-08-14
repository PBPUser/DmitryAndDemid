# CPU IMAGE BLOCK FORMAT (.negr)
# The in-project image container: written by CpuImage.Save, read by CpuImage.Load.
# This file is the specification. Change the format, change this file.
#
# Every varint here is a BitPackage unsigned varint (WriteVarULong / ReadVarULong):
# 7 bits of payload per byte, MOST significant group first, 0x80 set on every byte
# but the last. It is NOT the signed WriteVarLong encoding.

# FILE
SIGNATURE   5 bytes, ASCII "NEGR1" -- the only bytes in the file that are not part of a block
BLOCK*                             -- blocks, in order, up to and including END

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
# Ids 0x00-0x0F are the file's own structure. Ids 0x10 and up are TILE ENCODINGS: ways of
# spelling one 16x16 patch of the picture, all interchangeable, all decoding to the same thing.
[0x80] END          Id 0x00, required. Length is 0. Ends the block stream; bytes after it are ignored.
[0x81] RESOLUTION   Id 0x01, required. Exactly one, before any tile.
[0x82] MANIFEST     Id 0x02, required. Exactly one, before any tile.
[0x04] METADATA     Id 0x04, optional. One key/value string pair. Any count, any position.
[0x90] TILE_RAW8    Id 0x10, required. One 16x16 tile, colours written out as they are.

# RESOLUTION PAYLOAD
varint Width
varint Height
# Two varints and nothing else -- 2 bytes for anything up to 127x127, 4 for anything the game
# actually draws. A reader MUST see this before any tile, because it is what says how many
# tiles the file has to contain; a file that puts tiles first is rejected rather than guessed
# at. Neither dimension may exceed a signed 32-bit int, and Width * Height * 4 must fit in one
# too.

# MANIFEST PAYLOAD
varint Flags {
    [0x01] ALPHA_ENABLED -- pixels carry an alpha channel
}
# What is true of the whole image rather than of any one tile. A reader MUST see this before
# any tile, because ALPHA_ENABLED decides how long a tile payload is -- with alpha a pixel is
# 4 bytes, without it 3, and there is no way to size a tile without knowing which.
#
# The alpha bit is here and not in each tile deliberately: whether an image has transparency
# cannot vary from patch to patch, so paying a bit per tile for it would be a redundant copy
# of one decision -- and a copy that could disagree with itself. When it is clear, every pixel
# decodes fully opaque (A = 0xFF).
#
# Unknown flag bits are NOT an error, unlike unknown required block types. A flag cannot change
# how anything already understood is read -- that is what a new block type is for -- so an old
# reader that ignores one still decodes the file exactly right.

# METADATA PAYLOAD
string Key      -- varint byte length, then UTF-8 (BitPackage.WriteString)
string Value

# TILES
# The picture is a GRID of 16x16 tiles in row-major order: left to right, then top to bottom.
# Tiles carry no coordinates -- the Nth tile block in the file is the Nth cell of the grid --
# so the file must hold exactly ceil(Width/16) * ceil(Height/16) of them, no more and no fewer.
#
# The grid covers the image and overhangs it. A 20x20 image is 2x2 tiles; the tiles on the
# right and bottom edges are still whole 16x16 blocks, their outside-the-image pixels written
# as ZERO and DISCARDED on read. Every tile is therefore the same size, which is what lets a
# reader check a payload length against the manifest alone.
#
# Every tile encoding decodes to the same thing: 256 pixels of RGBA8888, row-major, 1024 bytes.
# So encodings mix freely within one file -- a writer picks whichever is smallest for each
# patch on its own, and a reader neither knows nor cares which it is about to get.
#
# Why 16x16 and not scanlines: a tile is a square of the picture, so whatever an encoding is
# good at (one flat colour, a handful of colours, a gradient, a repeat of the tile above) it
# can decide per patch and say so in one byte -- and no run has to survive the wrap from the
# end of one row to the start of the next.

# TILE_RAW8 PAYLOAD
# The first and simplest encoding: the colours themselves, uncompressed, one pixel after
# another in row-major order within the tile. 8 bits per channel.
#   ALPHA_ENABLED set:    R G B A per pixel -- 256 * 4 = 1024 bytes exactly
#   ALPHA_ENABLED clear:  R G B   per pixel -- 256 * 3 =  768 bytes exactly
# Any other length is a corrupt tile. This is the floor every later encoding is measured
# against: any patch can be written this way, so a writer that cannot do better always has
# this to fall back on.

# EXAMPLE
# A 1x1 opaque red image: one tile, all but one of whose 256 pixels are outside the image and
# therefore zero. 785 bytes -- a 16x16 tile is the smallest thing this format can say, so a
# 1x1 image costs the same as a 16x16 one.
4E 45 47 52 31  SIGNATURE "NEGR1"
81              RESOLUTION
   02           Length 2
   01           Width 1
   01           Height 1
82              MANIFEST
   01           Length 1
   00           Flags: 0 -- ALPHA_ENABLED clear, the one pixel is opaque
90              TILE_RAW8
   86 00        Length 768 (varint; 3 bytes per pixel, no alpha)
   FF 00 00     pixel (0,0): R G B
   00 00 00     pixel (1,0): outside the image, zero, discarded on read
   ...          the remaining 254 pixels, all zero
80              END
   00           Length 0

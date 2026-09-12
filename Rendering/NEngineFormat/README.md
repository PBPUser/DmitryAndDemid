# NEngineFormat — vendored

Not written here. This is a copy of the **NEngineFormats** repo's `NEngineFormat` library, compiled straight
into `DmitryAndDemid.Engine` rather than referenced as an assembly, so the game builds from this repo alone and
the Android project — which compiles `..\**\*.cs` rather than referencing anything — picks it up like every
other engine source file.

Upstream: `D:\Profile\source\repos\NEngineFormats`, project `NEngineFormat/NEngineFormat.csproj`.

## What is here

| Folder | What it is |
|---|---|
| `Core/` | The block framing every NEngine format is built from: `BlockBase`, the `[BlockId]` registry, its own `BitPackage` varints, and the LZMA2 wrapper. |
| `StaticIllustration/` | The AKOB static illustration (`.asi`, formerly `.akob`) — header, tiles, colour profiles, masks, palettes, layers, and the encoder/decoder. |

`QuickAudioWave/` is **deliberately not copied**. It is a different format (audio) that nothing here loads, and
`Core`/`StaticIllustration` do not reference it.

Namespaces are untouched: these types are `NEngineFormat.*`, not `DmitryAndDemid.*`. Note that
`NEngineFormat.Core.Utils.BitPackage` is a **different class** from the game's own
`DmitryAndDemid.Utils.BitPackage` (`Rendering/Utils/BitPackage.cs`) — same idea, same name, neither derived from
the other. Nothing should `using` both in one file.

## How the game reaches it

Through exactly one file: `Rendering/Data/Archive/StaticIllustrationImage.cs`, which decodes a `.asi` to a
`CpuImage`. Nothing else in the repo references a `NEngineFormat` type, which is what keeps this a vendored
dependency rather than something the game has grown into.

## Refreshing it

Re-copy `Core/` and `StaticIllustration/` from upstream (`*.cs`, no `obj/`) and rebuild. There are no local
edits to preserve — the sources are byte-identical to upstream on purpose, so that a refresh is a copy rather
than a merge. If that ever stops being true, say so here.

## The native dependency

The block region is optionally one LZMA2 stream, which `Core/Utils/Lzma2.cs` does through
`Joveler.Compression.XZ` and a native liblzma. The package is referenced by both
`Rendering/DmitryAndDemid.Engine.csproj` and `Android/DmitryAndDemid.Android.csproj`, so the managed side always
compiles; a host whose native library is missing still reads an **uncompressed** `.asi` and reports the
compressed case as a plain "write it uncompressed" error rather than a crash.

#!/usr/bin/env bash
# Assemble the game as a mono-nx (Nintendo Switch homebrew) payload.
#
# mono-nx (https://github.com/exelix11/mono-nx) runs .NET 9 managed assemblies through the Mono interpreter on
# Horizon OS. This script produces the managed-side payload it consumes: our net9 build (aag2.dll + its NuGet
# dependency DLLs) plus the game Assets. It does NOT build the mono-nx runtime/interpreter — that is a separate,
# native devkitPro build in the mono-nx repo (build_mono.sh + native/interpreter). See docs/switch-port.md.
#
# IMPORTANT — this payload will LOAD and the main loop will run (Deko3dBackend.OpenWindow only calls libnx
# symbols mono-nx ships: romfs/applet/pad/tick), but it will render NOTHING: the deko3d draw path is still a
# scaffold (all draw ops are no-ops) and mono-nx exposes no deko3d symbols yet. Its value right now is a Phase-0
# smoke/perf test: does our whole managed stack load and tick on the interpreter? See docs/switch-port.md.
#
# Usage:  Tools/stage_mononx.sh [output-dir]
#         MONO_NX_ROOT=~/path/to/mono-nx Tools/stage_mononx.sh   # also prints how to drop into its sd_files
set -euo pipefail
cd "$(dirname "$0")/.."

OUT="${1:-artifacts/mononx}"
CFG_DIR="bin/Release/net9.0"

echo "== building net9 managed assemblies (SwitchBuild) =="
dotnet build -c Release -p:SwitchBuild=true --nologo -v q

echo "== assembling payload in $OUT (mirrors the on-device /mono layout) =="
rm -rf "$OUT"
mkdir -p "$OUT/mono/lib_net9.0"

# The entry assembly sits at /mono/aag2.dll so its AppContext.BaseDirectory is /mono; the SWITCH startup hook
# anchors config.json + Assets there, so they go NEXT TO it. Dependency DLLs go in lib_net9.0, which is on the
# interpreter's assembly_dir (see sd_files/mono/config.ini). A framework-dependent build has NO System.*.dll —
# those come from mono-nx's framework_net9.0. Unused desktop backends (Raylib/Silk/Gtk) load lazily and only
# fault if their native P/Invoke runs, which the Deko3dBackend path never does.
cp "$CFG_DIR"/aag2.dll "$OUT/mono/aag2.dll"
# Ship the portable PDB next to the dll so mono can put file/line numbers in stack traces (mono-nx debugging is
# logging + traces, not an attachable debugger — see docs/switch-port.md). Harmless if the interpreter ignores it.
[ -f "$CFG_DIR/aag2.pdb" ] && cp "$CFG_DIR/aag2.pdb" "$OUT/mono/aag2.pdb"
cp config.json "$OUT/mono/config.json"
cp -a Assets "$OUT/mono/Assets"
# every dependency DLL (everything except aag2 itself) onto the assembly search path
for dll in "$CFG_DIR"/*.dll; do
  [ "$(basename "$dll")" = "aag2.dll" ] || cp "$dll" "$OUT/mono/lib_net9.0/"
done

echo "== payload assembled =="
echo "  entry    : $OUT/mono/aag2.dll  (+ config.json, Assets/ alongside → AppContext.BaseDirectory)"
echo "  dep dlls : $(ls "$OUT/mono/lib_net9.0"/*.dll | wc -l) in $OUT/mono/lib_net9.0/"
echo "  assets   : $(du -sh "$OUT/mono/Assets" | cut -f1)"

cat <<EOF

Next — run it on mono-nx (needs a Switch or Ryujinx; this host can't):
  1. Merge the payload into your mono-nx SD tree (it mirrors /mono exactly):
       cp -a $OUT/mono/. <mono-nx>/sd_files/mono/
  2. In sd_files/mono/config.ini set:   default_assembly = /mono/aag2.dll
     and for debugging:                 logging = true   (or file_io_redirect = /mono/log.txt)
  3. Re-copy sd_files to the SD / Ryujinx and launch mono_nx.nro.

Expected: the managed game boots and the 60 TPS loop runs; the screen stays black (no draw path yet).
That black-screen boot IS the Phase-0 result — it proves the interpreter runs our managed stack + sim.
EOF

if [ -n "${MONO_NX_ROOT:-}" ] && [ -d "$MONO_NX_ROOT/sd_files/mono" ]; then
  echo
  echo "MONO_NX_ROOT set — stage directly with:"
  echo "  cp -a $OUT/mono/. \"$MONO_NX_ROOT/sd_files/mono/\""
fi

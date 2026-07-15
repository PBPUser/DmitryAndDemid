#!/usr/bin/env bash
# Build the ARMv8 / Tegra X1 device build of the game.
#
# Target: any aarch64 Linux running on the Tegra X1 — a Nintendo Switch booted into L4T / switchroot Ubuntu,
# an Nvidia Jetson TX1, or a Shield TV under Linux. The published output is SELF-CONTAINED (the .NET 10 runtime
# ships inside it), so the device needs no dotnet install.
#
# The Raylib-cs NuGet ships a native libraylib.so for linux-x64 only, so on arm64 the game runs the
# Silk.NET/OpenGL backend, which binds the system libGL and needs no bundled native. At runtime the game
# detects arm64 and hides the Raylib option automatically (see Rendering/RendererRegistry.cs).
#
# Usage:  Tools/publish_tegra.sh [output-dir]
set -euo pipefail
cd "$(dirname "$0")/.."

OUT="${1:-publish/tegra-arm64}"

dotnet publish -c Release -r linux-arm64 --self-contained -o "$OUT"

cat <<EOF

Built ARMv8 (Tegra X1) self-contained build -> $OUT

On the device (L4T / switchroot Ubuntu / Jetson), install the runtime graphics deps once:
    sudo apt install libgl1 libglfw3            # GL + windowing for the Silk backend
    sudo apt install libgtk-3-0                 # OPTIONAL: only for the pre-launch config dialog

Then run:
    ./aag2                                       # renderer defaults to Silk on arm64
    ./aag2 --selftest                            # headless boot check (no window), prints SELFTEST OK

Notes:
  * The pre-launch GTK dialog is skipped on arm64; configure from the in-game settings screen instead.
  * config.json's renderer= is ignored if it says 'raylib' on arm64 — it falls back to Silk.
EOF

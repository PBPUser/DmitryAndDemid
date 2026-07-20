#!/usr/bin/env python3
"""
Cross-compiles the game's shaders to Metal Shading Language (MSL) for the native Metal backend
(Rendering/Metal/MetalBackend.cs, macOS/iOS).

The game authors shaders in legacy GLSL. Metal can't consume GLSL any more than Vulkan can — but the Vulkan
step (Tools/compile_shaders.py) already turns every shader into SPIR-V plus a uniform-reflection sidecar. This
tool builds ON TOP of those committed SPIR-V artifacts: SPIR-V -> MSL via SPIRV-Cross. So there is exactly one
GLSL-massaging pipeline (the Vulkan one), and Metal is a pure back-end translation of its output.

Crucially, the JSON reflection sidecars are reused UNCHANGED. SPIRV-Cross preserves the byte layout of the
generated `gl_DefaultUniformBlock`, so each uniform's recorded offset indexes into the MSL `constant` struct
exactly as it does into the Vulkan uniform buffer. MetalBackend loads the same {blockSize, uniforms[], ...}
and writes uniforms into a constant buffer at those offsets.

Inputs  (committed): Assets/Shaders/vulkan/<name>.<stage>.spv  +  <name>.<stage>.json
Outputs (committed): Assets/Shaders/metal/<name>.<stage>.metal +  <name>.<stage>.json (copied)
                     Assets/Shaders/metal/<name>.<stage>.metallib   (only when built on macOS; optional —
                     MetalBackend also compiles the .metal source at runtime via newLibraryWithSource:)

Entry point: SPIRV-Cross renames `main` to `main0` in MSL (main is reserved). MetalBackend looks up the
vertex/fragment function by that name.

Run from the repo root:  python3 Tools/compile_metal_shaders.py
Requires: spirv-cross (Khronos SPIRV-Cross). The .metallib step additionally needs Xcode's `xcrun metal`
(macOS only). Generated artifacts are committed, so players need neither tool.
First run Tools/compile_shaders.py so the Vulkan SPIR-V exists.
"""

import os
import shutil
import subprocess
import sys
from glob import glob

SRC_DIR = "Assets/Shaders/vulkan"
OUT_DIR = "Assets/Shaders/metal"


def have(tool: str) -> bool:
    return shutil.which(tool) is not None


def main() -> int:
    if not os.path.isdir(SRC_DIR):
        print(f"No {SRC_DIR}/ — run Tools/compile_shaders.py first (Metal builds on its SPIR-V).")
        return 1
    if not have("spirv-cross"):
        print("spirv-cross not found. Install Khronos SPIRV-Cross (e.g. 'pacman -S spirv-cross' / "
              "'brew install spirv-cross' / vulkan-sdk).")
        return 1

    os.makedirs(OUT_DIR, exist_ok=True)

    # macOS-only: precompile each .metal to a .metallib so the device does not compile source on first use.
    # Skipped elsewhere — MetalBackend falls back to compiling the .metal source at runtime.
    can_metallib = sys.platform == "darwin" and have("xcrun")

    spvs = sorted(glob(f"{SRC_DIR}/*.spv"))
    if not spvs:
        print(f"No .spv in {SRC_DIR} — run Tools/compile_shaders.py first.")
        return 1

    ok, failed = 0, []
    for spv in spvs:
        stem = os.path.basename(spv)[:-4]          # "outline.frag.spv" -> "outline.frag"
        metal = f"{OUT_DIR}/{stem}.metal"
        json_in = f"{SRC_DIR}/{stem}.json"

        # SPIR-V -> MSL. --msl selects the Metal backend; the default MSL version targets a broadly-supported
        # feature set. SPIRV-Cross emits `main0` as the entry point.
        result = subprocess.run(
            ["spirv-cross", "--msl", spv, "--output", metal],
            capture_output=True, text=True)
        if result.returncode != 0:
            failed.append((stem, result.stderr.strip().split("\n")[:1]))
            continue

        # Reuse the Vulkan reflection sidecar verbatim — same offsets, same block size.
        if os.path.exists(json_in):
            shutil.copyfile(json_in, f"{OUT_DIR}/{stem}.json")

        if can_metallib:
            air = f"{OUT_DIR}/{stem}.air"
            lib = f"{OUT_DIR}/{stem}.metallib"
            c1 = subprocess.run(["xcrun", "-sdk", "macosx", "metal", "-c", metal, "-o", air],
                                capture_output=True, text=True)
            if c1.returncode == 0:
                subprocess.run(["xcrun", "-sdk", "macosx", "metallib", air, "-o", lib],
                               capture_output=True, text=True)
                if os.path.exists(air):
                    os.remove(air)

        ok += 1

    print(f"MSL: {ok}/{len(spvs)} translated -> {OUT_DIR}"
          + ("  (+ .metallib)" if can_metallib else "  (.metal source only; runtime-compiled on device)"))
    for stem, err in failed:
        print(f"  FAILED {stem}: {err}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

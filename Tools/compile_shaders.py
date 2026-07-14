#!/usr/bin/env python3
"""
Compiles Assets/Shaders/*.{fs,vs} to SPIR-V for the Vulkan backend, plus a reflection sidecar.

Vulkan cannot consume the game's GLSL as written, so each shader is transformed IN MEMORY first — the
.fs/.vs assets are never modified, because the Raylib and OpenGL backends still compile them as-is:

  * gl_FragColor          (47 of 48 shaders) - removed in core GLSL; declare a real output and rebind.
  * texture2D()           (8 shaders)        - removed in core GLSL; it is texture() now.
  * a parameter named 'sampler' (2 shaders)  - reserved word in core GLSL.
  * '1000f' float literal (1 shader)         - not valid GLSL (that is C#/HLSL spelling).

Free-standing uniforms (uniform float border_width; ...) are illegal in Vulkan, so we compile with
glslang's relaxed rules (-R), which packs them into a generated 'gl_DefaultUniformBlock'. The reflection
sidecar records each uniform's byte offset in that block, which is how the backend implements
GetUniformLocation/SetShaderValue.

Outputs, per shader, into Assets/Shaders/vulkan/:
    <name>.frag.spv / <name>.vert.spv    SPIR-V module
    <name>.frag.json / <name>.vert.json  { blockSize, blockBinding, uniforms[], samplers[] }

Run from the repo root:  python3 Tools/compile_shaders.py
Requires glslangValidator (the generated artifacts are committed, so players do not need it).
"""

import json
import os
import re
import subprocess
import sys
from glob import glob

OUT_DIR = "Assets/Shaders/vulkan"

# GL type enums as reported by glslang reflection.
GL_TYPES = {
    0x1404: "int",
    0x1406: "float",
    0x8B50: "vec2",
    0x8B51: "vec3",
    0x8B52: "vec4",
    0x8B53: "ivec2",
    0x8B5E: "sampler2D",
    0x8B5C: "mat4",
}

# The vertex and fragment modules are compiled separately, so glslang numbers each stage's bindings from
# zero — the vertex default-uniform block would land on binding 0, colliding with the fragment's texture0.
# Shift the vertex block clear of the fragment's sampler range.
VERTEX_UBO_BINDING_SHIFT = "10"


# Varyings must sit at the SAME location in both stages. The stages are compiled as separate modules, so
# glslang's --auto-map-locations numbers each one independently, in declaration order — a fragment shader
# that omits 'uv' would put fragColor where the vertex shader put uv, and the varyings silently cross-wire
# (Vulkan rejects it: VUID-RuntimeSpirv-OpEntryPoint-08743). Pin them by name instead.
VARYING_LOCATIONS = {
    "fragTexCoord": 0,
    "uv": 1,
    "fragColor": 2,
}


def pin_varyings(source: str) -> str:
    for name, location in VARYING_LOCATIONS.items():
        source = re.sub(rf"^(\s*)(in|out)(\s+\w+\s+{name}\s*;)",
                        rf"\1layout(location = {location}) \2\3",
                        source, flags=re.M)
    return source


def vulkanize(source: str) -> str:
    """Make the game's legacy GLSL acceptable to a core-profile / Vulkan compiler."""
    # 330 does not allow a location qualifier on vertex outputs (that is 410+). This transform is Vulkan-only
    # and in memory, so raise the version; the Raylib/GL backends still compile the original 330 source.
    source = re.sub(r"#version\s+\d+", "#version 450", source, count=1)

    if "gl_FragColor" in source:
        lines = source.split("\n")
        after_version = next(i for i, l in enumerate(lines) if l.strip().startswith("#version")) + 1
        lines.insert(after_version, "layout(location = 0) out vec4 _fragColorOut;")
        source = "\n".join(lines)
        source = re.sub(r"\bgl_FragColor\b", "_fragColorOut", source)

        # Several shaders (boss_health_bar, ...) `return;` out of main WITHOUT writing the output, meaning
        # "do not draw this pixel". That leaves the fragment output UNDEFINED. GL/NVIDIA hands back zeros, so
        # on OpenGL it looks like a no-op; SPIR-V genuinely leaves the variable uninitialised, and the boss
        # health bar's full-screen quad then sprayed garbage over the whole gameplay overlay.
        # Zero-initialise the output so an unwritten fragment blends as fully transparent, matching GL.
        source = re.sub(r"(void\s+main\s*\([^)]*\)\s*\{)", r"\1\n    _fragColorOut = vec4(0.0);", source, count=1)

    source = re.sub(r"\btexture2D\s*\(", "texture(", source)
    source = re.sub(r"\btextureCube\s*\(", "texture(", source)
    source = re.sub(r"\bsampler\b(?!\dD)", "_smp", source)      # 'sampler' used as a parameter name
    source = re.sub(r"(?<![\w.])(\d+)f\b", r"\1.0", source)      # 1000f -> 1000.0
    return pin_varyings(source)


def parse_reflection(text: str) -> dict:
    """glslang -q output -> { blockSize, blockBinding, uniforms[], samplers[] }."""
    uniforms, samplers = [], []
    block_size, block_binding = 0, -1
    section = None

    for line in text.split("\n"):
        line = line.strip()
        if line.endswith("reflection:"):
            section = line
            continue
        if not line or ":" not in line:
            continue

        name, _, rest = line.partition(":")
        fields = dict(
            (parts[0], parts[1])
            for parts in (f.strip().split(" ", 1) for f in rest.split(","))
            if len(parts) == 2
        )

        if section == "Uniform reflection:":
            offset = int(fields.get("offset", -1))
            gl_type = int(fields.get("type", "0"), 16)
            binding = int(fields.get("binding", -1))
            if GL_TYPES.get(gl_type) == "sampler2D":
                samplers.append({"name": name.strip(), "binding": binding})
            elif offset >= 0:
                uniforms.append({
                    "name": name.strip(),
                    "offset": offset,
                    "type": GL_TYPES.get(gl_type, hex(gl_type)),
                })
        elif section == "Uniform block reflection:" and name.strip() == "gl_DefaultUniformBlock":
            block_size = int(fields.get("size", 0))
            block_binding = int(fields.get("binding", -1))

    uniforms.sort(key=lambda u: u["offset"])
    return {
        "blockSize": block_size,
        "blockBinding": block_binding,
        "uniforms": uniforms,
        "samplers": samplers,
    }


# The renderer needs a plain textured-quad shader for untextured/default draws. Raylib and OpenGL have one
# built in; Vulkan does not, so emit it here rather than adding a .fs to Assets/Shaders (which the game
# scans and would turn into a spurious entry in Runtime.Shaders).
DEFAULT_FRAGMENT = """#version 330
in vec2 fragTexCoord;
in vec4 fragColor;
uniform sampler2D texture0;
uniform vec4 colDiffuse;
out vec4 finalColor;
void main()
{
    finalColor = texture(texture0, fragTexCoord) * colDiffuse * fragColor;
}
"""


def main() -> int:
    os.makedirs(OUT_DIR, exist_ok=True)
    builtin = "Assets/Shaders/_default.fs"
    with open(builtin, "w") as f:
        f.write(DEFAULT_FRAGMENT)

    shaders = [(p, "frag") for p in sorted(glob("Assets/Shaders/*.fs"))]
    shaders += [(p, "vert") for p in sorted(glob("Assets/Shaders/*.vs"))]

    ok, failed = 0, []
    for path, stage in shaders:
        name = os.path.basename(path).rsplit(".", 1)[0]
        transformed = f"{OUT_DIR}/{name}.{stage}"
        spv = f"{OUT_DIR}/{name}.{stage}.spv"

        with open(transformed, "w") as f:
            f.write(vulkanize(open(path).read()))

        command = ["glslangValidator", "-V", "-R", "-q", "-S", stage,
                   "--auto-map-bindings", "--auto-map-locations"]
        if stage == "vert":
            command += ["--shift-UBO-binding", VERTEX_UBO_BINDING_SHIFT]
        command += [transformed, "-o", spv]
        result = subprocess.run(command, capture_output=True, text=True)

        os.remove(transformed)  # the transformed GLSL is an intermediate, not an asset

        if result.returncode != 0:
            failed.append((name, [l for l in result.stdout.split("\n") if "ERROR" in l][:1]))
            continue

        with open(f"{OUT_DIR}/{name}.{stage}.json", "w") as f:
            json.dump(parse_reflection(result.stdout), f, indent=1)
        ok += 1

    os.remove(builtin)   # it is an input to this tool, not a game asset

    print(f"SPIR-V: {ok}/{len(shaders)} compiled -> {OUT_DIR}")
    for name, err in failed:
        print(f"  FAILED {name}: {err}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

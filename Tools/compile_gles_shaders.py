#!/usr/bin/env python3
"""Produce OpenGL ES 3.0 versions of the game's shaders, for Android.

The shipped shaders are desktop GLSL (#version 330/400/410) and an ES driver rejects them outright. The
differences that actually matter here are small and mechanical:

  * the version line becomes `#version 300 es`
  * ES has no default float precision in a fragment shader — one must be declared or nothing compiles
  * `gl_FragColor` does not exist in ES 3.0; a fragment shader writes a declared `out` instead
  * `texture2D`/`textureCube` are gone, replaced by the overloaded `texture`

Everything else in these shaders (in/out varyings, `texture()`, `textureSize`) is already ES 3.0-legal.

Output goes to Assets/Shaders/gles/, mirroring the source names, and the GL backend prefers that directory
when it is running on an ES context. The desktop sources are left untouched.

Usage:  python3 Tools/compile_gles_shaders.py
"""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SOURCE_DIR = ROOT / "Assets" / "Shaders"
OUTPUT_DIR = SOURCE_DIR / "gles"

FRAGMENT_OUT = "_fragColorOut"

PRECISION = """precision highp float;
precision highp int;
precision highp sampler2D;
"""


def transform(source: str, is_fragment: bool) -> str:
    source = re.sub(r"#version[ \t]+\d+([ \t]+\w+)?", "#version 300 es", source, count=1)

    # texture2D(x, y) -> texture(x, y). ES 3.0 dropped the suffixed forms entirely.
    source = re.sub(r"\btexture2D\b", "texture", source)
    source = re.sub(r"\btextureCube\b", "texture", source)

    lines = source.splitlines()
    insert_at = next((i for i, l in enumerate(lines) if l.strip().startswith("#version")), -1) + 1

    header: list[str] = [PRECISION.rstrip()]

    uses_frag_color = is_fragment and "gl_FragColor" in source
    if uses_frag_color:
        header.append(f"out vec4 {FRAGMENT_OUT};")

    lines[insert_at:insert_at] = header
    source = "\n".join(lines)

    if uses_frag_color:
        source = source.replace("gl_FragColor", FRAGMENT_OUT)

        # A shader that returns early without ever assigning leaves the output undefined — on desktop that
        # happened to read back as the cleared value, on other drivers it is garbage (this is exactly the bug
        # that speckled the Vulkan spell cards). Zero it on entry to main.
        source = re.sub(
            r"(void\s+main\s*\(\s*\)\s*\{)",
            r"\1\n    " + FRAGMENT_OUT + " = vec4(0.0);",
            source,
            count=1,
        )

    return source + "\n"


# Bits of a line where an integer literal is genuinely an integer and must not be touched: an array or
# component index, an explicit int constructor, a texture LOD, a preprocessor line.
INT_CONTEXT = re.compile(
    r"\[[^\]]*\]"                                   # a[3], color[3]
    r"|\b(?:ivec[234]|int|uint|uvec[234])\s*\([^)]*\)"
    r"|\btextureSize\s*\([^)]*\)"
    r"|\btexelFetch\s*\([^)]*\)"
)

# An `int i = 0` / `for (int k = 0; ...)` declaration: those literals are ints on purpose.
INT_DECLARATION = re.compile(r"\b(?:int|uint)\s+\w+\s*=[^;]*")

# An integer literal: digits not already part of a float (1.0, .5, 1e3) or an identifier (vec2, x1).
INT_LITERAL = re.compile(r"(?<![\w.])(\d+)(?![\w.])")

# GLSL ES will not let a shader declare something that shadows a built-in function; desktop GLSL allows it.
# glslang names the offender, so the fix is a rename confined to the generated ES source.
REDEFINITION = re.compile(r"ERROR: \d+:\d+: '(\w+)' : redefinition")


def promote_ints_on_line(line: str) -> str:
    """Rewrite bare integer literals as floats, leaving genuine integer positions alone.

    ES 3.0 has no implicit int->float conversion, so `x - 1`, `clamp(v, 0, 1)` and `/ 9` — all of which
    desktop GLSL quietly promotes — are hard errors. Only lines glslang has actually complained about are
    passed here, so the blast radius is limited to code that already does not compile.
    """
    if line.lstrip().startswith("#"):
        return line

    # Mask the stretches where an integer is meant, promote what is left, then put the masks back.
    masked: list[str] = []

    def hide(match: re.Match[str]) -> str:
        masked.append(match.group(0))
        # The marker must not contain digits: the promotion pass below would happily rewrite them and the
        # text being protected would never come back.
        return chr(0xE000 + len(masked) - 1)

    line = INT_DECLARATION.sub(hide, line)
    line = INT_CONTEXT.sub(hide, line)
    line = INT_LITERAL.sub(lambda m: f"{m.group(1)}.0", line)
    for index, original in enumerate(masked):
        line = line.replace(chr(0xE000 + index), original)
    return line


def validate(path: pathlib.Path, source: str) -> tuple[list[int], list[str]]:
    """Compile with glslang; report the error lines and any built-ins the shader illegally redefines."""
    import subprocess
    import tempfile

    suffix = ".frag" if path.suffix == ".fs" else ".vert"
    with tempfile.NamedTemporaryFile("w", suffix=suffix, delete=False) as handle:
        handle.write(source)
        temp = handle.name
    try:
        result = subprocess.run(["glslangValidator", temp], capture_output=True, text=True)
    finally:
        pathlib.Path(temp).unlink(missing_ok=True)

    if result.returncode == 0:
        return [], []
    lines = sorted({int(m) for m in re.findall(r"ERROR: \d+:(\d+):", result.stdout)})
    return lines, REDEFINITION.findall(result.stdout)


def fix_until_clean(path: pathlib.Path, source: str, rounds: int = 200) -> tuple[str, list[int]]:
    """Promote literals on whichever lines glslang rejects, until it stops rejecting them."""
    for _ in range(rounds):
        errors, redefined = validate(path, source)
        if not errors:
            return source, []

        changed = False
        for name in redefined:
            renamed = re.sub(rf"\b{re.escape(name)}\b(?!\s*\()", f"{name}_", source)
            if renamed != source:
                source = renamed
                changed = True
        if changed:
            continue

        lines = source.splitlines()
        for number in errors:
            if not 1 <= number <= len(lines):
                continue
            fixed = promote_ints_on_line(lines[number - 1])
            if fixed != lines[number - 1]:
                lines[number - 1] = fixed
                changed = True

        if not changed:
            return source, errors           # nothing left that this transform knows how to fix
        source = "\n".join(lines) + "\n"

    return source, validate(path, source)[0]


def main() -> int:
    if not SOURCE_DIR.is_dir():
        print(f"no shader directory at {SOURCE_DIR}", file=sys.stderr)
        return 1

    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

    count = 0
    failed: list[str] = []
    for path in sorted(SOURCE_DIR.glob("*.fs")) + sorted(SOURCE_DIR.glob("*.vs")):
        converted = transform(path.read_text(), is_fragment=path.suffix == ".fs")
        converted, errors = fix_until_clean(path, converted)
        (OUTPUT_DIR / path.name).write_text(converted)
        count += 1
        if errors:
            failed.append(f"{path.name} (lines {', '.join(str(e) for e in errors)})")

    print(f"wrote {count} ES shaders to {OUTPUT_DIR.relative_to(ROOT)}")
    if failed:
        print(f"{len(failed)} still do not compile for ES 3.0:", file=sys.stderr)
        for name in failed:
            print(f"  {name}", file=sys.stderr)
        return 1
    print("all compile clean for OpenGL ES 3.0")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

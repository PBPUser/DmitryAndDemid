#!/usr/bin/env bash
# Short wrapper so you don't have to paste the long venv path (which wraps/mangles in the
# terminal). Runs the NXBT Switch driver with the venv python that has nxbt installed.
#
#   sudo Tools/nxbt.sh pair        # first time  (open Change Grip/Order on the Switch)
#   sudo Tools/nxbt.sh reconnect   # later       (uses the saved Switch address)
#
# Override the venv python with NXBT_PY=/path/to/python if the scratchpad venv is gone.
set -euo pipefail

VENV_PY="${NXBT_PY:-/tmp/claude-1000/-home-iwuwka-Documents-DmitryAndDemid/a33a3c2a-881f-4ef2-89dc-f731d8186052/scratchpad/nxbt-venv/bin/python}"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ ! -x "$VENV_PY" ]]; then
    echo "venv python not found at: $VENV_PY" >&2
    echo "Set NXBT_PY=/path/to/python (a python with nxbt installed) and retry." >&2
    exit 1
fi

# -u: unbuffered stdout/stderr, so progress shows immediately even when piped into `tee`
# (block-buffering otherwise swallows output if you Ctrl-C before the buffer flushes).
exec "$VENV_PY" -u "$REPO/Tools/nxbt_drive.py" "$@"

#!/usr/bin/env python3
"""Drive the Nintendo Switch from this PC by emulating a Bluetooth Pro Controller (NXBT).

Used to remote-control the mono-nx homebrew build of the game for testing: navigate the
menu, launch the benchmark, etc., without touching the console. NXBT presents the PC's
Bluetooth adapter to the Switch as a real Pro Controller; the game reads it via SDL like
any other pad.

Prereqs (see docs/switch-port.md):
  * Must run as ROOT (raw L2CAP sockets + BlueZ DBus).
  * NXBT installed in ../scratchpad venv, or on PATH.
  * DO NOT hand-write a bluetooth.service override: NXBT configures BlueZ itself
    (writes /run/systemd/system/bluetooth.service.d/nxbt.conf with `--compat --noplugin=*`
    and reverts it on exit). A same-named override under /etc will SHADOW NXBT's /run one
    (/etc outranks /run) and break SDP registration, so the Switch won't see the controller.
  * Needs the CLI tools NXBT shells out to: hciconfig, hcitool, sdptool (bluez-utils).

First-time pairing:
  sudo .../nxbt-venv/bin/python Tools/nxbt_drive.py pair
  -> On the Switch: Controllers > Change Grip/Order (the screen with the empty pad slots).
     Once it connects, the Switch's address is saved to Tools/.nxbt_switch_addr.

Later (Switch already knows us, faster, no Change Grip menu):
  sudo .../nxbt-venv/bin/python Tools/nxbt_drive.py reconnect

Then you're dropped into live keyboard control. Key map is printed on screen.
"""

# --- Python 3.14 fix -------------------------------------------------------------------
# NXBT was written when Linux multiprocessing defaulted to `fork`. Python 3.14 flipped the
# default to `forkserver`, which re-imports __main__ in the child and breaks NXBT's Manager
# / Process usage. Force `fork` back BEFORE importing nxbt (which spawns a Manager on init).
import multiprocessing
multiprocessing.set_start_method("fork", force=True)

import argparse
import os
import sys
import time
import threading
from pathlib import Path

try:
    from nxbt import Nxbt, PRO_CONTROLLER
except ImportError:
    sys.exit("nxbt not importable — run this with the venv python "
             "(scratchpad/nxbt-venv/bin/python) or `pip install nxbt`.")

ADDR_FILE = Path(__file__).with_name(".nxbt_switch_addr")

# Terminal-key -> controller action. Buttons map to packet booleans; sticks map to an
# (stick, axis, sign) tuple that sets X_VALUE/Y_VALUE to +/-100.
STICK_MAX = 100
BUTTON_KEYS = {
    " ": "A", "z": "A",
    "x": "B",
    "c": "X",
    "v": "Y",
    "q": "L", "e": "R",
    "1": "ZL", "3": "ZR",
    "h": "HOME", "g": "CAPTURE",
    "KEY_ENTER": "PLUS", "\r": "PLUS", "\n": "PLUS",
    "KEY_BACKSPACE": "MINUS", "-": "MINUS",
    # D-pad on the arrow keys
    "KEY_UP": "DPAD_UP", "KEY_DOWN": "DPAD_DOWN",
    "KEY_LEFT": "DPAD_LEFT", "KEY_RIGHT": "DPAD_RIGHT",
}
# Left stick on WASD, right stick on IJKL.  value = (packet_stick_key, axis, sign)
STICK_KEYS = {
    "w": ("L_STICK", "Y_VALUE", +1), "s": ("L_STICK", "Y_VALUE", -1),
    "a": ("L_STICK", "X_VALUE", -1), "d": ("L_STICK", "X_VALUE", +1),
    "i": ("R_STICK", "Y_VALUE", +1), "k": ("R_STICK", "Y_VALUE", -1),
    "j": ("R_STICK", "X_VALUE", -1), "l": ("R_STICK", "X_VALUE", +1),
}
# How long an input stays "held" after its last keypress. Terminal auto-repeat refreshes
# the timestamp faster than this, so holding a key gives continuous movement; releasing it
# lets the input decay to neutral within HOLD_DECAY seconds.
HOLD_DECAY = 0.13
SEND_HZ = 66


def make_nxbt(debug=False):
    nx = Nxbt(debug=debug)
    adapters = nx.get_available_adapters()
    if not adapters:
        sys.exit("No Bluetooth adapters found. Is bluetooth.service running and the "
                 "adapter powered? (bluetoothctl show)")
    print(f"[nxbt] adapters: {adapters}")
    return nx


def _sh(cmd):
    import subprocess
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=4)
        return (r.stdout + r.stderr).strip()
    except Exception as e:
        return f"(cmd {cmd[0]} failed: {e})"


def _adapter_snapshot():
    """Best-effort read of the adapter's advertised class/discoverable state, so we can
    tell whether it's actually presenting as a gamepad while waiting for the Switch."""
    out = _sh(["hciconfig", "hci0", "class"]).replace("\n", " ")
    d = next((l.strip() for l in _sh(["bluetoothctl", "show"]).splitlines()
              if "Discoverable:" in l), "")
    return f"{out} | {d}"


def _deep_diag():
    """Full picture used while waiting: is the HID SDP record actually registered, and is
    any Switch trying to connect? If the SDP browse shows no HID service, the Switch has
    nothing to connect to even though our class says Gamepad."""
    sdp = _sh(["sdptool", "browse", "local"])
    # Condense: just the service names + whether an HID/PnP record exists.
    names = [l.strip() for l in sdp.splitlines() if "Service Name:" in l]
    has_hid = "0x0011" in sdp or "Human Interface Device" in sdp or "HID" in sdp
    cons = _sh(["hcitool", "con"])
    devs = _sh(["bluetoothctl", "devices"])
    return ("\n    [diag] SDP local services: " + (", ".join(names) if names else "(none)") +
            f"\n    [diag] HID SDP record present: {has_hid}" +
            f"\n    [diag] active BT connections: {cons.replace(chr(10), ' | ')}" +
            f"\n    [diag] known devices: {devs.replace(chr(10), ' | ') or '(none)'}")


def connect(nx, reconnect, timeout=90):
    addr = None
    if reconnect:
        if not ADDR_FILE.exists():
            sys.exit(f"No saved Switch address at {ADDR_FILE}. Run `pair` first.")
        addr = ADDR_FILE.read_text().strip()
        print(f"[nxbt] reconnecting to saved Switch {addr} "
              f"(make sure the Switch is ON and NOT on the Change Grip/Order menu)")
    else:
        print("[nxbt] creating Pro Controller. On the Switch open "
              "Controllers > Change Grip/Order now...")

    index = nx.create_controller(PRO_CONTROLLER, reconnect_address=addr)
    print(f"[nxbt] controller index {index}. Waiting for the Switch (timeout {timeout}s).")
    print(f"[nxbt] adapter: {_adapter_snapshot()}")
    print(f"[nxbt] initial diag:{_deep_diag()}")

    # Poll the controller state instead of blocking silently, so a `crashed` state prints
    # its traceback and a stuck `connecting`/`initializing` is visible (Switch not seeing us).
    # Also dump deep diagnostics every 15s so ONE run captures whether the SDP record is
    # registered and whether any Switch is attempting a connection — without needing a
    # second terminal or the user to watch the screen.
    print("[nxbt] >>> DO NOT press Ctrl-C — it ends on its own. A heartbeat prints every 3s "
          "so you can see it's alive while the Switch (on Change Grip/Order) connects. <<<")
    start = time.monotonic()
    last = None
    last_diag = start
    last_beat = start
    while True:
        st = nx.state[index]["state"]
        if st != last:
            print(f"[nxbt] state -> {st!r}   (adapter: {_adapter_snapshot()})")
            last = st
        if st == "connected":
            print("[nxbt] CONNECTED.")
            break
        if st == "crashed":
            print("[nxbt] CONTROLLER CRASHED — traceback:")
            print(nx.state[index]["errors"])
            sys.exit(1)
        now = time.monotonic()
        if now - last_beat >= 3:
            # Lightweight heartbeat: proves the loop is alive. If these STOP printing while
            # the process is still running, the state poll is hanging (a different bug).
            cons = _sh(["hcitool", "con"]).replace("\n", " ")
            print(f"[nxbt] ..{int(now - start)}s state={st!r} | {cons}")
            last_beat = now
        if now - last_diag >= 15:
            print(f"[nxbt] +{int(now - start)}s deep diag:{_deep_diag()}")
            last_diag = now
        if now - start > timeout:
            print(f"[nxbt] TIMED OUT after {timeout}s in state {st!r}. "
                  f"The Switch never connected — likely a discovery/pairing issue on the Switch side.")
            print(f"[nxbt] final diag:{_deep_diag()}")
            sys.exit(2)
        time.sleep(1)

    # Save the Switch address for fast reconnects next time.
    try:
        addrs = nx.get_switch_addresses()
        if addrs:
            ADDR_FILE.write_text(addrs[0])
            print(f"[nxbt] saved Switch address {addrs[0]} -> {ADDR_FILE.name}")
    except Exception as e:
        print(f"[nxbt] (couldn't read Switch address: {e})")
    return index


def build_packet(nx, active, now):
    """Compose an input packet from inputs still 'held' (refreshed within HOLD_DECAY)."""
    pkt = nx.create_input_packet()
    for key, ts in list(active.items()):
        if now - ts > HOLD_DECAY:
            continue
        if key in BUTTON_KEYS:
            pkt[BUTTON_KEYS[key]] = True
        elif key in STICK_KEYS:
            stick, axis, sign = STICK_KEYS[key]
            pkt[stick][axis] = sign * STICK_MAX
    return pkt


def interactive(nx, index):
    from blessed import Terminal
    term = Terminal()
    active = {}          # key -> last-seen monotonic timestamp
    lock = threading.Lock()
    running = threading.Event()
    running.set()

    def sender():
        period = 1.0 / SEND_HZ
        while running.is_set():
            now = time.monotonic()
            with lock:
                pkt = build_packet(nx, active, now)
            try:
                nx.set_controller_input(index, pkt)
            except Exception:
                pass
            time.sleep(period)

    t = threading.Thread(target=sender, daemon=True)
    t.start()

    print(term.clear)
    print("=== NXBT live control — game reads this as a Pro Controller ===")
    print(" arrows: D-pad     WASD: left stick    IJKL: right stick")
    print(" space/z: A   x: B   c: X   v: Y   q: L   e: R   1: ZL   3: ZR")
    print(" Enter: +(start)   Backspace/-: -(select)   h: HOME   g: CAPTURE")
    print(" Hold a key to hold the input.  ESC or Ctrl-C to quit.")
    print("----------------------------------------------------------------")

    try:
        with term.cbreak():
            while running.is_set():
                key = term.inkey(timeout=0.05)
                if not key:
                    continue
                name = key.name if key.is_sequence else str(key)
                if name in ("KEY_ESCAPE",) or key == chr(3):  # ESC / Ctrl-C
                    break
                lookup = name if name in BUTTON_KEYS or name in STICK_KEYS else str(key)
                if lookup in BUTTON_KEYS or lookup in STICK_KEYS:
                    with lock:
                        active[lookup] = time.monotonic()
                    sys.stdout.write(f"\r  -> {lookup:<14}")
                    sys.stdout.flush()
    except KeyboardInterrupt:
        pass
    finally:
        running.clear()
        t.join(timeout=1)
        print("\n[nxbt] exiting live control.")


def run_macro(nx, index, macro_text):
    print(f"[nxbt] running macro:\n{macro_text}")
    mid = nx.macro(index, macro_text, block=True)
    print(f"[nxbt] macro {mid} done.")


def serve(nx, index, host, port):
    """Stay connected and accept commands over UDP, so another process on this machine
    (that does NOT need root) can drive the controller. This is how the assistant controls
    the Switch: the user runs this once with sudo; the assistant sends UDP datagrams.

    Protocol — one command per datagram, ascii, reply is sent back to the sender:
        ping                 -> pong                      (liveness check)
        state                -> connected|connecting|...  (controller state)
        capture              -> presses the Capture button (screenshot to SD Album)
        quit                 -> shuts the server down
        <anything else>      -> run verbatim as an NXBT macro, e.g.:
                                  "A 0.1"                 tap A
                                  "DPAD_UP 0.3"           hold Up 0.3s
                                  "L_STICK@+000+100 0.5s" push left stick up 0.5s
                                  multi-line macros work too (newlines in the datagram)
    """
    import socket
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind((host, port))
    sock.settimeout(0.5)
    print(f"[nxbt] UDP command server listening on {host}:{port}")
    print(f"[nxbt] commands: ping | state | capture | quit | <raw NXBT macro>")
    while True:
        st = nx.state[index]["state"]
        if st == "crashed":
            print("[nxbt] controller CRASHED:")
            print(nx.state[index]["errors"])
            return
        try:
            data, addr = sock.recvfrom(8192)
        except socket.timeout:
            continue
        cmd = data.decode("utf-8", "replace").strip()
        if not cmd:
            continue
        low = cmd.lower()
        try:
            if low == "ping":
                reply = "pong"
            elif low == "state":
                reply = st
            elif low in ("quit", "shutdown", "exit"):
                sock.sendto(b"bye", addr)
                print("[nxbt] shutdown requested over UDP.")
                return
            elif low == "capture":
                nx.macro(index, "CAPTURE 0.2", block=False)
                reply = "ok capture"
            else:
                mid = nx.macro(index, cmd, block=False)
                reply = f"ok macro {mid}"
        except Exception as e:
            reply = f"err {e}"
        try:
            sock.sendto(reply.encode(), addr)
        except Exception:
            pass
        print(f"[nxbt] <{addr[0]}:{addr[1]}> {cmd!r} -> {reply}")


def main():
    ap = argparse.ArgumentParser(description="Drive the Switch via an emulated Pro Controller.")
    ap.add_argument("mode", choices=["pair", "reconnect"],
                    help="pair: first time (needs Change Grip/Order). reconnect: reuse saved address.")
    ap.add_argument("--macro", metavar="TEXT",
                    help="Run this NXBT macro string instead of interactive control, then exit. "
                         "e.g. --macro $'B 0.1\\n0.1\\nA 0.1'")
    ap.add_argument("--macro-file", metavar="PATH", help="Run a macro read from a file, then exit.")
    ap.add_argument("--serve", action="store_true",
                    help="After connecting, stay up and take commands over UDP (no interactive "
                         "terminal needed). Lets a non-root client on this machine drive the pad.")
    ap.add_argument("--udp-host", default="127.0.0.1", help="UDP bind host for --serve (default 127.0.0.1).")
    ap.add_argument("--udp-port", type=int, default=9999, help="UDP bind port for --serve (default 9999).")
    ap.add_argument("--debug", action="store_true",
                    help="Enable NXBT's internal Bluetooth debug logging (verbose).")
    args = ap.parse_args()

    if os.geteuid() != 0:
        print("WARNING: not running as root — NXBT needs root for raw Bluetooth sockets. "
              "Re-run with sudo if controller creation fails.", file=sys.stderr)

    nx = make_nxbt(debug=args.debug)
    try:
        index = connect(nx, reconnect=(args.mode == "reconnect"))

        if args.serve:
            serve(nx, index, args.udp_host, args.udp_port)
        elif args.macro_file:
            run_macro(nx, index, Path(args.macro_file).read_text())
        elif args.macro:
            run_macro(nx, index, args.macro)
        else:
            interactive(nx, index)
    finally:
        # NXBT forks a tree of multiprocessing children (Manager + controller server +
        # logging). On Ctrl-C these ORPHAN instead of dying and keep holding the Bluetooth
        # adapter, so the next run silently contends with a ghost controller. Terminate them
        # explicitly so every exit path leaves the adapter free.
        _shutdown()


def _shutdown():
    print("\n[nxbt] shutting down — terminating controller processes...")
    for child in multiprocessing.active_children():
        try:
            child.terminate()
        except Exception:
            pass
    for child in multiprocessing.active_children():
        try:
            child.join(timeout=2)
            if child.is_alive():
                os.kill(child.pid, 9)
        except Exception:
            pass


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        _shutdown()
        sys.exit(130)

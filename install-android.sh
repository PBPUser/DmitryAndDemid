#!/usr/bin/env bash
# Build the Android APK and install it to the connected device via adb.
#
# Builds with build-android.sh (so the two stay in sync), then `adb install -r`s the signed APK onto the one
# connected device/emulator and launches it.
#
# Prerequisites: everything build-android.sh needs, plus adb (from the Android SDK platform-tools) and a device
# with USB debugging enabled. Override ADB / ANDROID_HOME / JAVA_HOME in the environment if needed.
#
# Usage:  ./install-android.sh [Debug|Release]
set -euo pipefail
cd "$(dirname "$0")"

: "${ANDROID_HOME:=$HOME/Android/Sdk}"
export ANDROID_HOME

CONFIG="${1:-Release}"
APP_ID="co.sugar.aag2"

# Prefer the SDK's adb, fall back to whatever is on PATH.
ADB="${ADB:-$ANDROID_HOME/platform-tools/adb}"
[[ -x "$ADB" ]] || ADB="adb"

# Build first.
./build-android.sh "$CONFIG"

APK="Android/bin/$CONFIG/net10.0-android/$APP_ID-Signed.apk"
[[ -f "$APK" ]] || { echo "Signed APK not found: $APK" >&2; exit 1; }

# Need exactly one device: `adb get-state` succeeds only when a single device is attached.
if ! "$ADB" get-state >/dev/null 2>&1; then
    echo "No single device/emulator is connected. Attached devices:" >&2
    "$ADB" devices >&2 || true
    echo "Connect one device with USB debugging enabled (or set ANDROID_SERIAL) and retry." >&2
    exit 1
fi

echo "Installing $APK ..."
"$ADB" install -r "$APK"

echo "Launching $APP_ID ..."
"$ADB" shell monkey -p "$APP_ID" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1 || true

echo "Done."

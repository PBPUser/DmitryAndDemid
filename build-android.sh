#!/usr/bin/env bash
# Build the Android APK of the game (Release by default).
#
# Produces:  Android/bin/<Config>/net10.0-android/co.sugar.aag2-Signed.apk
#
# Prerequisites (see Android/DmitryAndDemid.Android.csproj):
#   * the Android SDK (ANDROID_HOME)
#   * a JDK between 17 and 21 (the .NET Android SDK refuses anything outside that range)
# Override ANDROID_HOME / JAVA_HOME in the environment if yours live elsewhere.
#
# Usage:  ./build-android.sh [Debug|Release]
set -euo pipefail
cd "$(dirname "$0")"

: "${ANDROID_HOME:=$HOME/Android/Sdk}"
: "${JAVA_HOME:=/usr/lib/jvm/java-21-openjdk}"
export ANDROID_HOME JAVA_HOME

CONFIG="${1:-Release}"

echo "Building Android APK ($CONFIG)"
echo "  ANDROID_HOME=$ANDROID_HOME"
echo "  JAVA_HOME=$JAVA_HOME"

dotnet build Android/DmitryAndDemid.Android.csproj -c "$CONFIG"

APK="Android/bin/$CONFIG/net10.0-android/co.sugar.aag2-Signed.apk"
if [[ -f "$APK" ]]; then
    echo "APK ready: $APK"
else
    echo "Build finished but the signed APK was not found at: $APK" >&2
    exit 1
fi

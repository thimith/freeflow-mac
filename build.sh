#!/bin/bash
# Build FreeFlow.app.   ./build.sh           → build/FreeFlow.app
#                       ./build.sh install   → also copy to /Applications and launch
set -euo pipefail
cd "$(dirname "$0")"

APP_NAME="FreeFlow"
BUNDLE_ID="app.freeflow.mac"
APP="build/$APP_NAME.app"

echo "==> Compiling (release)…"
swift build -c release
BIN="$(swift build -c release --show-bin-path)/$APP_NAME"

echo "==> Assembling $APP"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/$APP_NAME"
cp Resources/Info.plist "$APP/Contents/Info.plist"

# Bundle whisper.cpp if it was built (scripts/build-whisper.sh) → no Homebrew needed.
if [ -x vendor/bin/whisper-server ]; then
  cp vendor/bin/whisper-server vendor/bin/whisper-cli "$APP/Contents/MacOS/"
  echo "==> Bundled whisper.cpp"
fi

if [ -f Resources/AppIcon.png ] && command -v iconutil >/dev/null; then
  ICONSET="build/AppIcon.iconset"
  rm -rf "$ICONSET"; mkdir -p "$ICONSET"
  for s in 16 32 128 256 512; do
    sips -z $s $s Resources/AppIcon.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
    sips -z $((s*2)) $((s*2)) Resources/AppIcon.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
  done
  iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
fi

# Ad-hoc signature by default. Set FREEFLOW_SIGN_ID="Apple Development: you@…" to use a real
# certificate — then macOS keeps your Accessibility permission across rebuilds.
SIGN_ID="${FREEFLOW_SIGN_ID:--}"
for b in "$APP"/Contents/MacOS/whisper-*; do
  [ -e "$b" ] && codesign --force --sign "$SIGN_ID" "$b"
done
codesign --force --sign "$SIGN_ID" --identifier "$BUNDLE_ID" "$APP"

echo "==> Built $APP"

if [ "${1:-}" = "install" ]; then
  pkill -x "$APP_NAME" 2>/dev/null || true
  sleep 0.5
  rm -rf "/Applications/$APP_NAME.app"
  cp -R "$APP" /Applications/
  if [ "$SIGN_ID" = "-" ]; then
    # A new ad-hoc build is a "different app" to macOS — clear the stale permission so it re-prompts.
    tccutil reset Accessibility "$BUNDLE_ID" >/dev/null 2>&1 || true
  fi
  echo "==> Installed to /Applications — launching"
  open "/Applications/$APP_NAME.app"
fi

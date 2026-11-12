#!/bin/bash
# Build a self-contained FreeFlow.dmg (app + bundled whisper.cpp).
set -euo pipefail
cd "$(dirname "$0")"

[ -x vendor/bin/whisper-server ] || ./scripts/build-whisper.sh
./build.sh

STAGE="build/dmg"
rm -rf "$STAGE" build/FreeFlow.dmg
mkdir -p "$STAGE"
cp -R build/FreeFlow.app "$STAGE/"
ln -s /Applications "$STAGE/Applications"
cat > "$STAGE/READ ME FIRST.txt" <<'TXT'
1. Drag FreeFlow into Applications.
2. Open it. If macOS says it can't verify the developer:
   System Settings → Privacy & Security → scroll down → "Open Anyway".
3. Allow Microphone and Accessibility when asked.
4. System Settings → Keyboard → "Press 🌐 key to" → "Do Nothing".
5. Wait for the speech model download to finish (Settings → Voice), then
   click into any text field, hold fn, talk, release.
TXT
hdiutil create -volname "FreeFlow" -srcfolder "$STAGE" -ov -format UDZO build/FreeFlow.dmg
echo "==> build/FreeFlow.dmg"

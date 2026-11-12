#!/bin/bash
# Build whisper.cpp (whisper-server + whisper-cli) as static, Metal-accelerated binaries into vendor/bin.
set -euo pipefail
cd "$(dirname "$0")/.."

TAG="${WHISPER_TAG:-$(git ls-remote --tags --refs https://github.com/ggml-org/whisper.cpp \
  | awk -F/ '{print $3}' | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sort -V | tail -1)}"
echo "==> whisper.cpp $TAG"

SRC="vendor/whisper.cpp"
rm -rf "$SRC"
git clone --depth 1 --branch "$TAG" https://github.com/ggml-org/whisper.cpp "$SRC"

cmake -S "$SRC" -B "$SRC/build" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_OSX_DEPLOYMENT_TARGET=14.0 \
  -DCMAKE_OSX_ARCHITECTURES=arm64 \
  -DBUILD_SHARED_LIBS=OFF \
  -DGGML_NATIVE=OFF \
  -DGGML_METAL=ON \
  -DGGML_METAL_EMBED_LIBRARY=ON \
  -DWHISPER_BUILD_EXAMPLES=ON \
  -DWHISPER_BUILD_TESTS=OFF \
  -DWHISPER_SDL2=OFF
cmake --build "$SRC/build" --config Release -j "$(sysctl -n hw.ncpu)" --target whisper-server whisper-cli

mkdir -p vendor/bin
cp "$SRC/build/bin/whisper-server" "$SRC/build/bin/whisper-cli" vendor/bin/
echo "==> vendor/bin ready:"; ls -la vendor/bin
otool -L vendor/bin/whisper-server

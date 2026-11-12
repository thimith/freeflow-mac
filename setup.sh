#!/bin/bash
# One-shot setup: installs the free local speech engine, downloads a model, builds and installs FreeFlow.
set -euo pipefail
cd "$(dirname "$0")"

MODEL="${FREEFLOW_MODEL:-large-v3-turbo-q5_0}"   # or: base, small, large-v3-turbo
MODEL_DIR="$HOME/Library/Application Support/FreeFlow/models"

echo "==> FreeFlow for Mac — setup"

if ! xcode-select -p >/dev/null 2>&1; then
  echo "Installing Xcode Command Line Tools (a dialog will appear)…"
  xcode-select --install || true
  echo "When that finishes, run ./setup.sh again."
  exit 1
fi

if ! command -v brew >/dev/null 2>&1; then
  echo "Homebrew is required for the local Whisper engine."
  echo "Install it from https://brew.sh (one command), then run ./setup.sh again."
  exit 1
fi

if ! command -v whisper-server >/dev/null 2>&1 && ! command -v whisper-cli >/dev/null 2>&1; then
  echo "==> Installing whisper.cpp (free, offline speech recognition)…"
  brew install whisper-cpp
fi

mkdir -p "$MODEL_DIR"
if [ ! -f "$MODEL_DIR/ggml-$MODEL.bin" ]; then
  echo "==> Downloading Whisper model '$MODEL'…"
  curl -L --fail --progress-bar -o "$MODEL_DIR/ggml-$MODEL.bin.part" \
    "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$MODEL.bin"
  mv "$MODEL_DIR/ggml-$MODEL.bin.part" "$MODEL_DIR/ggml-$MODEL.bin"
fi
defaults write app.freeflow.mac whisperModel -string "$MODEL"

if [ "${WITH_OLLAMA:-0}" = "1" ]; then
  echo "==> Installing Ollama + llama3.2 for AI polish…"
  brew install --cask ollama || true
  open -a Ollama || true
  sleep 3
  ollama pull llama3.2 || echo "   (run 'ollama pull llama3.2' once Ollama is running)"
  defaults write app.freeflow.mac polishProvider -string ollama
fi

./build.sh install

cat <<'MSG'

✅ FreeFlow is running (look for the waveform icon in the menu bar).

Next:
  1. Allow Microphone + Accessibility when macOS asks.
  2. System Settings → Keyboard → "Press 🌐 key to" → "Do Nothing".
  3. Click into any text field, hold fn, talk, let go.
MSG

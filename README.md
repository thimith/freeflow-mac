# FreeFlow for Mac

A free, open Wispr Flow–style voice dictation app for macOS. Hold a key, talk, let go: polished text lands wherever your cursor is, in any app.

The defaults are all free. Speech recognition runs locally (whisper.cpp). AI cleanup runs on-device (Apple Intelligence) or locally (Ollama). Cloud options (Groq free tier, or any OpenAI-compatible API) are there if you want them, but you don't need them.

## Install (about 5 minutes)

```bash
cd FreeFlowMac
./setup.sh                 # installs whisper.cpp, downloads the model, builds, installs to /Applications
# or, with a local LLM for AI polish too:
WITH_OLLAMA=1 ./setup.sh
```

Requirements: macOS 14+, Xcode Command Line Tools (`xcode-select --install`), and [Homebrew](https://brew.sh).
Apple Silicon is recommended. On Intel Macs, use the `small` model: `FREEFLOW_MODEL=small ./setup.sh`.

After it launches:
1. Allow **Microphone** and **Accessibility** when macOS asks. You can also do this in Settings → Permissions.
2. Go to System Settings → Keyboard → **"Press 🌐 key to" → Do Nothing**.
3. Click into any text field, **hold fn**, talk, and release.

To rebuild after changing the code, run `./build.sh install`.

## How to use it

| Action | How |
|---|---|
| Dictate (push-to-talk) | Hold **fn** (or right ⌥ / right ⌘), talk, release |
| Hands-free | **Double-tap fn**, talk as long as you like, tap fn again |
| Toggle hands-free from anywhere | **⌥ Space** (configurable) |
| Command mode | Select text, hold **fn + ⌃ Control**, say *"make this more friendly"* / *"translate to Spanish"* / *"turn into bullet points"*. The selection gets replaced. With nothing selected, it writes what you ask for: *"write a short reply saying I'll be there at 6"* |
| Cancel | **Esc** |
| Paste last transcript again | **⌃⌘ V** (configurable), or from the menu bar |
| History & stats | Menu bar → History… |

## Features

- **Works in every app.** Text is pasted at the cursor and your clipboard is restored afterwards. For apps that block paste, there's a "simulate typing" mode.
- **Floating recording pill** with a live waveform, a timer, and processing states. It never steals focus.
- **Free local speech-to-text** (whisper.cpp, Large v3 Turbo). It runs as a warm background server, so it's fast. Multilingual with auto-detect, so you can switch between French, English, and Spanish. Models download from inside the app.
- **Other speech engines:** Apple on-device recognition (zero setup), Groq Whisper (free tier, very fast), or any OpenAI-compatible endpoint.
- **AI polish:** removes filler words (um, euh, este…), applies self-corrections ("3pm, no actually 4pm" becomes "4pm"), fixes punctuation, formats lists, and follows "new line" / "new paragraph". Providers: Apple Intelligence, Ollama, Groq, or a custom API. If the AI fails, the basic rule-based cleanup is used.
- **Context-aware tone:** casual in Slack, WhatsApp, and Messages; proper paragraphs in Mail and Gmail; literal in code editors and terminals. Browser tabs are detected from the window title.
- **Command mode:** edit selected text or generate new text by voice.
- **Translate as you speak:** dictate in one language and get the text in another.
- **Personal dictionary:** names and jargon are passed to Whisper and to the AI. Use `wrong -> Right` to fix words that keep getting misheard.
- **Snippets:** say "my calendly link" and get the full URL, or any block of text.
- **Personal style instructions**, e.g. "never use em dashes".
- **History:** searchable, with copy, re-paste, and a view of the original transcript. Stats show words dictated, speaking speed, and time saved compared with typing.
- **Quality of life:** start/stop sounds, optional auto-mute of music while you talk, silence detection (no empty pastes), launch at login, a 10-minute safety cap.

## Privacy

With Local Whisper plus Apple Intelligence or Ollama, nothing leaves your Mac. History is stored in `~/Library/Application Support/FreeFlow/history.json`. API keys, if you use cloud providers, are stored in the app's preferences.

## Troubleshooting

- **The shortcut does nothing after a rebuild.** macOS ties the Accessibility permission to the exact build. `./build.sh install` resets it so you'll be asked again. If you still have trouble, remove FreeFlow in System Settings → Privacy → Accessibility, add it back, and restart it. To avoid this entirely, sign with a certificate: `FREEFLOW_SIGN_ID="Apple Development: …" ./build.sh install`.
- **Pressing fn opens emoji or Apple dictation.** Set "Press 🌐 key to" to "Do Nothing", or switch the trigger to right ⌥ in Settings.
- **"whisper-server not found".** Run `brew install whisper-cpp`. FreeFlow also falls back to `whisper-cli` if only that is installed.
- **Wrong language on short phrases.** Pick your language in Settings → General instead of Auto-detect.
- **Apple Intelligence shows as unavailable.** It needs macOS 26 on Apple Silicon with Apple Intelligence turned on. Otherwise, use Ollama (free) or Groq (free tier).

## Project layout

```
Sources/FreeFlow/
  AppDelegate.swift     menu bar, key state machine, the dictation pipeline
  Input.swift           global key monitoring (fn / right ⌥ / right ⌘), Carbon hotkeys, mic recorder
  LocalWhisper.swift    whisper.cpp server management + model downloads
  Transcription.swift   Apple Speech, Groq / OpenAI-compatible speech-to-text
  Polish.swift          Apple Intelligence / Ollama / Groq LLM, prompts, command mode
  SystemIO.swift        paste/typing, selection capture, app-context detection, sounds, mute
  TextProcessing.swift  dictionary, snippets, rule-based cleanup
  Overlay.swift         floating pill UI
  History.swift         history store + window
  SettingsView.swift    settings window
  Prefs.swift           settings model
```

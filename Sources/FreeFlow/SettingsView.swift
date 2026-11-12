import SwiftUI
import AppKit
import AVFoundation
import ServiceManagement
import ApplicationServices

struct SettingsView: View {
    var body: some View {
        TabView {
            GeneralTab().tabItem { Label("General", systemImage: "gearshape") }
            VoiceTab().tabItem { Label("Voice", systemImage: "waveform") }
            AITab().tabItem { Label("AI Polish", systemImage: "sparkles") }
            WordsTab().tabItem { Label("Dictionary", systemImage: "character.book.closed") }
            PermissionsTab().tabItem { Label("Permissions", systemImage: "lock.shield") }
        }
        .frame(width: 680, height: 600)
    }
}

private func caption(_ s: String, _ color: Color = .secondary) -> some View {
    Text(s).font(.caption).foregroundColor(color).fixedSize(horizontal: false, vertical: true)
}

// MARK: - General

struct GeneralTab: View {
    @AppStorage(K.trigger) private var trigger: TriggerKey = .fn
    @AppStorage(K.toggleCombo) private var toggle: KeyCombo = .optionSpace
    @AppStorage(K.pasteCombo) private var pasteCombo: KeyCombo = .controlCommandV
    @AppStorage(K.language) private var language = "auto"
    @AppStorage(K.translateTo) private var translateTo = ""
    @AppStorage(K.polish) private var polish: PolishProvider = .off
    @AppStorage(K.insertMethod) private var insert: InsertMethod = .paste
    @AppStorage(K.restoreClipboard) private var restore = true
    @AppStorage(K.trailingSpace) private var trailing = true
    @AppStorage(K.sounds) private var sounds = true
    @AppStorage(K.muteMedia) private var mute = false
    @AppStorage(K.showOverlay) private var overlay = true
    @AppStorage(K.silenceDetection) private var silence = true
    @State private var launchAtLogin = SMAppService.mainApp.status == .enabled

    var body: some View {
        Form {
            Section("Shortcuts") {
                Picker("Push-to-talk key", selection: $trigger) {
                    ForEach(TriggerKey.allCases) { Text($0.label).tag($0) }
                }
                caption("Hold to talk, release to insert. Double-tap for hands-free, tap again to finish. Hold \(trigger.short) + ⌃ Control for Command mode: select text and say “make this friendlier”, or ask for anything with nothing selected. Esc cancels.")
                if trigger == .fn {
                    caption("Tip: set System Settings → Keyboard → “Press 🌐 key to” → “Do Nothing”, so macOS doesn't open the emoji picker or its own dictation.", .orange)
                }
                Picker("Start / stop hands-free", selection: $toggle) {
                    ForEach(KeyCombo.toggleChoices) { Text($0.label).tag($0) }
                }
                Picker("Paste last transcript", selection: $pasteCombo) {
                    ForEach(KeyCombo.pasteChoices) { Text($0.label).tag($0) }
                }
            }

            Section("Language") {
                Picker("I speak", selection: $language) {
                    ForEach(Languages.all) { Text($0.name).tag($0.code) }
                }
                caption("Auto-detect handles switching between languages (e.g. French, English, Spanish). Pick one if short phrases get the wrong language.")
                Picker("Translate output to", selection: $translateTo) {
                    Text("Don't translate").tag("")
                    ForEach(Languages.all.dropFirst()) { Text($0.name).tag($0.english) }
                }
                if !translateTo.isEmpty && polish == .off {
                    caption("Translation needs an AI provider — set one in AI Polish.", .orange)
                }
            }

            Section("Inserting text") {
                Picker("Method", selection: $insert) {
                    ForEach(InsertMethod.allCases) { Text($0.label).tag($0) }
                }
                Toggle("Restore my clipboard after pasting", isOn: $restore)
                Toggle("Add a space after each dictation", isOn: $trailing)
            }

            Section("Feedback") {
                Toggle("Play start / stop sounds", isOn: $sounds)
                Toggle("Mute system audio while dictating", isOn: $mute)
                Toggle("Show the floating recording pill", isOn: $overlay)
                Toggle("Ignore recordings with no speech", isOn: $silence)
            }

            Section {
                Toggle("Launch FreeFlow at login", isOn: $launchAtLogin)
                    .onChange(of: launchAtLogin) { _, on in
                        do {
                            if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
                        } catch {
                            launchAtLogin = SMAppService.mainApp.status == .enabled
                        }
                    }
                caption("Works when FreeFlow.app lives in /Applications.")
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - Voice (speech-to-text)

struct VoiceTab: View {
    @AppStorage(K.transcription) private var provider: TranscriptionProvider = .localWhisper
    @AppStorage(K.groqKey) private var groqKey = ""
    @AppStorage(K.groqWhisperModel) private var groqModel = "whisper-large-v3-turbo"
    @AppStorage(K.customBase) private var customBase = ""
    @AppStorage(K.customKey) private var customKey = ""
    @AppStorage(K.customSTTModel) private var customModel = ""

    var body: some View {
        Form {
            Section("Speech-to-text engine") {
                Picker("Engine", selection: $provider) {
                    ForEach(TranscriptionProvider.allCases) { Text($0.label).tag($0) }
                }
                .pickerStyle(.radioGroup)
            }
            switch provider {
            case .localWhisper:
                LocalWhisperSection()
            case .apple:
                Section("Apple on-device") {
                    caption("Uses macOS's built-in speech recognition, fully offline when the language pack is installed. No auto-detect: it uses the language chosen in General (or your system language). Quality is below Whisper but there's nothing to install.")
                }
            case .groq:
                Section("Groq (free tier)") {
                    SecureField("API key", text: $groqKey)
                    TextField("Model", text: $groqModel)
                    Link("Get a free key at console.groq.com/keys", destination: URL(string: "https://console.groq.com/keys")!)
                    caption("Audio is sent to Groq. Free tier is rate-limited but generous for personal dictation. The same key powers Groq AI Polish.")
                }
            case .custom:
                Section("OpenAI-compatible API") {
                    TextField("Base URL", text: $customBase)
                    SecureField("API key", text: $customKey)
                    TextField("Model", text: $customModel)
                    caption("Calls {base}/audio/transcriptions — works with OpenAI, a self-hosted faster-whisper server, LocalAI, etc.")
                }
            }
        }
        .formStyle(.grouped)
    }
}

struct LocalWhisperSection: View {
    @AppStorage(K.whisperModel) private var model = "large-v3-turbo-q5_0"
    @ObservedObject private var dl = ModelDownloader.shared
    @ObservedObject private var whisper = LocalWhisper.shared

    private var enginePath: String? {
        LocalWhisper.binaryPath("whisper-server") ?? LocalWhisper.binaryPath("whisper-cli")
    }

    var body: some View {
        Section("Engine (whisper.cpp)") {
            if let p = enginePath {
                LabeledContent("Installed", value: p)
                LabeledContent("Status") {
                    HStack {
                        Text(whisper.status).foregroundStyle(.secondary)
                        Button("Start now") { Task { try? await whisper.ensureServer() } }
                    }
                }
            } else {
                VStack(alignment: .leading, spacing: 6) {
                    Text("whisper.cpp isn't installed yet. In Terminal, run:")
                    HStack {
                        Text("brew install whisper-cpp").font(.system(.body, design: .monospaced)).textSelection(.enabled)
                        Button("Copy") {
                            NSPasteboard.general.clearContents()
                            NSPasteboard.general.setString("brew install whisper-cpp", forType: .string)
                        }
                    }
                    caption("(Or just run ./setup.sh from the project folder — it does this for you.)")
                }
            }
        }
        Section("Model") {
            ForEach(WhisperModel.all) { m in
                HStack(spacing: 10) {
                    Image(systemName: model == m.id ? "largecircle.fill.circle" : "circle")
                        .foregroundColor(model == m.id ? Color.accentColor : Color.secondary)
                        .onTapGesture { model = m.id }
                    VStack(alignment: .leading, spacing: 2) {
                        Text(m.name)
                        Text(m.detail).font(.caption).foregroundStyle(.secondary)
                    }
                    .contentShape(Rectangle())
                    .onTapGesture { model = m.id }
                    Spacer()
                    if let p = dl.progress[m.id] {
                        ProgressView(value: p).frame(width: 110)
                        Text("\(Int(p * 100))%").font(.caption.monospacedDigit()).frame(width: 34)
                        Button("Cancel") { dl.cancel(m.id) }
                    } else if dl.isDownloaded(m) {
                        Text("Downloaded").font(.caption).foregroundStyle(.green)
                        Button { dl.delete(m) } label: { Image(systemName: "trash") }.buttonStyle(.borderless)
                    } else {
                        Button("Download") { dl.download(m) }
                    }
                }
            }
            if let e = dl.lastError { Text(e).font(.caption).foregroundStyle(.red) }
            caption("Everything runs on your Mac — audio never leaves it. Models are stored in ~/Library/Application Support/FreeFlow/models.")
        }
    }
}

// MARK: - AI polish

struct AITab: View {
    @AppStorage(K.polish) private var provider: PolishProvider = .off
    @AppStorage(K.groqKey) private var groqKey = ""
    @AppStorage(K.groqLLMModel) private var groqModel = "llama-3.3-70b-versatile"
    @AppStorage(K.ollamaURL) private var ollamaURL = "http://localhost:11434/v1"
    @AppStorage(K.ollamaModel) private var ollamaModel = "llama3.2"
    @AppStorage(K.customBase) private var customBase = ""
    @AppStorage(K.customKey) private var customKey = ""
    @AppStorage(K.customLLMModel) private var customModel = ""
    @AppStorage(K.contextAware) private var contextAware = true
    @AppStorage(K.customInstructions) private var instructions = ""
    @State private var testOutput = ""
    @State private var testing = false

    private let sample = "um so I think we should uh meet tomorrow at three no actually four pm and um can you bring the the slides"

    var body: some View {
        Form {
            Section("AI that cleans up what you said") {
                Picker("Provider", selection: $provider) {
                    ForEach(PolishProvider.allCases) { Text($0.label).tag($0) }
                }
                .pickerStyle(.radioGroup)
                caption("Removes ums and repetitions, applies self-corrections (“3pm, no actually 4pm” → “4pm”), fixes punctuation, formats lists, follows “new line / new paragraph”, and adapts tone to the app you're in. Also powers Command mode and translation.")
            }

            switch provider {
            case .off:
                Section { caption("Only simple rules are applied (filler words, capitalization, “new line”). Command mode and translation are disabled.") }
            case .appleIntelligence:
                Section("Apple Intelligence") {
                    LabeledContent("Status", value: AppleIntelligence.statusText)
                    caption("Free and fully on-device. Turn it on in System Settings → Apple Intelligence & Siri.")
                }
            case .ollama:
                Section("Ollama") {
                    TextField("Server URL", text: $ollamaURL)
                    TextField("Model", text: $ollamaModel)
                    Link("Install Ollama (free) from ollama.com", destination: URL(string: "https://ollama.com/download")!)
                    caption("Then in Terminal: ollama pull \(ollamaModel.isEmpty ? "llama3.2" : ollamaModel). Small models (llama3.2, qwen2.5:3b, gemma3:4b) are fast enough for real-time cleanup.")
                }
            case .groq:
                Section("Groq (free tier)") {
                    SecureField("API key", text: $groqKey)
                    TextField("Model", text: $groqModel)
                    Link("Get a free key at console.groq.com/keys", destination: URL(string: "https://console.groq.com/keys")!)
                    caption("Very fast. Text is sent to Groq. Try llama-3.1-8b-instant for even lower latency.")
                }
            case .custom:
                Section("OpenAI-compatible API") {
                    TextField("Base URL", text: $customBase)
                    SecureField("API key", text: $customKey)
                    TextField("Model", text: $customModel)
                }
            }

            Section("Style") {
                Toggle("Adapt to the app I'm typing in", isOn: $contextAware)
                caption("Casual in Slack/WhatsApp/Messages, proper paragraphs in Mail/Gmail, literal in code editors and terminals.")
                VStack(alignment: .leading) {
                    Text("Personal instructions")
                    TextEditor(text: $instructions)
                        .font(.body)
                        .frame(height: 70)
                        .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.secondary.opacity(0.3)))
                    caption("e.g. “Use British spelling. Never use em dashes. Keep my French accents.”")
                }
            }

            if provider != .off {
                Section("Try it") {
                    Text("“\(sample)”").font(.callout).foregroundStyle(.secondary)
                    HStack {
                        Button(testing ? "Running…" : "Test AI polish") { runTest() }.disabled(testing)
                        Spacer()
                    }
                    if !testOutput.isEmpty { Text(testOutput).textSelection(.enabled) }
                }
            }
        }
        .formStyle(.grouped)
    }

    private func runTest() {
        testing = true
        testOutput = ""
        Task {
            do {
                testOutput = try await Polisher.polishThrowing(sample, context: .empty)
            } catch {
                testOutput = "⚠️ " + error.localizedDescription
            }
            testing = false
        }
    }
}

// MARK: - Dictionary & snippets

struct WordsTab: View {
    @AppStorage(K.dictionary) private var dictionary = ""
    @AppStorage(K.snippets) private var snippets = ""

    var body: some View {
        Form {
            Section("Personal dictionary") {
                editor($dictionary)
                caption("One per line. Names, jargon, brands: “Medellín”, “Focus Tribe”. Fix a word that keeps getting misheard with an arrow: “wisper -> Wispr”.")
            }
            Section("Snippets") {
                editor($snippets)
                caption("Say a phrase, get a block of text. One per line: “my calendly link => https://calendly.com/you”. Use \\n for line breaks. Say the phrase alone to insert just the snippet, or inside a sentence to expand it in place.")
            }
        }
        .formStyle(.grouped)
    }

    private func editor(_ text: Binding<String>) -> some View {
        TextEditor(text: text)
            .font(.system(.body, design: .monospaced))
            .frame(height: 150)
            .overlay(RoundedRectangle(cornerRadius: 6).stroke(Color.secondary.opacity(0.3)))
    }
}

// MARK: - Permissions

struct PermissionsTab: View {
    @State private var mic = false
    @State private var ax = false
    private let timer = Timer.publish(every: 1, on: .main, in: .common).autoconnect()

    var body: some View {
        Form {
            Section("FreeFlow needs") {
                row("Microphone", "To hear you.", granted: mic, pane: "Privacy_Microphone") {
                    AVCaptureDevice.requestAccess(for: .audio) { _ in }
                }
                row("Accessibility", "To detect the shortcut key in any app and paste the text where your cursor is.", granted: ax, pane: "Privacy_Accessibility") {
                    let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
                    _ = AXIsProcessTrustedWithOptions(opts)
                }
            }
            Section("Troubleshooting") {
                caption("Shortcut not reacting after you rebuilt the app? macOS ties Accessibility to the exact build. Open Accessibility, remove FreeFlow with “–”, add it again, then restart FreeFlow.")
                Button("Restart FreeFlow") { relaunch() }
            }
        }
        .formStyle(.grouped)
        .onAppear(perform: refresh)
        .onReceive(timer) { _ in refresh() }
    }

    private func refresh() {
        mic = AVCaptureDevice.authorizationStatus(for: .audio) == .authorized
        ax = AXIsProcessTrusted()
    }

    private func row(_ title: String, _ detail: String, granted: Bool, pane: String, request: @escaping () -> Void) -> some View {
        HStack(alignment: .top) {
            Image(systemName: granted ? "checkmark.circle.fill" : "xmark.circle.fill")
                .foregroundColor(granted ? Color.green : Color.red)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).fontWeight(.medium)
                caption(detail)
            }
            Spacer()
            if !granted {
                Button("Grant") {
                    request()
                    NSWorkspace.shared.open(URL(string: "x-apple.systempreferences:com.apple.preference.security?\(pane)")!)
                }
            }
        }
    }

    private func relaunch() {
        let path = Bundle.main.bundlePath
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = ["-c", "sleep 0.6; open \"\(path)\""]
        try? p.run()
        NSApp.terminate(nil)
    }
}

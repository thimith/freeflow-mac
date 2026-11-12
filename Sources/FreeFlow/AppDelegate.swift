import AppKit
import SwiftUI
import AVFoundation
import ApplicationServices

@main
struct FreeFlowMain {
    @MainActor static func main() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.accessory)   // menu-bar only, no Dock icon
        app.run()
    }
}

enum DictationMode: String { case dictation, command }

/// idle → pushToTalk (holding key) → processing → idle
///          ↘ pendingTap (quick tap) → handsFree (second tap) → processing
enum RecState { case idle, pushToTalk, pendingTap, handsFree, processing }

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var statusItem: NSStatusItem!
    private var statusLine: NSMenuItem!
    private let hotkeys = HotkeyMonitor()
    private let recorder = AudioRecorder()
    private let overlay = OverlayController()
    private let media = MediaController()

    private var state: RecState = .idle
    private var mode: DictationMode = .dictation
    private var downTime = Date()
    private var ignoreNextUp = false
    private var announced = false
    private var tapTimer: Timer?
    private var maxTimer: Timer?
    private var axPollTimer: Timer?
    private var recordingURL: URL?
    private var context = AppContext.empty
    private var selection: String?
    private var processingTask: Task<Void, Never>?
    private var lastText: String?

    private var settingsWindow: NSWindow?
    private var historyWindow: NSWindow?

    // MARK: Launch

    func applicationDidFinishLaunching(_ notification: Notification) {
        Prefs.registerDefaults()
        if !Prefs.d.bool(forKey: K.didFirstLaunch) {
            Prefs.d.set(true, forKey: K.didFirstLaunch)
            if AppleIntelligence.isAvailable {
                Prefs.d.set(PolishProvider.appleIntelligence.rawValue, forKey: K.polish)
            }
            // Start fetching the speech model right away so it's ready by the time permissions are granted.
            if Prefs.transcriptionProvider == .localWhisper,
               let m = WhisperModel.all.first(where: { $0.id == Prefs.whisperModel }),
               !ModelDownloader.shared.isDownloaded(m) {
                ModelDownloader.shared.download(m)
            }
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) { self.openSettings() }
        }

        setupStatusItem()

        hotkeys.onTriggerDown = { [weak self] ctrl in self?.triggerDown(controlHeld: ctrl) }
        hotkeys.onTriggerUp = { [weak self] in self?.triggerUp() }
        hotkeys.onCommandModifier = { [weak self] in self?.switchToCommandMode() }
        hotkeys.onOtherKeyWhileHeld = { [weak self] in self?.otherKeyWhileHeld() }
        hotkeys.onEscape = { [weak self] in self?.cancel() }
        hotkeys.onToggle = { [weak self] in self?.toggleHandsFree() }
        hotkeys.onPasteLast = { [weak self] in self?.pasteLast() }
        hotkeys.start()

        recorder.onLevel = { [weak self] level in self?.overlay.model.push(level: level) }

        requestPermissions()

        if Prefs.transcriptionProvider == .localWhisper {
            Task { try? await LocalWhisper.shared.ensureServer() }   // warm the model up
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        LocalWhisper.shared.stop()
        media.restore()
    }

    private func requestPermissions() {
        if AVCaptureDevice.authorizationStatus(for: .audio) == .notDetermined {
            AVCaptureDevice.requestAccess(for: .audio) { _ in }
        }
        let opts = [kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String: true] as CFDictionary
        if !AXIsProcessTrustedWithOptions(opts) {
            axPollTimer = Timer.scheduledTimer(withTimeInterval: 2, repeats: true) { [weak self] t in
                guard AXIsProcessTrusted() else { return }
                t.invalidate()
                DispatchQueue.main.async { self?.hotkeys.restartMonitors() }
            }
        }
    }

    // MARK: Menu bar

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        setIcon("waveform")

        let menu = NSMenu()
        menu.delegate = self
        statusLine = NSMenuItem(title: "", action: nil, keyEquivalent: "")
        statusLine.isEnabled = false
        menu.addItem(statusLine)
        menu.addItem(.separator())
        menu.addItem(item("Start / Stop Dictation", #selector(menuToggle)))
        menu.addItem(item("Paste Last Transcript", #selector(menuPasteLast)))
        menu.addItem(.separator())
        menu.addItem(item("History…", #selector(openHistory), key: "y"))
        menu.addItem(item("Settings…", #selector(openSettings), key: ","))
        menu.addItem(.separator())
        let quit = NSMenuItem(title: "Quit FreeFlow", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        menu.addItem(quit)
        statusItem.menu = menu
    }

    private func item(_ title: String, _ action: Selector, key: String = "") -> NSMenuItem {
        let i = NSMenuItem(title: title, action: action, keyEquivalent: key)
        i.target = self
        return i
    }

    nonisolated func menuWillOpen(_ menu: NSMenu) {
        MainActor.assumeIsolated {
            let words = HistoryStore.shared.totalWords
            statusLine.title = "Hold \(Prefs.triggerKey.short) to dictate · \(words) words so far"
        }
    }

    private func setIcon(_ symbol: String) {
        let img = NSImage(systemSymbolName: symbol, accessibilityDescription: "FreeFlow")
        img?.isTemplate = true
        statusItem.button?.image = img
    }

    @objc private func menuToggle() { toggleHandsFree() }
    @objc private func menuPasteLast() { pasteLast() }

    @objc func openSettings() {
        if settingsWindow == nil {
            let w = NSWindow(contentViewController: NSHostingController(rootView: SettingsView()))
            w.title = "FreeFlow Settings"
            w.styleMask = [.titled, .closable, .miniaturizable]
            w.isReleasedWhenClosed = false
            w.center()
            settingsWindow = w
        }
        NSApp.activate(ignoringOtherApps: true)
        settingsWindow?.makeKeyAndOrderFront(nil)
    }

    @objc func openHistory() {
        if historyWindow == nil {
            let view = HistoryView(onPaste: { [weak self] text in self?.pasteIntoPreviousApp(text) })
            let w = NSWindow(contentViewController: NSHostingController(rootView: view))
            w.title = "FreeFlow History"
            w.styleMask = [.titled, .closable, .miniaturizable, .resizable]
            w.setContentSize(NSSize(width: 680, height: 560))
            w.isReleasedWhenClosed = false
            w.center()
            historyWindow = w
        }
        NSApp.activate(ignoringOtherApps: true)
        historyWindow?.makeKeyAndOrderFront(nil)
    }

    private func pasteIntoPreviousApp(_ text: String) {
        NSApp.hide(nil)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.35) { TextInserter.insert(text) }
    }

    // MARK: Key state machine

    private func triggerDown(controlHeld: Bool) {
        switch state {
        case .handsFree:
            ignoreNextUp = true
            stopAndProcess()
        case .pendingTap:
            // Second tap → hands-free.
            tapTimer?.invalidate()
            state = .handsFree
            ignoreNextUp = true
            announce()
            overlay.set(mode == .command ? .command : .handsFree)
        case .idle:
            mode = controlHeld ? .command : .dictation
            downTime = Date()
            _ = startRecording(as: .pushToTalk)
        case .pushToTalk, .processing:
            break
        }
    }

    private func triggerUp() {
        if ignoreNextUp { ignoreNextUp = false; return }
        guard state == .pushToTalk else { return }
        if Date().timeIntervalSince(downTime) < 0.3 {
            state = .pendingTap
            tapTimer = Timer.scheduledTimer(withTimeInterval: 0.35, repeats: false) { [weak self] _ in
                DispatchQueue.main.async {
                    guard let self, self.state == .pendingTap else { return }
                    self.cancelRecording(silently: true)
                }
            }
        } else {
            stopAndProcess()
        }
    }

    private func switchToCommandMode() {
        guard state == .pushToTalk || state == .handsFree, mode == .dictation else { return }
        mode = .command
        captureSelection()
        Polisher.prepare(mode: .command, selection: selection, context: context)
        if announced { overlay.set(.command) }
    }

    private func otherKeyWhileHeld() {
        // The trigger is being used as a normal modifier (e.g. ⌥+key, fn+arrow) → abort quietly.
        if state == .pushToTalk && !capturingSelection { cancelRecording(silently: true) }
    }

    private func toggleHandsFree() {
        switch state {
        case .idle:
            mode = .dictation
            if startRecording(as: .handsFree) { announce() }
        case .handsFree, .pushToTalk, .pendingTap:
            stopAndProcess()
        case .processing:
            break
        }
    }

    private func cancel() {
        switch state {
        case .pushToTalk, .pendingTap, .handsFree:
            cancelRecording(silently: false)
        case .processing:
            processingTask?.cancel()
            processingTask = nil
            finishIdle()
            overlay.flash("Cancelled")
        case .idle:
            break
        }
    }

    // MARK: Recording

    private func startRecording(as newState: RecState) -> Bool {
        guard AVCaptureDevice.authorizationStatus(for: .audio) == .authorized else {
            AVCaptureDevice.requestAccess(for: .audio) { _ in }
            overlay.flash("Allow microphone access in Settings → Permissions")
            Feedback.error()
            return false
        }
        context = AppContext.current()
        selection = nil
        announced = false
        do {
            recordingURL = try recorder.start()
        } catch {
            overlay.flash(error.localizedDescription)
            Feedback.error()
            return false
        }
        state = newState
        if mode == .command { captureSelection() }
        Polisher.prepare(mode: mode, selection: selection, context: context)
        // Don't flash UI/sounds for accidental taps or for the trigger used as a modifier.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) { [weak self] in
            guard let self, self.state == .pushToTalk || self.state == .handsFree else { return }
            self.announce()
        }
        maxTimer = Timer.scheduledTimer(withTimeInterval: 600, repeats: false) { [weak self] _ in
            DispatchQueue.main.async { self?.stopAndProcess() }
        }
        return true
    }

    private func announce() {
        guard !announced else { return }
        announced = true
        if Prefs.sounds { Feedback.start() }
        if Prefs.muteMedia { media.mute() }
        let phase: OverlayModel.Phase = mode == .command ? .command : (state == .handsFree ? .handsFree : .listening)
        overlay.show(phase)
        setIcon("record.circle")
    }

    private var capturingSelection = false

    private func captureSelection() {
        guard selection == nil else { return }
        capturingSelection = true
        selection = TextInserter.selectedText()
        capturingSelection = false
    }

    private func cancelRecording(silently: Bool) {
        tapTimer?.invalidate()
        maxTimer?.invalidate()
        recorder.stop()
        if let u = recordingURL { try? FileManager.default.removeItem(at: u) }
        recordingURL = nil
        if announced && Prefs.muteMedia { media.restore() }
        ignoreNextUp = false
        finishIdle()
        if silently { overlay.hide() } else { overlay.flash("Cancelled") }
    }

    private func finishIdle() {
        state = .idle
        announced = false
        setIcon("waveform")
    }

    private func stopAndProcess() {
        tapTimer?.invalidate()
        maxTimer?.invalidate()
        guard let url = recordingURL else { finishIdle(); return }
        recordingURL = nil
        let info = recorder.stop()
        if Prefs.muteMedia { media.restore() }
        if Prefs.sounds {
            // If we muted the output, wait for it to come back so the sound is audible.
            if Prefs.muteMedia { DispatchQueue.main.asyncAfter(deadline: .now() + 0.25) { Feedback.stop() } }
            else { Feedback.stop() }
        }

        if info.duration < 0.4 || (Prefs.silenceDetection && info.peak < 0.18) {
            try? FileManager.default.removeItem(at: url)
            finishIdle()
            overlay.flash("No speech detected")
            return
        }

        state = .processing
        setIcon("ellipsis.circle")
        overlay.set(.transcribing)
        let mode = self.mode, ctx = context, sel = selection
        processingTask = Task { [weak self] in
            await self?.process(url: url, duration: info.duration, mode: mode, ctx: ctx, selection: sel)
        }
    }

    private func process(url: URL, duration: TimeInterval, mode: DictationMode, ctx: AppContext, selection: String?) async {
        defer { try? FileManager.default.removeItem(at: url) }
        let t0 = Date()
        do {
            let raw = TextCleanup.stripArtifacts(try await Transcriber.transcribe(url))
            let sttTime = Date().timeIntervalSince(t0)
            try Task.checkCancellation()
            guard !raw.isEmpty else {
                finishIdle()
                overlay.flash("No speech detected")
                return
            }

            let t1 = Date()
            var usedAI = false
            var output: String
            if mode == .command {
                usedAI = true
                overlay.set(.thinking)
                output = try await Polisher.command(instruction: raw, selection: selection, context: ctx)
            } else if let snippet = Snippets.exactMatch(raw) {
                output = snippet
            } else if Polisher.needsAI(raw) {
                usedAI = true
                overlay.set(.polishing)
                output = await Polisher.polish(raw, context: ctx)
                output = Snippets.expandInline(output)
            } else {
                output = Snippets.expandInline(TextCleanup.basic(raw))
            }
            output = Vocabulary.applyReplacements(output)
            try Task.checkCancellation()
            let aiTime = usedAI ? Date().timeIntervalSince(t1) : 0
            NSLog("FreeFlow timing: speech %.2fs, AI %.2fs", sttTime, aiTime)

            deliver(output, raw: raw, duration: duration, mode: mode, ctx: ctx,
                    sttTime: sttTime, aiTime: aiTime)
        } catch is CancellationError {
            // user pressed Esc
        } catch {
            guard !Task.isCancelled else { return }
            finishIdle()
            Feedback.error()
            overlay.flash(error.localizedDescription)
        }
    }

    private func deliver(_ text: String, raw: String, duration: TimeInterval, mode: DictationMode, ctx: AppContext,
                         sttTime: TimeInterval, aiTime: TimeInterval) {
        var t = text
        if Prefs.trailingSpace, mode == .dictation, ctx.category != .terminal,
           let last = t.last, !last.isWhitespace {
            t += " "
        }
        TextInserter.insert(t)
        lastText = t
        HistoryStore.shared.add(HistoryEntry(date: Date(), text: text, raw: raw, app: ctx.appName,
                                             mode: mode.rawValue, seconds: duration,
                                             sttSeconds: sttTime, aiSeconds: aiTime))
        finishIdle()
        overlay.set(.done(String(format: "%.1fs", sttTime + aiTime)))
    }

    private func pasteLast() {
        guard let t = lastText ?? HistoryStore.shared.entries.first?.text else {
            overlay.flash("Nothing dictated yet")
            return
        }
        TextInserter.insert(t)
    }
}

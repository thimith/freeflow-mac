import AppKit
import AVFoundation
import Carbon.HIToolbox

// MARK: - Global keyboard handling

final class HotkeyMonitor {
    var onTriggerDown: ((_ controlHeld: Bool) -> Void)?
    var onTriggerUp: (() -> Void)?
    var onCommandModifier: (() -> Void)?
    var onOtherKeyWhileHeld: (() -> Void)?
    var onEscape: (() -> Void)?
    var onToggle: (() -> Void)?
    var onPasteLast: (() -> Void)?

    private var monitors: [Any] = []
    private var triggerIsDown = false
    private var hotKeyRefs: [EventHotKeyRef] = []
    private var handlerInstalled = false
    private var registered: (KeyCombo, KeyCombo)?
    private var defaultsObserver: NSObjectProtocol?

    func start() {
        installMonitors()
        registerHotkeys()
        defaultsObserver = NotificationCenter.default.addObserver(
            forName: UserDefaults.didChangeNotification, object: nil, queue: .main
        ) { [weak self] _ in self?.registerHotkeys() }
    }

    /// Re-create NSEvent monitors (needed after Accessibility is granted).
    func restartMonitors() {
        for m in monitors { NSEvent.removeMonitor(m) }
        monitors.removeAll()
        installMonitors()
    }

    private func installMonitors() {
        if let m = NSEvent.addGlobalMonitorForEvents(matching: .flagsChanged, handler: { [weak self] e in self?.handleFlags(e) }) {
            monitors.append(m)
        }
        if let m = NSEvent.addLocalMonitorForEvents(matching: .flagsChanged, handler: { [weak self] e in self?.handleFlags(e); return e }) {
            monitors.append(m)
        }
        if let m = NSEvent.addGlobalMonitorForEvents(matching: .keyDown, handler: { [weak self] e in self?.handleKey(e) }) {
            monitors.append(m)
        }
        if let m = NSEvent.addLocalMonitorForEvents(matching: .keyDown, handler: { [weak self] e in self?.handleKey(e); return e }) {
            monitors.append(m)
        }
    }

    private func isSynthetic(_ e: NSEvent) -> Bool {
        e.cgEvent?.getIntegerValueField(.eventSourceUserData) == kSyntheticTag
    }

    private func handleKey(_ e: NSEvent) {
        if isSynthetic(e) { return }
        if e.keyCode == UInt16(kVK_Escape) { onEscape?(); return }
        if triggerIsDown { onOtherKeyWhileHeld?() }
    }

    private func handleFlags(_ e: NSEvent) {
        if isSynthetic(e) { return }
        let flags = e.modifierFlags
        let raw = flags.rawValue

        // Control pressed while the trigger is held → Command mode.
        if triggerIsDown, e.keyCode == UInt16(kVK_Control) || e.keyCode == UInt16(kVK_RightControl),
           flags.contains(.control) {
            onCommandModifier?()
            return
        }

        let pressed: Bool
        switch Prefs.triggerKey {
        case .fn:
            guard e.keyCode == UInt16(kVK_Function) || e.keyCode == 179 else { return }
            pressed = flags.contains(.function)
        case .rightOption:
            guard e.keyCode == UInt16(kVK_RightOption) else { return }
            pressed = raw & 0x40 != 0      // NX_DEVICERALTKEYMASK
        case .rightCommand:
            guard e.keyCode == UInt16(kVK_RightCommand) else { return }
            pressed = raw & 0x10 != 0      // NX_DEVICERCMDKEYMASK
        }

        if pressed && !triggerIsDown {
            triggerIsDown = true
            onTriggerDown?(flags.contains(.control))
        } else if !pressed && triggerIsDown {
            triggerIsDown = false
            onTriggerUp?()
        }
    }

    // MARK: Carbon hotkeys (consume the keystroke, no Accessibility needed)

    func registerHotkeys() {
        let t = Prefs.toggleCombo, p = Prefs.pasteCombo
        if let r = registered, r.0 == t, r.1 == p { return }
        for ref in hotKeyRefs { UnregisterEventHotKey(ref) }
        hotKeyRefs.removeAll()
        installHandler()
        for (combo, id) in [(t, UInt32(1)), (p, UInt32(2))] where combo != .none {
            var ref: EventHotKeyRef?
            let hkID = EventHotKeyID(signature: OSType(0x4646_4C57), id: id)
            if RegisterEventHotKey(combo.keyCode, combo.modifiers, hkID, GetApplicationEventTarget(), 0, &ref) == noErr,
               let ref {
                hotKeyRefs.append(ref)
            }
        }
        registered = (t, p)
    }

    private func installHandler() {
        guard !handlerInstalled else { return }
        handlerInstalled = true
        var spec = EventTypeSpec(eventClass: OSType(kEventClassKeyboard), eventKind: UInt32(kEventHotKeyPressed))
        let me = Unmanaged.passUnretained(self).toOpaque()
        InstallEventHandler(GetApplicationEventTarget(), { (_: EventHandlerCallRef?, event: EventRef?, userData: UnsafeMutableRawPointer?) -> OSStatus in
            guard let event, let userData else { return OSStatus(eventNotHandledErr) }
            var hk = EventHotKeyID()
            GetEventParameter(event, EventParamName(kEventParamDirectObject), EventParamType(typeEventHotKeyID),
                              nil, MemoryLayout<EventHotKeyID>.size, nil, &hk)
            let monitor = Unmanaged<HotkeyMonitor>.fromOpaque(userData).takeUnretainedValue()
            DispatchQueue.main.async {
                if hk.id == 1 { monitor.onToggle?() } else if hk.id == 2 { monitor.onPasteLast?() }
            }
            return noErr
        }, 1, &spec, me, nil)
    }
}

// MARK: - Microphone

final class AudioRecorder {
    private var recorder: AVAudioRecorder?
    private var timer: Timer?
    private var startDate = Date()
    private(set) var peak: Float = 0
    var onLevel: ((Float) -> Void)?

    var isRecording: Bool { recorder?.isRecording ?? false }

    /// Records 16 kHz mono 16-bit WAV — the native format for Whisper, accepted by every API.
    func start() throws -> URL {
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("freeflow-\(UUID().uuidString).wav")
        let settings: [String: Any] = [
            AVFormatIDKey: Int(kAudioFormatLinearPCM),
            AVSampleRateKey: 16_000.0,
            AVNumberOfChannelsKey: 1,
            AVLinearPCMBitDepthKey: 16,
            AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: false,
        ]
        let r = try AVAudioRecorder(url: url, settings: settings)
        r.isMeteringEnabled = true
        r.prepareToRecord()
        guard r.record() else { throw FFError.message("Couldn't start the microphone") }
        recorder = r
        peak = 0
        startDate = Date()
        let t = Timer(timeInterval: 1.0 / 30.0, repeats: true) { [weak self] _ in self?.tick() }
        RunLoop.main.add(t, forMode: .common)
        timer = t
        return url
    }

    private func tick() {
        guard let r = recorder else { return }
        r.updateMeters()
        let db = r.averagePower(forChannel: 0)
        let level = max(0, min(1, (db + 50) / 45))
        peak = max(peak, level)
        onLevel?(level)
    }

    @discardableResult
    func stop() -> (duration: TimeInterval, peak: Float) {
        timer?.invalidate()
        timer = nil
        let d = recorder?.currentTime ?? Date().timeIntervalSince(startDate)
        recorder?.stop()
        recorder = nil
        return (d, peak)
    }
}

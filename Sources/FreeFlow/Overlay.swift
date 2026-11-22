import AppKit
import SwiftUI

final class OverlayModel: ObservableObject {
    enum Phase: Equatable {
        case listening, handsFree, command, transcribing, polishing, thinking, translating, done(String), message(String)
        var isRecording: Bool { self == .listening || self == .handsFree || self == .command }
    }
    static let barCount = 26
    @Published var phase: Phase = .listening
    @Published var levels: [CGFloat] = Array(repeating: 0, count: OverlayModel.barCount)
    @Published var startDate = Date()
    /// What the Copy button on the "Inserted" pill copies (nil = no button).
    @Published var copyText: String?
    @Published var copied = false

    func copy() {
        guard let t = copyText else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(t, forType: .string)
        copied = true
    }

    func push(level: Float) {
        var l = levels
        l.removeFirst()
        l.append(CGFloat(level))
        levels = l
    }

    func reset() {
        levels = Array(repeating: 0, count: Self.barCount)
        startDate = Date()
    }
}

/// The floating "pill" (top centre by default — see Settings → General → Bubble position). Never steals focus, ignores the mouse.
final class OverlayController {
    let model = OverlayModel()
    private var hideWork: DispatchWorkItem?
    private lazy var panel: NSPanel = makePanel()

    private func makePanel() -> NSPanel {
        let p = ClickPanel(contentRect: NSRect(x: 0, y: 0, width: 360, height: 64),
                        styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isFloatingPanel = true
        p.level = .statusBar
        p.backgroundColor = .clear
        p.isOpaque = false
        p.hasShadow = false
        p.ignoresMouseEvents = true
        p.hidesOnDeactivate = false
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        let host = FirstMouseHostingView(rootView: OverlayView(model: model))
        host.frame = NSRect(x: 0, y: 0, width: 360, height: 64)
        host.autoresizingMask = [.width, .height]
        p.contentView = host
        return p
    }

    private func position() {
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main
        guard let vf = screen?.visibleFrame else { return }
        panel.setFrame(Prefs.bubblePlace.frame(for: panel.frame.size, in: vf, margin: 10), display: true)
    }

    func show(_ phase: OverlayModel.Phase) {
        hideWork?.cancel()
        model.reset()
        model.copyText = nil
        panel.ignoresMouseEvents = true
        model.phase = phase
        guard Prefs.showOverlay else { return }
        position()
        panel.alphaValue = 1
        panel.orderFrontRegardless()
    }

    func set(_ phase: OverlayModel.Phase) {
        hideWork?.cancel()
        model.phase = phase
        if case .done = phase {} else { model.copyText = nil }
        model.copied = false
        // clickable only while it shows the Copy button; otherwise clicks go straight through
        panel.ignoresMouseEvents = model.copyText == nil
        if Prefs.showOverlay && !panel.isVisible {
            position()
            panel.alphaValue = 1
            panel.orderFrontRegardless()
        }
        switch phase {
        case .done: scheduleHide(after: model.copyText == nil ? 1.1 : 5)
        case .message: scheduleHide(after: 2.4)
        default: break
        }
    }

    func flash(_ message: String) { set(.message(message)) }

    /// "✓ Inserted · 1.2s  [Copy]" — stays up 5 s so you can copy what you just dictated.
    func done(_ timing: String, copy text: String?) {
        model.copyText = text
        set(.done(timing))
    }

    /// Show an error in the pill AND keep it in the error log (Settings → Errors), so it can be read after it fades.
    func error(_ message: String, detail: Error? = nil) {
        ErrorLog.add(message, detail: detail.map { String(describing: $0) })
        set(.message(message))
        // errors stay up a little longer than other messages
        hideWork?.cancel()
        scheduleHide(after: 4.5)
    }

    private func scheduleHide(after s: TimeInterval) {
        let w = DispatchWorkItem { [weak self] in self?.hide() }
        hideWork = w
        DispatchQueue.main.asyncAfter(deadline: .now() + s, execute: w)
    }

    func hide() {
        hideWork?.cancel()
        guard panel.isVisible else { return }
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.18
            panel.animator().alphaValue = 0
        }, completionHandler: { [weak self] in
            guard let self, self.panel.alphaValue < 0.05 else { return }
            self.panel.orderOut(nil)
        })
    }
}

struct OverlayView: View {
    @ObservedObject var model: OverlayModel

    var body: some View {
        HStack(spacing: 10) {
            leading
            center
        }
        .padding(.horizontal, 16)
        .frame(height: 40)
        .background(Capsule().fill(Color.black.opacity(0.86)))
        .overlay(Capsule().strokeBorder(Color.white.opacity(0.14), lineWidth: 1))
        .foregroundColor(.white)
        .font(.system(size: 12, weight: .medium))
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: Prefs.bubblePlace.alignment)
        .animation(.easeInOut(duration: 0.15), value: model.phase)
    }

    @ViewBuilder private var leading: some View {
        switch model.phase {
        case .listening:
            Circle().fill(Color.red).frame(width: 8, height: 8)
        case .handsFree:
            Image(systemName: "lock.fill").font(.system(size: 10, weight: .bold)).foregroundColor(.orange)
        case .command:
            Image(systemName: "sparkles").foregroundColor(Color(red: 0.75, green: 0.6, blue: 1))
        case .transcribing, .polishing, .thinking, .translating:
            ProgressView().progressViewStyle(.circular).controlSize(.small).scaleEffect(0.7).frame(width: 14, height: 14)
        case .done:
            Image(systemName: "checkmark.circle.fill").foregroundColor(.green)
        case .message:
            Image(systemName: "exclamationmark.circle.fill").foregroundColor(.yellow)
        }
    }

    @ViewBuilder private var center: some View {
        switch model.phase {
        case .listening, .handsFree, .command:
            Waveform(levels: model.levels)
            TimelineView(.periodic(from: model.startDate, by: 1)) { ctx in
                Text(elapsed(ctx.date))
                    .font(.system(size: 11, weight: .medium, design: .monospaced))
                    .foregroundColor(.white.opacity(0.65))
            }
            if model.phase == .handsFree { Text("esc").font(.system(size: 10)).foregroundColor(.white.opacity(0.4)) }
        case .transcribing: Text("Transcribing…")
        case .polishing: Text("Polishing…")
        case .thinking: Text("Thinking…")
        case .translating: Text("Translating…")
        case .done(let t):
            Text("Inserted · \(t)")
            if model.copyText != nil {
                Button(action: { model.copy() }) {
                    HStack(spacing: 4) {
                        Image(systemName: model.copied ? "checkmark" : "doc.on.doc").font(.system(size: 10, weight: .bold))
                        Text(model.copied ? "Copied" : "Copy").font(.system(size: 11, weight: .semibold))
                    }
                    .padding(.horizontal, 9)
                    .frame(height: 24)
                    .background(Capsule().fill(Color.white.opacity(model.copied ? 0.28 : 0.16)))
                    .contentShape(Capsule())
                }
                .buttonStyle(.plain)
            }
        case .message(let m): Text(m).lineLimit(1).frame(maxWidth: 280)
        }
    }

    private func elapsed(_ now: Date) -> String {
        let s = max(0, Int(now.timeIntervalSince(model.startDate)))
        return String(format: "%d:%02d", s / 60, s % 60)
    }
}

struct Waveform: View {
    let levels: [CGFloat]
    var body: some View {
        HStack(alignment: .center, spacing: 2) {
            ForEach(levels.indices, id: \.self) { i in
                Capsule()
                    .fill(Color.white.opacity(0.9))
                    .frame(width: 3, height: max(3, min(24, levels[i] * 28)))
            }
        }
        .frame(height: 26)
        .animation(.linear(duration: 0.07), value: levels)
    }
}

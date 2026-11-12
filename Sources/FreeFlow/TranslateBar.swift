import AppKit
import SwiftUI

/// A panel you can click without it stealing focus from the app you're typing in.
final class ClickPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

final class FirstMouseHostingView<V: View>: NSHostingView<V> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

final class TranslateBarModel: ObservableObject {
    enum Mode { case compact, menu, all }
    @Published var mode: Mode = .compact
}

/// "文A Translate" button that appears for 5 s after each dictation, and the language card it opens.
final class TranslateBar {
    var onTranslate: ((TLang) -> Void)?
    var onImprove: (() -> Void)?

    private let model = TranslateBarModel()
    private var panel: ClickPanel?
    private var hideWork: DispatchWorkItem?
    private var outsideMonitor: Any?

    var isVisible: Bool { panel?.isVisible ?? false }

    /// The small pill, auto-hides after 5 seconds.
    func showCompact() {
        model.mode = .compact
        present(size: NSSize(width: 150, height: 44))
        scheduleHide(after: 5)
    }

    /// The language card (from the pill or the ⌃⌥T shortcut).
    func showMenu() {
        model.mode = .menu
        let rows = (TLang.quick.count + 1) / 2
        present(size: NSSize(width: 300, height: CGFloat(78 + rows * 48 + 2 * 48)))
        scheduleHide(after: 20)
        watchOutsideClicks()
    }

    func showAll() {
        model.mode = .all
        present(size: NSSize(width: 300, height: 420))
        scheduleHide(after: 30)
        watchOutsideClicks()
    }

    func hide() {
        hideWork?.cancel()
        if let m = outsideMonitor { NSEvent.removeMonitor(m); outsideMonitor = nil }
        panel?.orderOut(nil)
    }

    private func scheduleHide(after s: TimeInterval) {
        hideWork?.cancel()
        let w = DispatchWorkItem { [weak self] in self?.hide() }
        hideWork = w
        DispatchQueue.main.asyncAfter(deadline: .now() + s, execute: w)
    }

    private func watchOutsideClicks() {
        guard outsideMonitor == nil else { return }
        outsideMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            self?.hide()
        }
    }

    private func present(size: NSSize) {
        let p = panel ?? makePanel()
        panel = p
        let mouse = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(mouse, $0.frame, false) } ?? NSScreen.main
        let vf = screen?.visibleFrame ?? .zero
        p.setFrame(NSRect(x: vf.midX - size.width / 2, y: vf.minY + 24, width: size.width, height: size.height), display: true)
        p.orderFrontRegardless()
    }

    private func makePanel() -> ClickPanel {
        let p = ClickPanel(contentRect: NSRect(x: 0, y: 0, width: 150, height: 44),
                           styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        p.isFloatingPanel = true
        p.level = .statusBar
        p.backgroundColor = .clear
        p.isOpaque = false
        p.hasShadow = true
        p.hidesOnDeactivate = false
        p.becomesKeyOnlyIfNeeded = true
        p.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        let view = TranslateBarView(model: model,
                                    expand: { [weak self] in self?.showMenu() },
                                    more: { [weak self] in self?.showAll() },
                                    pick: { [weak self] lang in self?.hide(); self?.onTranslate?(lang) },
                                    improve: { [weak self] in self?.hide(); self?.onImprove?() })
        let host = FirstMouseHostingView(rootView: view)
        host.autoresizingMask = [.width, .height]
        p.contentView = host
        return p
    }
}

struct TranslateBarView: View {
    @ObservedObject var model: TranslateBarModel
    let expand: () -> Void
    let more: () -> Void
    let pick: (TLang) -> Void
    let improve: () -> Void

    var body: some View {
        Group {
            switch model.mode {
            case .compact: compact
            case .menu: menu
            case .all: allLanguages
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .bottom)
    }

    private var compact: some View {
        Button(action: expand) {
            HStack(spacing: 8) {
                Text("文A").font(.system(size: 14, weight: .heavy))
                Text("Translate").font(.system(size: 13, weight: .semibold))
            }
            .foregroundColor(.white)
            .padding(.horizontal, 18)
            .frame(height: 40)
            .background(Capsule().fill(LinearGradient(colors: [Color(red: 0.49, green: 0.30, blue: 1), Color(red: 0.16, green: 0.43, blue: 1)],
                                                      startPoint: .leading, endPoint: .trailing)))
            .overlay(Capsule().strokeBorder(Color.white.opacity(0.35), lineWidth: 1.5))
            .contentShape(Capsule())
        }
        .buttonStyle(.plain)
    }

    private var menu: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Translate to").font(.system(size: 11, weight: .semibold)).foregroundColor(.white.opacity(0.55))
            LazyVGrid(columns: [GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8)], spacing: 8) {
                ForEach(TLang.quick) { l in
                    tile("\(l.flag)  \(l.short)") { pick(l) }
                }
            }
            tile("🌐  More languages", action: more)
            tile("✨  Improve by voice", accent: true, action: improve)
        }
        .padding(12)
        .background(card)
    }

    private var allLanguages: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("All languages").font(.system(size: 11, weight: .semibold)).foregroundColor(.white.opacity(0.55))
            ScrollView {
                VStack(spacing: 6) {
                    ForEach(TLang.all) { l in tile("\(l.flag)  \(l.label)") { pick(l) } }
                }
            }
        }
        .padding(12)
        .background(card)
    }

    private var card: some View {
        RoundedRectangle(cornerRadius: 18)
            .fill(Color(red: 0.11, green: 0.11, blue: 0.14))
            .overlay(RoundedRectangle(cornerRadius: 18).strokeBorder(Color.white.opacity(0.18), lineWidth: 1))
    }

    private func tile(_ label: String, accent: Bool = false, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(label)
                .font(.system(size: 14, weight: .medium))
                .foregroundColor(.white)
                .lineLimit(1)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 12)
                .frame(height: 40)
                .background(
                    RoundedRectangle(cornerRadius: 11).fill(accent
                        ? AnyShapeStyle(LinearGradient(colors: [Color(red: 0.67, green: 0.43, blue: 1), Color(red: 0.93, green: 0.28, blue: 0.6)],
                                                       startPoint: .leading, endPoint: .trailing))
                        : AnyShapeStyle(Color(red: 0.19, green: 0.19, blue: 0.23)))
                )
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

import AppKit
import ApplicationServices

/// Marks events we synthesize so our own key monitors ignore them.
let kSyntheticTag: Int64 = 0x4646_4C57

// MARK: - Which app are we typing into?

enum AppCategory: String { case email, chat, code, terminal, document, other }

struct AppContext {
    var appName: String
    var bundleID: String
    var windowTitle: String
    var category: AppCategory

    static let empty = AppContext(appName: "", bundleID: "", windowTitle: "", category: .other)

    static func current() -> AppContext {
        guard let app = NSWorkspace.shared.frontmostApplication else { return .empty }
        let bid = app.bundleIdentifier ?? ""
        let title = windowTitle(pid: app.processIdentifier) ?? ""
        return AppContext(appName: app.localizedName ?? "", bundleID: bid, windowTitle: title,
                          category: categorize(bid.lowercased(), title.lowercased()))
    }

    private static func windowTitle(pid: pid_t) -> String? {
        let app = AXUIElementCreateApplication(pid)
        var win: CFTypeRef?
        guard AXUIElementCopyAttributeValue(app, kAXFocusedWindowAttribute as CFString, &win) == .success,
              let w = win else { return nil }
        var title: CFTypeRef?
        guard AXUIElementCopyAttributeValue(w as! AXUIElement, kAXTitleAttribute as CFString, &title) == .success else { return nil }
        return title as? String
    }

    private static func categorize(_ bid: String, _ title: String) -> AppCategory {
        let email = ["com.apple.mail", "com.microsoft.outlook", "com.superhuman", "com.readdle.smartemail", "com.airmailapp", "com.mimestream", "it.bloop.airmail", "com.sparkmailapp", "com.google.gmail"]
        let chat = ["com.tinyspeck.slackmacgap", "com.hnc.discord", "com.apple.mobilesms", "net.whatsapp", "desktop.whatsapp", "ru.keepcoder.telegram", "org.telegram", "org.whispersystems.signal", "com.microsoft.teams", "com.facebook.archon", "com.facebook.messenger", "us.zoom"]
        let code = ["com.apple.dt.xcode", "com.microsoft.vscode", "com.todesktop.230313mzl4w4u92", "dev.zed", "com.jetbrains", "com.sublimetext", "com.panic.nova", "com.exafunction.windsurf", "com.anthropic.claude", "com.openai.chat"]
        let terminal = ["com.apple.terminal", "com.googlecode.iterm2", "dev.warp", "net.kovidgoyal.kitty", "com.mitchellh.ghostty", "io.alacritty"]
        let docs = ["notion.id", "md.obsidian", "com.apple.notes", "com.apple.iwork.pages", "com.microsoft.word", "com.apple.textedit", "com.craft", "com.lukilabs", "net.shinyfrog.bear", "com.ulyssesapp"]

        if terminal.contains(where: { bid.hasPrefix($0) }) { return .terminal }
        if email.contains(where: { bid.hasPrefix($0) }) { return .email }
        if chat.contains(where: { bid.hasPrefix($0) }) { return .chat }
        if code.contains(where: { bid.hasPrefix($0) }) { return .code }
        if docs.contains(where: { bid.hasPrefix($0) }) { return .document }

        // Browsers: guess from the tab/window title.
        if ["gmail", "outlook", "inbox", "proton mail", "fastmail"].contains(where: { title.contains($0) }) { return .email }
        if ["slack", "discord", "whatsapp", "messenger", "telegram", "linkedin messaging", "teams"].contains(where: { title.contains($0) }) { return .chat }
        if ["github", "gitlab", "stack overflow", "claude", "chatgpt", "replit", "codesandbox"].contains(where: { title.contains($0) }) { return .code }
        if ["google docs", "notion", "confluence", "docs.google"].contains(where: { title.contains($0) }) { return .document }
        return .other
    }
}

// MARK: - Putting text into the focused app

enum TextInserter {
    enum Result { case inserted, copiedOnly(String) }

    /// Inserts into the focused app. If macOS won't let us send keystrokes (Accessibility missing or
    /// stale after an update) or no other app is focused, the text is left on the clipboard instead.
    @discardableResult
    static func insert(_ text: String) -> Result {
        if !AXIsProcessTrusted() {
            copyOnly(text)
            return .copiedOnly("Copied — re-enable Accessibility to auto-paste")
        }
        if NSWorkspace.shared.frontmostApplication?.processIdentifier == ProcessInfo.processInfo.processIdentifier {
            copyOnly(text)
            return .copiedOnly("Copied — click into another app's text field")
        }
        switch Prefs.insertMethod {
        case .paste: paste(text)
        case .type: typeText(text)
        }
        return .inserted
    }

    static func copyOnly(_ text: String) {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(text, forType: .string)
    }

    static func paste(_ text: String) {
        let pb = NSPasteboard.general
        let saved = Prefs.restoreClipboard ? snapshot(pb) : nil
        pb.clearContents()
        pb.setString(text, forType: .string)
        // Tell clipboard managers (Raycast, Paste, Maccy…) to ignore this entry.
        pb.setString("", forType: NSPasteboard.PasteboardType("org.nspasteboard.TransientType"))
        let ourChange = pb.changeCount
        sendKey(9, flags: .maskCommand) // ⌘V
        if let saved {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                guard pb.changeCount == ourChange else { return } // user copied something meanwhile
                restore(saved, to: pb)
            }
        }
    }

    static func typeText(_ text: String) {
        DispatchQueue.global(qos: .userInitiated).async {
            let src = CGEventSource(stateID: .hidSystemState)
            let units = Array(text.utf16)
            var i = 0
            while i < units.count {
                var chunk = Array(units[i..<min(i + 16, units.count)])
                for down in [true, false] {
                    guard let e = CGEvent(keyboardEventSource: src, virtualKey: 0, keyDown: down) else { continue }
                    e.keyboardSetUnicodeString(stringLength: chunk.count, unicodeString: &chunk)
                    e.setIntegerValueField(.eventSourceUserData, value: kSyntheticTag)
                    e.post(tap: .cghidEventTap)
                }
                i += 16
                usleep(6000)
            }
        }
    }

    static func sendKey(_ code: CGKeyCode, flags: CGEventFlags) {
        let src = CGEventSource(stateID: .hidSystemState)
        for down in [true, false] {
            guard let e = CGEvent(keyboardEventSource: src, virtualKey: code, keyDown: down) else { continue }
            e.flags = flags
            e.setIntegerValueField(.eventSourceUserData, value: kSyntheticTag)
            e.post(tap: .cghidEventTap)
        }
    }

    // MARK: Selection (for Command mode)

    static func selectedText() -> String? {
        if let s = axSelectedText(), !s.isEmpty { return s }
        return copySelection()
    }

    private static func axSelectedText() -> String? {
        let sys = AXUIElementCreateSystemWide()
        var focused: CFTypeRef?
        guard AXUIElementCopyAttributeValue(sys, kAXFocusedUIElementAttribute as CFString, &focused) == .success,
              let el = focused else { return nil }
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(el as! AXUIElement, kAXSelectedTextAttribute as CFString, &value) == .success else { return nil }
        return value as? String
    }

    /// Text to translate / improve: the selection if there is one, otherwise the whole text box (⌘A, which stays selected
    /// so the result replaces it).
    static func selectionOrWholeField() -> String? {
        if let s = selectedText(), !s.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return s }
        sendKey(0, flags: .maskCommand)   // ⌘A
        RunLoop.current.run(until: Date().addingTimeInterval(0.08))
        return copySelection()
    }

    /// Fallback for apps without accessibility text support (Electron, browsers): ⌘C and read the clipboard.
    static func copySelection() -> String? {
        let pb = NSPasteboard.general
        let saved = snapshot(pb)
        let before = pb.changeCount
        sendKey(8, flags: .maskCommand) // ⌘C
        let deadline = Date().addingTimeInterval(0.3)
        while pb.changeCount == before && Date() < deadline {
            RunLoop.current.run(until: Date().addingTimeInterval(0.02))
        }
        guard pb.changeCount != before else { return nil }
        let s = pb.string(forType: .string)
        restore(saved, to: pb)
        return s
    }

    // MARK: Clipboard snapshot

    static func snapshot(_ pb: NSPasteboard) -> [[NSPasteboard.PasteboardType: Data]] {
        (pb.pasteboardItems ?? []).map { item in
            var dict: [NSPasteboard.PasteboardType: Data] = [:]
            for t in item.types { if let d = item.data(forType: t) { dict[t] = d } }
            return dict
        }
    }

    static func restore(_ items: [[NSPasteboard.PasteboardType: Data]], to pb: NSPasteboard) {
        pb.clearContents()
        let objs: [NSPasteboardItem] = items.map { dict in
            let it = NSPasteboardItem()
            for (t, d) in dict { it.setData(d, forType: t) }
            return it
        }
        if !objs.isEmpty { pb.writeObjects(objs) }
    }
}

// MARK: - Sounds & media

enum Feedback {
    static func start() { NSSound(named: "Pop")?.play() }
    static func stop() { NSSound(named: "Tink")?.play() }
    static func error() { NSSound(named: "Basso")?.play() }
}

/// Mutes system output while you dictate (so music/videos don't leak into the mic), then restores it.
final class MediaController {
    private var mutedByUs = false
    private let q = DispatchQueue(label: "freeflow.media")

    func mute() {
        q.asyncAfter(deadline: .now() + 0.2) {
            let already = Self.osa("output muted of (get volume settings)") == "true"
            if !already {
                _ = Self.osa("set volume with output muted")
                self.mutedByUs = true
            }
        }
    }

    func restore() {
        q.async {
            if self.mutedByUs {
                _ = Self.osa("set volume without output muted")
                self.mutedByUs = false
            }
        }
    }

    private static func osa(_ script: String) -> String {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
        p.arguments = ["-e", script]
        let out = Pipe()
        p.standardOutput = out
        p.standardError = FileHandle.nullDevice
        do { try p.run() } catch { return "" }
        let data = out.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return String(decoding: data, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

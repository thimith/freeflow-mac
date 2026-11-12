import AppKit
import SwiftUI

struct HistoryEntry: Codable, Identifiable, Hashable {
    var id = UUID()
    var date: Date
    var text: String
    var raw: String
    var app: String
    var mode: String
    var seconds: Double
    var sttSeconds: Double? = nil
    var aiSeconds: Double? = nil
    var words: Int { text.split(whereSeparator: { $0.isWhitespace }).count }
}

final class HistoryStore: ObservableObject {
    static let shared = HistoryStore()
    @Published private(set) var entries: [HistoryEntry] = []
    private let url = AppPaths.support.appendingPathComponent("history.json")

    init() {
        if let data = try? Data(contentsOf: url),
           let list = try? JSONDecoder().decode([HistoryEntry].self, from: data) {
            entries = list
        }
    }

    func add(_ e: HistoryEntry) {
        entries.insert(e, at: 0)
        if entries.count > 2000 { entries.removeLast(entries.count - 2000) }
        save()
    }

    func delete(_ e: HistoryEntry) { entries.removeAll { $0.id == e.id }; save() }
    func clear() { entries.removeAll(); save() }

    private func save() {
        let snapshot = entries
        let url = self.url
        DispatchQueue.global(qos: .utility).async {
            if let data = try? JSONEncoder().encode(snapshot) { try? data.write(to: url, options: .atomic) }
        }
    }

    var totalWords: Int { entries.reduce(0) { $0 + $1.words } }
    var totalSeconds: Double { entries.reduce(0) { $0 + $1.seconds } }
}

struct HistoryView: View {
    @ObservedObject var store = HistoryStore.shared
    @State private var query = ""
    @State private var confirmClear = false
    var onPaste: (String) -> Void

    init(onPaste: @escaping (String) -> Void) { self.onPaste = onPaste }

    private var filtered: [HistoryEntry] {
        guard !query.isEmpty else { return store.entries }
        return store.entries.filter {
            $0.text.localizedCaseInsensitiveContains(query) || $0.app.localizedCaseInsensitiveContains(query)
        }
    }

    private var wpm: Int {
        store.totalSeconds > 5 ? Int(Double(store.totalWords) / (store.totalSeconds / 60)) : 0
    }

    private var timeSaved: String {
        // vs. typing at 40 wpm
        let typingMinutes = Double(store.totalWords) / 40
        let saved = max(0, typingMinutes - store.totalSeconds / 60)
        return saved >= 60 ? String(format: "%.1f h", saved / 60) : "\(Int(saved)) min"
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 28) {
                stat("Words dictated", "\(store.totalWords)")
                stat("Dictations", "\(store.entries.count)")
                stat("Speaking speed", "\(wpm) wpm")
                stat("Time saved", timeSaved)
                Spacer()
            }
            .padding(16)
            HStack {
                Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                TextField("Search history", text: $query).textFieldStyle(.plain)
                Spacer()
                Button("Clear All…") { confirmClear = true }.disabled(store.entries.isEmpty)
            }
            .padding(.horizontal, 16).padding(.bottom, 10)
            Divider()
            if filtered.isEmpty {
                VStack(spacing: 8) {
                    Image(systemName: "waveform").font(.largeTitle).foregroundStyle(.tertiary)
                    Text(store.entries.isEmpty ? "Nothing yet — hold \(Prefs.triggerKey.short) and talk." : "No matches")
                        .foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                List(filtered) { e in HistoryRow(entry: e, onPaste: onPaste) }
                    .listStyle(.inset)
            }
        }
        .frame(minWidth: 600, minHeight: 500)
        .confirmationDialog("Delete all history?", isPresented: $confirmClear) {
            Button("Delete All", role: .destructive) { store.clear() }
        }
    }

    private func stat(_ title: String, _ value: String) -> some View {
        VStack(alignment: .leading, spacing: 2) {
            Text(value).font(.system(size: 20, weight: .semibold, design: .rounded))
            Text(title).font(.caption).foregroundStyle(.secondary)
        }
    }
}

struct HistoryRow: View {
    let entry: HistoryEntry
    var onPaste: (String) -> Void
    @State private var showRaw = false

    init(entry: HistoryEntry, onPaste: @escaping (String) -> Void) {
        self.entry = entry
        self.onPaste = onPaste
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack {
                if entry.mode == "command" { Image(systemName: "sparkles").foregroundStyle(.purple) }
                Text(entry.app.isEmpty ? "Unknown app" : entry.app).font(.caption.weight(.semibold))
                Text(entry.date.formatted(date: .abbreviated, time: .shortened)).font(.caption).foregroundStyle(.secondary)
                if let s = entry.sttSeconds {
                    Text(timing(s, entry.aiSeconds ?? 0)).font(.caption.monospacedDigit()).foregroundStyle(.tertiary)
                        .help("Time after you stopped talking: speech-to-text + AI polish")
                }
                Spacer()
                Button { copy(entry.text) } label: { Image(systemName: "doc.on.doc") }
                    .buttonStyle(.borderless).help("Copy")
                Button { onPaste(entry.text) } label: { Image(systemName: "arrow.uturn.left.circle") }
                    .buttonStyle(.borderless).help("Paste into the previous app")
                Button { HistoryStore.shared.delete(entry) } label: { Image(systemName: "trash") }
                    .buttonStyle(.borderless).help("Delete")
            }
            Text(entry.text).textSelection(.enabled)
            if entry.raw != entry.text {
                Button(showRaw ? "Hide original" : "Show original transcript") { showRaw.toggle() }
                    .buttonStyle(.link).font(.caption)
                if showRaw {
                    Text(entry.raw).font(.callout).foregroundStyle(.secondary).textSelection(.enabled)
                }
            }
        }
        .padding(.vertical, 4)
    }

    private func timing(_ stt: Double, _ ai: Double) -> String {
        ai > 0 ? String(format: "voice %.1fs · AI %.1fs", stt, ai) : String(format: "voice %.1fs", stt)
    }

    private func copy(_ s: String) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(s, forType: .string)
    }
}

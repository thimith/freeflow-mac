import Foundation

struct WhisperModel: Identifiable {
    let id: String
    let name: String
    let detail: String
    var fileName: String { "ggml-\(id).bin" }
    var url: URL { URL(string: "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/\(fileName)")! }

    static let all: [WhisperModel] = [
        WhisperModel(id: "base", name: "Base", detail: "142 MB · fastest · OK for clear English"),
        WhisperModel(id: "small", name: "Small", detail: "466 MB · good balance · fine on Intel Macs"),
        WhisperModel(id: "large-v3-turbo-q5_0", name: "Large v3 Turbo (quantized)", detail: "547 MB · best accuracy & multilingual · recommended on Apple Silicon"),
        WhisperModel(id: "large-v3-turbo", name: "Large v3 Turbo (full)", detail: "1.6 GB · marginally better, more RAM"),
    ]
}

private enum WhisperError: Error { case noServerBinary }

/// Runs whisper.cpp's `whisper-server` in the background (model stays loaded → low latency).
/// Falls back to `whisper-cli` per request if the server binary isn't installed.
@MainActor
final class LocalWhisper: ObservableObject {
    static let shared = LocalWhisper()

    @Published var status = "Idle"

    private var process: Process?
    private var running: (model: String, lang: String)?
    private var startTask: Task<Void, Error>?
    let port = 8178

    nonisolated static var modelsDir: URL {
        let u = AppPaths.support.appendingPathComponent("models", isDirectory: true)
        try? FileManager.default.createDirectory(at: u, withIntermediateDirectories: true)
        return u
    }

    nonisolated static func modelURL(_ id: String) -> URL {
        modelsDir.appendingPathComponent("ggml-\(id).bin")
    }

    nonisolated static func binaryPath(_ name: String) -> String? {
        // Bundled inside FreeFlow.app (the .dmg build ships whisper.cpp) …
        let bundled = Bundle.main.bundleURL.appendingPathComponent("Contents/MacOS/\(name)").path
        if FileManager.default.isExecutableFile(atPath: bundled) { return bundled }
        // … or installed with Homebrew.
        let dirs = ["/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin", NSHomeDirectory() + "/.local/bin"]
        for d in dirs {
            let p = d + "/" + name
            if FileManager.default.isExecutableFile(atPath: p) { return p }
        }
        return nil
    }

    nonisolated static var threads: Int {
        max(4, min(8, ProcessInfo.processInfo.activeProcessorCount))
    }

    private var baseURL: URL { URL(string: "http://127.0.0.1:\(port)")! }

    private static let session: URLSession = {
        let c = URLSessionConfiguration.ephemeral
        c.connectionProxyDictionary = [:]   // never route localhost through a proxy
        c.timeoutIntervalForRequest = 120
        return URLSession(configuration: c)
    }()

    // MARK: Server lifecycle

    func ensureServer() async throws {
        if let t = startTask { try await t.value; return }
        if let p = process, p.isRunning, let r = running,
           r.model == Prefs.whisperModel, r.lang == Prefs.language { return }
        let t = Task { try await self.launch() }
        startTask = t
        defer { startTask = nil }
        try await t.value
    }

    private func launch() async throws {
        stop()
        let model = Prefs.whisperModel
        let lang = Prefs.language
        let modelURL = Self.modelURL(model)
        guard FileManager.default.fileExists(atPath: modelURL.path) else {
            status = "Model not downloaded"
            throw FFError.message("Download a Whisper model in Settings → Voice")
        }
        guard let bin = Self.binaryPath("whisper-server") else {
            status = "whisper-server not found (using whisper-cli)"
            throw WhisperError.noServerBinary
        }
        Self.killOrphans()

        let base = ["-m", modelURL.path, "--host", "127.0.0.1", "--port", String(port),
                    "-l", lang, "-t", String(Self.threads)]
        // Speed flags: greedy decoding (beam search is ~2-3x slower for little gain on dictation),
        // no temperature-fallback re-decodes, flash attention on Metal.
        let fast = ["-bs", "1", "-bo", "1", "-nf", "-fa"]
        status = "Loading \(model)…"
        do {
            try await start(bin: bin, args: base + fast)
        } catch {
            // An older/newer whisper.cpp may not know one of the flags — retry with the basics.
            try await start(bin: bin, args: base)
        }
        running = (model, lang)
        status = "Ready · \(model)"
    }

    private func start(bin: String, args: [String]) async throws {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: bin)
        p.arguments = args
        p.standardOutput = FileHandle.nullDevice
        p.standardError = FileHandle.nullDevice
        try p.run()
        process = p
        for _ in 0..<240 {   // up to 60 s for big models on first load
            if !p.isRunning {
                process = nil
                status = "Engine crashed — re-download the model?"
                throw FFError.message("whisper-server exited unexpectedly")
            }
            if await ping() { return }
            try await Task.sleep(nanoseconds: 250_000_000)
        }
        p.terminate()
        process = nil
        throw FFError.message("whisper-server didn't start in time")
    }

    func stop() {
        process?.terminate()
        process = nil
        running = nil
        status = "Idle"
    }

    private func ping() async -> Bool {
        var r = URLRequest(url: baseURL)
        r.timeoutInterval = 0.5
        do {
            let (_, resp) = try await Self.session.data(for: r)
            return resp is HTTPURLResponse
        } catch { return false }
    }

    nonisolated private static func killOrphans() {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/pkill")
        p.arguments = ["-f", "whisper-server.*--port 8178"]
        try? p.run()
        p.waitUntilExit()
    }

    // MARK: Transcription

    func transcribe(url: URL, language: String, prompt: String) async throws -> String {
        do {
            try await ensureServer()
        } catch WhisperError.noServerBinary {
            return try await Self.runCLI(url: url, language: language, prompt: prompt)
        }

        var mp = Multipart()
        mp.addFile("file", filename: "audio.wav", mime: "audio/wav", data: try Data(contentsOf: url))
        mp.addField("response_format", "json")
        mp.addField("temperature", "0.0")
        if language != "auto" { mp.addField("language", language) }
        if !prompt.isEmpty { mp.addField("prompt", prompt) }

        var req = URLRequest(url: baseURL.appendingPathComponent("inference"))
        req.httpMethod = "POST"
        req.setValue(mp.contentType, forHTTPHeaderField: "Content-Type")
        req.httpBody = mp.finished()

        let data = try await HTTP.send(req, session: Self.session)
        guard let obj = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            throw FFError.message("Unexpected response from whisper-server")
        }
        if let err = obj["error"] as? String { throw FFError.message(err) }
        return (obj["text"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
    }

    nonisolated static func runCLI(url: URL, language: String, prompt: String) async throws -> String {
        guard let bin = binaryPath("whisper-cli") ?? binaryPath("whisper-cpp") else {
            throw FFError.message("whisper.cpp isn't installed — run: brew install whisper-cpp")
        }
        let model = modelURL(Prefs.whisperModel)
        guard FileManager.default.fileExists(atPath: model.path) else {
            throw FFError.message("Download a Whisper model in Settings → Voice")
        }
        return try await Task.detached(priority: .userInitiated) { () throws -> String in
            let p = Process()
            p.executableURL = URL(fileURLWithPath: bin)
            var args = ["-m", model.path, "-f", url.path, "-l", language, "-nt", "-np", "-t", String(threads),
                        "-bs", "1", "-bo", "1"]
            if !prompt.isEmpty { args += ["--prompt", prompt] }
            p.arguments = args
            let out = Pipe()
            p.standardOutput = out
            p.standardError = FileHandle.nullDevice
            try p.run()
            let data = out.fileHandleForReading.readDataToEndOfFile()
            p.waitUntilExit()
            guard p.terminationStatus == 0 else { throw FFError.message("whisper-cli failed (\(p.terminationStatus))") }
            return String(decoding: data, as: UTF8.self)
                .components(separatedBy: .newlines)
                .map { $0.trimmingCharacters(in: .whitespaces) }
                .filter { !$0.isEmpty }
                .joined(separator: " ")
        }.value
    }
}

// MARK: - Model downloads with progress

final class ModelDownloader: NSObject, ObservableObject, URLSessionDownloadDelegate {
    static let shared = ModelDownloader()

    @Published var progress: [String: Double] = [:]
    @Published var lastError: String?

    private var idsByTask: [Int: String] = [:]
    private var tasksByID: [String: URLSessionDownloadTask] = [:]
    private let lock = NSLock()
    private lazy var session = URLSession(configuration: .default, delegate: self, delegateQueue: nil)

    func isDownloaded(_ m: WhisperModel) -> Bool {
        FileManager.default.fileExists(atPath: LocalWhisper.modelURL(m.id).path)
    }

    func download(_ m: WhisperModel) {
        lastError = nil
        let t = session.downloadTask(with: m.url)
        lock.lock(); idsByTask[t.taskIdentifier] = m.id; tasksByID[m.id] = t; lock.unlock()
        progress[m.id] = 0
        t.resume()
    }

    func cancel(_ id: String) {
        lock.lock(); let t = tasksByID[id]; lock.unlock()
        t?.cancel()
        progress[id] = nil
    }

    func delete(_ m: WhisperModel) {
        try? FileManager.default.removeItem(at: LocalWhisper.modelURL(m.id))
        objectWillChange.send()
    }

    private func modelID(for task: URLSessionTask) -> String? {
        lock.lock(); defer { lock.unlock() }
        return idsByTask[task.taskIdentifier]
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64,
                    totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        guard let id = modelID(for: downloadTask), totalBytesExpectedToWrite > 0 else { return }
        let p = Double(totalBytesWritten) / Double(totalBytesExpectedToWrite)
        DispatchQueue.main.async { if self.progress[id] != nil { self.progress[id] = p } }
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        guard let id = modelID(for: downloadTask) else { return }
        if let http = downloadTask.response as? HTTPURLResponse, http.statusCode != 200 {
            DispatchQueue.main.async { self.lastError = "Download failed (HTTP \(http.statusCode))" }
            return
        }
        let dest = LocalWhisper.modelURL(id)
        try? FileManager.default.removeItem(at: dest)
        do {
            try FileManager.default.moveItem(at: location, to: dest)
        } catch {
            DispatchQueue.main.async { self.lastError = error.localizedDescription }
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        guard let id = modelID(for: task) else { return }
        lock.lock(); idsByTask[task.taskIdentifier] = nil; tasksByID[id] = nil; lock.unlock()
        DispatchQueue.main.async {
            self.progress[id] = nil
            if let e = error as NSError?, e.code != NSURLErrorCancelled { self.lastError = e.localizedDescription }
            self.objectWillChange.send()
            if error == nil, id == Prefs.whisperModel, Prefs.transcriptionProvider == .localWhisper {
                Task { @MainActor in try? await LocalWhisper.shared.ensureServer() }
            }
        }
    }
}

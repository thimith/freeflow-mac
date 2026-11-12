import Foundation
import Speech

// MARK: - HTTP helpers

struct Multipart {
    let boundary = "FreeFlow-\(UUID().uuidString)"
    private(set) var body = Data()

    var contentType: String { "multipart/form-data; boundary=\(boundary)" }

    mutating func addField(_ name: String, _ value: String) {
        body.append("--\(boundary)\r\nContent-Disposition: form-data; name=\"\(name)\"\r\n\r\n\(value)\r\n".data(using: .utf8)!)
    }

    mutating func addFile(_ name: String, filename: String, mime: String, data: Data) {
        body.append("--\(boundary)\r\nContent-Disposition: form-data; name=\"\(name)\"; filename=\"\(filename)\"\r\nContent-Type: \(mime)\r\n\r\n".data(using: .utf8)!)
        body.append(data)
        body.append("\r\n".data(using: .utf8)!)
    }

    func finished() -> Data {
        var b = body
        b.append("--\(boundary)--\r\n".data(using: .utf8)!)
        return b
    }
}

enum HTTP {
    static func send(_ req: URLRequest, session: URLSession = .shared) async throws -> Data {
        let (data, resp) = try await session.data(for: req)
        guard let http = resp as? HTTPURLResponse else { throw FFError.message("No response from server") }
        guard (200..<300).contains(http.statusCode) else {
            var msg = String(data: data, encoding: .utf8) ?? ""
            if let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
               let err = obj["error"] as? [String: Any], let m = err["message"] as? String { msg = m }
            if http.statusCode == 401 { throw FFError.message("Invalid API key (401)") }
            if http.statusCode == 429 { throw FFError.message("Rate limited (429) — try again in a moment") }
            throw FFError.message("HTTP \(http.statusCode): \(msg.prefix(160))")
        }
        return data
    }

    static func endpoint(_ base: String, _ path: String) throws -> URL {
        var b = base.trimmingCharacters(in: .whitespacesAndNewlines)
        while b.hasSuffix("/") { b.removeLast() }
        guard let u = URL(string: b + path) else { throw FFError.message("Invalid URL: \(base)") }
        return u
    }
}

// MARK: - Transcriber

enum Transcriber {
    static func transcribe(_ url: URL) async throws -> String {
        let lang = Prefs.language
        let prompt = Vocabulary.whisperPrompt()
        switch Prefs.transcriptionProvider {
        case .localWhisper:
            return try await LocalWhisper.shared.transcribe(url: url, language: lang, prompt: prompt)
        case .apple:
            return try await AppleSpeech.transcribe(url: url, language: lang)
        case .groq:
            guard !Prefs.groqKey.isEmpty else {
                throw FFError.message("Add your free Groq API key in Settings → Voice")
            }
            return try await openAICompatible(base: "https://api.groq.com/openai/v1", key: Prefs.groqKey,
                                              model: Prefs.groqWhisperModel, url: url, language: lang, prompt: prompt)
        case .custom:
            return try await openAICompatible(base: Prefs.customBase, key: Prefs.customKey,
                                              model: Prefs.customSTTModel, url: url, language: lang, prompt: prompt)
        }
    }

    static func openAICompatible(base: String, key: String, model: String, url: URL,
                                 language: String, prompt: String) async throws -> String {
        var mp = Multipart()
        mp.addFile("file", filename: "audio.wav", mime: "audio/wav", data: try Data(contentsOf: url))
        mp.addField("model", model)
        if language != "auto" { mp.addField("language", language) }
        if !prompt.isEmpty { mp.addField("prompt", prompt) }
        mp.addField("response_format", "json")
        mp.addField("temperature", "0")

        var req = URLRequest(url: try HTTP.endpoint(base, "/audio/transcriptions"))
        req.httpMethod = "POST"
        req.timeoutInterval = 60
        req.setValue(mp.contentType, forHTTPHeaderField: "Content-Type")
        if !key.isEmpty { req.setValue("Bearer \(key)", forHTTPHeaderField: "Authorization") }
        req.httpBody = mp.finished()

        let data = try await HTTP.send(req)
        let obj = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        return (obj?["text"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

// MARK: - Apple on-device speech

enum AppleSpeech {
    private static var currentTask: SFSpeechRecognitionTask?

    static func transcribe(url: URL, language: String) async throws -> String {
        let status: SFSpeechRecognizerAuthorizationStatus = await withCheckedContinuation { c in
            SFSpeechRecognizer.requestAuthorization { c.resume(returning: $0) }
        }
        guard status == .authorized else {
            throw FFError.message("Allow Speech Recognition in System Settings → Privacy")
        }
        let locale: Locale = language == "auto"
            ? Locale.current
            : Locale(identifier: Languages.appleLocales[language] ?? language)
        guard let recognizer = SFSpeechRecognizer(locale: locale) ?? SFSpeechRecognizer(locale: Locale(identifier: "en-US")),
              recognizer.isAvailable else {
            throw FFError.message("Apple speech recognition isn't available for this language")
        }
        let request = SFSpeechURLRecognitionRequest(url: url)
        if recognizer.supportsOnDeviceRecognition { request.requiresOnDeviceRecognition = true }
        request.addsPunctuation = true
        request.shouldReportPartialResults = false
        request.contextualStrings = Vocabulary.parse().words

        return try await withCheckedThrowingContinuation { cont in
            var finished = false
            currentTask = recognizer.recognitionTask(with: request) { result, error in
                if finished { return }
                if let error {
                    finished = true
                    cont.resume(throwing: error)
                } else if let result, result.isFinal {
                    finished = true
                    cont.resume(returning: result.bestTranscription.formattedString)
                }
            }
        }
    }
}

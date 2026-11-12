import Foundation

func rx(_ s: String, _ pattern: String, _ template: String,
        _ options: NSRegularExpression.Options = [.caseInsensitive]) -> String {
    guard let re = try? NSRegularExpression(pattern: pattern, options: options) else { return s }
    return re.stringByReplacingMatches(in: s, range: NSRange(s.startIndex..., in: s), withTemplate: template)
}

// MARK: - Personal dictionary
// One entry per line. "Wispr" adds a word; "wisper -> Wispr" also fixes a mishearing.

enum Vocabulary {
    struct Parsed { var words: [String]; var replacements: [(String, String)] }

    static func parse() -> Parsed {
        var words: [String] = []
        var reps: [(String, String)] = []
        for line in Prefs.dictionary.components(separatedBy: .newlines) {
            let l = line.trimmingCharacters(in: .whitespaces)
            if l.isEmpty || l.hasPrefix("#") { continue }
            if let r = l.range(of: "->") ?? l.range(of: "=>") {
                let wrong = l[..<r.lowerBound].trimmingCharacters(in: .whitespaces)
                let right = l[r.upperBound...].trimmingCharacters(in: .whitespaces)
                if !wrong.isEmpty && !right.isEmpty { reps.append((wrong, right)); words.append(right) }
            } else {
                words.append(l)
            }
        }
        return Parsed(words: words, replacements: reps)
    }

    /// Prompt that biases Whisper towards the user's vocabulary.
    static func whisperPrompt() -> String {
        let w = parse().words
        guard !w.isEmpty else { return "" }
        return "Vocabulary: " + w.prefix(60).joined(separator: ", ") + "."
    }

    static func applyReplacements(_ s: String) -> String {
        var out = s
        for (wrong, right) in parse().replacements {
            out = rx(out, "\\b" + NSRegularExpression.escapedPattern(for: wrong) + "\\b",
                     NSRegularExpression.escapedTemplate(for: right))
        }
        return out
    }
}

// MARK: - Snippets ("my calendly link => https://calendly.com/me")

enum Snippets {
    static func parse() -> [(trigger: String, text: String)] {
        var out: [(String, String)] = []
        for line in Prefs.snippets.components(separatedBy: .newlines) {
            let l = line.trimmingCharacters(in: .whitespaces)
            if l.isEmpty || l.hasPrefix("#") { continue }
            guard let r = l.range(of: "=>") ?? l.range(of: "->") else { continue }
            let trig = l[..<r.lowerBound].trimmingCharacters(in: .whitespaces)
            let text = l[r.upperBound...].trimmingCharacters(in: .whitespaces)
                .replacingOccurrences(of: "\\n", with: "\n")
            if !trig.isEmpty && !text.isEmpty { out.append((trig, text)) }
        }
        return out
    }

    static func normalize(_ s: String) -> String {
        let lowered = s.lowercased().folding(options: .diacriticInsensitive, locale: nil)
        let stripped = lowered.unicodeScalars.filter { CharacterSet.alphanumerics.contains($0) || $0 == " " }
        return String(String.UnicodeScalarView(stripped))
            .split(separator: " ").joined(separator: " ")
    }

    /// The whole utterance is a snippet trigger → return the expansion verbatim.
    static func exactMatch(_ raw: String) -> String? {
        let n = normalize(raw)
        return parse().first { normalize($0.trigger) == n }?.text
    }

    /// Trigger phrase used inside a sentence → expand in place.
    static func expandInline(_ s: String) -> String {
        var out = s
        for sn in parse() {
            out = rx(out, "\\b" + NSRegularExpression.escapedPattern(for: sn.trigger) + "\\b",
                     NSRegularExpression.escapedTemplate(for: sn.text))
        }
        return out
    }
}

// MARK: - Cleanup without an LLM

enum TextCleanup {
    /// Remove Whisper artifacts like [BLANK_AUDIO], (music) and known silence hallucinations.
    static func stripArtifacts(_ s: String) -> String {
        var t = rx(s, "\\[[^\\]]*\\]", "")
        t = rx(t, "\\((?:music|applause|silence|inaudible|laughs?|musique|música|risas)[^)]*\\)", "")
        t = t.trimmingCharacters(in: .whitespacesAndNewlines)
        let lower = t.lowercased()
        let junk = ["thanks for watching", "thank you for watching", "subtitles by", "amara.org",
                    "sous-titrage", "sous-titres réalisés", "subtítulos realizados", "please subscribe"]
        if junk.contains(where: { lower.contains($0) }) && t.count < 80 { return "" }
        return t
    }

    static func basic(_ s: String) -> String {
        var t = s
        t = rx(t, "[,.]?\\s*\\b(new paragraph|nouveau paragraphe|nuevo párrafo|nuevo parrafo)\\b[,.]?\\s*", "\n\n")
        t = rx(t, "[,.]?\\s*\\b(new line|à la ligne|nouvelle ligne|nueva línea|nueva linea)\\b[,.]?\\s*", "\n")
        t = rx(t, "\\b(u+m+|u+h+|e+r+m+|h+m+|e+u+h+|u+h+m+|mm+)\\b[,.]?\\s*", "")
        t = rx(t, "[ \\t]{2,}", " ")
        t = rx(t, " +([,.!?;:])", "$1")
        t = t.trimmingCharacters(in: .whitespacesAndNewlines)
        if let first = t.first, first.isLowercase {
            t = first.uppercased() + t.dropFirst()
        }
        return t
    }

    /// Clean up common LLM wrapping: <think> blocks, echoed tags, "Here is…:" preambles, quotes.
    static func sanitizeLLM(_ s: String, raw: String) -> String {
        var t = rx(s, "<think>.*?</think>", "", [.caseInsensitive, .dotMatchesLineSeparators])
        t = rx(t, "</?(transcript|selected_text|text)>", "")
        t = rx(t, "^\\s*(here is|here's|voici|aquí está|aqui esta)[^\\n]*:\\s*\\n", "")
        t = t.trimmingCharacters(in: .whitespacesAndNewlines)
        if t.count >= 2, t.hasPrefix("\""), t.hasSuffix("\""), !raw.hasPrefix("\"") {
            t = String(t.dropFirst().dropLast())
        }
        if t.hasPrefix("```") && !raw.lowercased().contains("code") {
            t = rx(t, "^```[a-zA-Z]*\\n?|\\n?```$", "")
        }
        return t.trimmingCharacters(in: .whitespacesAndNewlines)
    }
}

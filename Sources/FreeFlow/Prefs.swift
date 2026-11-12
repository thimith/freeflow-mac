import Foundation
import Carbon.HIToolbox

// MARK: - Option enums

enum TriggerKey: String, CaseIterable, Identifiable {
    case fn, rightOption, rightCommand
    var id: String { rawValue }
    var label: String {
        switch self {
        case .fn: return "fn / 🌐 Globe"
        case .rightOption: return "Right ⌥ Option"
        case .rightCommand: return "Right ⌘ Command"
        }
    }
    var short: String {
        switch self {
        case .fn: return "fn"
        case .rightOption: return "right ⌥"
        case .rightCommand: return "right ⌘"
        }
    }
}

enum KeyCombo: String, CaseIterable, Identifiable {
    case none, optionSpace, controlOptionSpace, commandShiftSpace, controlCommandV, optionShiftV, controlOptionV, controlOptionT, optionShiftT
    var id: String { rawValue }
    var label: String {
        switch self {
        case .none: return "None"
        case .optionSpace: return "⌥ Space"
        case .controlOptionSpace: return "⌃⌥ Space"
        case .commandShiftSpace: return "⇧⌘ Space"
        case .controlCommandV: return "⌃⌘ V"
        case .optionShiftV: return "⌥⇧ V"
        case .controlOptionV: return "⌃⌥ V"
        case .controlOptionT: return "⌃⌥ T"
        case .optionShiftT: return "⌥⇧ T"
        }
    }
    var keyCode: UInt32 {
        switch self {
        case .none: return 0
        case .optionSpace, .controlOptionSpace, .commandShiftSpace: return UInt32(kVK_Space)
        case .controlCommandV, .optionShiftV, .controlOptionV: return UInt32(kVK_ANSI_V)
        case .controlOptionT, .optionShiftT: return UInt32(kVK_ANSI_T)
        }
    }
    var modifiers: UInt32 {
        switch self {
        case .none: return 0
        case .optionSpace: return UInt32(optionKey)
        case .controlOptionSpace: return UInt32(controlKey | optionKey)
        case .commandShiftSpace: return UInt32(cmdKey | shiftKey)
        case .controlCommandV: return UInt32(controlKey | cmdKey)
        case .optionShiftV: return UInt32(optionKey | shiftKey)
        case .controlOptionV: return UInt32(controlKey | optionKey)
        case .controlOptionT: return UInt32(controlKey | optionKey)
        case .optionShiftT: return UInt32(optionKey | shiftKey)
        }
    }
    static let toggleChoices: [KeyCombo] = [.none, .optionSpace, .controlOptionSpace, .commandShiftSpace]
    static let pasteChoices: [KeyCombo] = [.none, .controlCommandV, .optionShiftV, .controlOptionV]
    static let translateChoices: [KeyCombo] = [.none, .controlOptionT, .optionShiftT]
}

enum TranscriptionProvider: String, CaseIterable, Identifiable {
    case localWhisper, apple, groq, custom
    var id: String { rawValue }
    var label: String {
        switch self {
        case .localWhisper: return "Local Whisper — free, offline, best quality"
        case .apple: return "Apple on-device — free, offline, no setup"
        case .groq: return "Groq Whisper — free tier, cloud, fastest"
        case .custom: return "Custom OpenAI-compatible API"
        }
    }
}

enum PolishProvider: String, CaseIterable, Identifiable {
    case off, appleIntelligence, ollama, groq, custom
    var id: String { rawValue }
    var label: String {
        switch self {
        case .off: return "Off — basic cleanup rules only"
        case .appleIntelligence: return "Apple Intelligence — free, on-device (macOS 26)"
        case .ollama: return "Ollama — free, local"
        case .groq: return "Groq Llama — free tier, cloud"
        case .custom: return "Custom OpenAI-compatible API"
        }
    }
}

enum InsertMethod: String, CaseIterable, Identifiable {
    case paste, type
    var id: String { rawValue }
    var label: String { self == .paste ? "Paste (fast, recommended)" : "Simulate typing (for apps that block paste)" }
}

// MARK: - Keys

enum K {
    static let trigger = "triggerKey"
    static let toggleCombo = "toggleCombo"
    static let pasteCombo = "pasteCombo"
    static let transcription = "transcriptionProvider"
    static let polish = "polishProvider"
    static let whisperModel = "whisperModel"
    static let language = "language"
    static let translateTo = "translateTo"
    static let groqKey = "groqKey"
    static let groqWhisperModel = "groqWhisperModel"
    static let groqLLMModel = "groqLLMModel"
    static let ollamaURL = "ollamaURL"
    static let ollamaModel = "ollamaModel"
    static let customBase = "customBase"
    static let customKey = "customKey"
    static let customSTTModel = "customSTTModel"
    static let customLLMModel = "customLLMModel"
    static let contextAware = "contextAware"
    static let customInstructions = "customInstructions"
    static let dictionary = "dictionary"
    static let snippets = "snippets"
    static let sounds = "sounds"
    static let muteMedia = "muteMedia"
    static let restoreClipboard = "restoreClipboard"
    static let insertMethod = "insertMethod"
    static let trailingSpace = "trailingSpace"
    static let showOverlay = "showOverlay"
    static let silenceDetection = "silenceDetection"
    static let didFirstLaunch = "didFirstLaunch"
    static let quickLangs = "quickLangs"
    static let translateCombo = "translateCombo"
    static let translateAfterDictation = "translateAfterDictation"
}

// MARK: - Typed accessors

enum Prefs {
    static var d: UserDefaults { .standard }

    static func registerDefaults() {
        d.register(defaults: [
            K.trigger: TriggerKey.fn.rawValue,
            K.toggleCombo: KeyCombo.optionSpace.rawValue,
            K.pasteCombo: KeyCombo.controlCommandV.rawValue,
            K.transcription: TranscriptionProvider.localWhisper.rawValue,
            K.polish: PolishProvider.off.rawValue,
            K.whisperModel: "large-v3-turbo-q5_0",
            K.language: "auto",
            K.translateTo: "",
            K.groqKey: "",
            K.groqWhisperModel: "whisper-large-v3-turbo",
            K.groqLLMModel: "llama-3.3-70b-versatile",
            K.ollamaURL: "http://localhost:11434/v1",
            K.ollamaModel: "llama3.2",
            K.customBase: "https://api.openai.com/v1",
            K.customKey: "",
            K.customSTTModel: "gpt-4o-mini-transcribe",
            K.customLLMModel: "gpt-4o-mini",
            K.contextAware: true,
            K.customInstructions: "",
            K.dictionary: "",
            K.snippets: "",
            K.sounds: true,
            K.muteMedia: false,
            K.restoreClipboard: true,
            K.insertMethod: InsertMethod.paste.rawValue,
            K.trailingSpace: true,
            K.showOverlay: true,
            K.silenceDetection: true,
            K.quickLangs: "es-CO,en-US,fr-FR,",
            K.translateCombo: KeyCombo.controlOptionT.rawValue,
            K.translateAfterDictation: true,
        ])
    }

    static func str(_ k: String) -> String { d.string(forKey: k) ?? "" }

    static var triggerKey: TriggerKey { TriggerKey(rawValue: str(K.trigger)) ?? .fn }
    static var toggleCombo: KeyCombo { KeyCombo(rawValue: str(K.toggleCombo)) ?? .none }
    static var pasteCombo: KeyCombo { KeyCombo(rawValue: str(K.pasteCombo)) ?? .none }
    static var transcriptionProvider: TranscriptionProvider { TranscriptionProvider(rawValue: str(K.transcription)) ?? .localWhisper }
    static var polishProvider: PolishProvider { PolishProvider(rawValue: str(K.polish)) ?? .off }
    static var whisperModel: String { str(K.whisperModel) }
    static var language: String { let l = str(K.language); return l.isEmpty ? "auto" : l }
    static var translateTo: String { str(K.translateTo) }
    static var groqKey: String { str(K.groqKey).trimmingCharacters(in: .whitespacesAndNewlines) }
    static var groqWhisperModel: String { str(K.groqWhisperModel) }
    static var groqLLMModel: String { str(K.groqLLMModel) }
    static var ollamaURL: String { str(K.ollamaURL) }
    static var ollamaModel: String { str(K.ollamaModel) }
    static var customBase: String { str(K.customBase) }
    static var customKey: String { str(K.customKey).trimmingCharacters(in: .whitespacesAndNewlines) }
    static var customSTTModel: String { str(K.customSTTModel) }
    static var customLLMModel: String { str(K.customLLMModel) }
    static var contextAware: Bool { d.bool(forKey: K.contextAware) }
    static var customInstructions: String { str(K.customInstructions) }
    static var dictionary: String { str(K.dictionary) }
    static var snippets: String { str(K.snippets) }
    static var sounds: Bool { d.bool(forKey: K.sounds) }
    static var muteMedia: Bool { d.bool(forKey: K.muteMedia) }
    static var restoreClipboard: Bool { d.bool(forKey: K.restoreClipboard) }
    static var insertMethod: InsertMethod { InsertMethod(rawValue: str(K.insertMethod)) ?? .paste }
    static var trailingSpace: Bool { d.bool(forKey: K.trailingSpace) }
    static var showOverlay: Bool { d.bool(forKey: K.showOverlay) }
    static var silenceDetection: Bool { d.bool(forKey: K.silenceDetection) }
    static var translateCombo: KeyCombo { KeyCombo(rawValue: str(K.translateCombo)) ?? .none }
    static var translateAfterDictation: Bool { d.bool(forKey: K.translateAfterDictation) }

    /// Four quick translation slots ("" = empty).
    static var quickLangs: [String] {
        var parts = str(K.quickLangs).components(separatedBy: ",")
        while parts.count < 4 { parts.append("") }
        return Array(parts.prefix(4))
    }
    static func setQuickLang(_ slot: Int, _ id: String) {
        var q = quickLangs
        q[slot] = id
        let filled = q.filter { !$0.isEmpty }   // keep filled slots first
        d.set((filled + Array(repeating: "", count: 4 - filled.count)).joined(separator: ","), forKey: K.quickLangs)
    }
}

// MARK: - Translation targets (same list and regional varieties as the Android bubble)

struct TLang: Identifiable, Hashable {
    let id: String, flag: String, label: String, prompt: String
    var short: String { label.components(separatedBy: " (").first ?? label }

    static let all: [TLang] = [
        TLang(id: "es-CO", flag: "🇨🇴", label: "Español (Colombia)", prompt: "Colombian Spanish, with the natural everyday words and expressions people use in Colombia (paisa/Medellín register when informal)"),
        TLang(id: "es-419", flag: "🌎", label: "Español (Latinoamérica)", prompt: "neutral Latin American Spanish"),
        TLang(id: "es-MX", flag: "🇲🇽", label: "Español (México)", prompt: "Mexican Spanish with natural Mexican expressions"),
        TLang(id: "es-AR", flag: "🇦🇷", label: "Español (Argentina)", prompt: "Rioplatense (Argentine) Spanish using voseo (vos)"),
        TLang(id: "es-ES", flag: "🇪🇸", label: "Español (España)", prompt: "Peninsular Spanish from Spain, using vosotros"),
        TLang(id: "en-US", flag: "🇺🇸", label: "English (US)", prompt: "American English"),
        TLang(id: "en-GB", flag: "🇬🇧", label: "English (UK)", prompt: "British English with British spelling"),
        TLang(id: "fr-FR", flag: "🇫🇷", label: "Français (France)", prompt: "French as spoken in France"),
        TLang(id: "fr-CA", flag: "🇨🇦", label: "Français (Québec)", prompt: "Québécois French with natural Québec expressions"),
        TLang(id: "pt-BR", flag: "🇧🇷", label: "Português (Brasil)", prompt: "Brazilian Portuguese"),
        TLang(id: "pt-PT", flag: "🇵🇹", label: "Português (Portugal)", prompt: "European Portuguese"),
        TLang(id: "de", flag: "🇩🇪", label: "Deutsch", prompt: "German"),
        TLang(id: "it", flag: "🇮🇹", label: "Italiano", prompt: "Italian"),
        TLang(id: "nl", flag: "🇳🇱", label: "Nederlands", prompt: "Dutch"),
        TLang(id: "zh", flag: "🇨🇳", label: "中文", prompt: "Simplified Chinese"),
        TLang(id: "ja", flag: "🇯🇵", label: "日本語", prompt: "Japanese"),
        TLang(id: "ko", flag: "🇰🇷", label: "한국어", prompt: "Korean"),
        TLang(id: "ar", flag: "🇸🇦", label: "العربية", prompt: "Modern Standard Arabic"),
        TLang(id: "hi", flag: "🇮🇳", label: "हिन्दी", prompt: "Hindi"),
        TLang(id: "ru", flag: "🇷🇺", label: "Русский", prompt: "Russian"),
        TLang(id: "tr", flag: "🇹🇷", label: "Türkçe", prompt: "Turkish"),
        TLang(id: "pl", flag: "🇵🇱", label: "Polski", prompt: "Polish"),
        TLang(id: "id", flag: "🇮🇩", label: "Indonesia", prompt: "Indonesian"),
        TLang(id: "vi", flag: "🇻🇳", label: "Tiếng Việt", prompt: "Vietnamese"),
        TLang(id: "th", flag: "🇹🇭", label: "ไทย", prompt: "Thai"),
        TLang(id: "sv", flag: "🇸🇪", label: "Svenska", prompt: "Swedish"),
        TLang(id: "uk", flag: "🇺🇦", label: "Українська", prompt: "Ukrainian"),
    ]

    static func byID(_ id: String) -> TLang? { all.first { $0.id == id } }
    static var quick: [TLang] { Prefs.quickLangs.compactMap { byID($0) } }
}

// MARK: - Languages

struct Lang: Identifiable {
    let code: String
    let name: String
    let english: String
    var id: String { code }
}

enum Languages {
    static let all: [Lang] = [
        Lang(code: "auto", name: "Auto-detect", english: ""),
        Lang(code: "en", name: "English", english: "English"),
        Lang(code: "fr", name: "Français", english: "French"),
        Lang(code: "es", name: "Español", english: "Spanish"),
        Lang(code: "pt", name: "Português", english: "Portuguese"),
        Lang(code: "de", name: "Deutsch", english: "German"),
        Lang(code: "it", name: "Italiano", english: "Italian"),
        Lang(code: "nl", name: "Nederlands", english: "Dutch"),
        Lang(code: "pl", name: "Polski", english: "Polish"),
        Lang(code: "ru", name: "Русский", english: "Russian"),
        Lang(code: "uk", name: "Українська", english: "Ukrainian"),
        Lang(code: "tr", name: "Türkçe", english: "Turkish"),
        Lang(code: "ar", name: "العربية", english: "Arabic"),
        Lang(code: "hi", name: "हिन्दी", english: "Hindi"),
        Lang(code: "ja", name: "日本語", english: "Japanese"),
        Lang(code: "ko", name: "한국어", english: "Korean"),
        Lang(code: "zh", name: "中文", english: "Chinese"),
    ]

    static let appleLocales: [String: String] = [
        "en": "en-US", "fr": "fr-FR", "es": "es-ES", "pt": "pt-BR", "de": "de-DE", "it": "it-IT",
        "nl": "nl-NL", "pl": "pl-PL", "ru": "ru-RU", "uk": "uk-UA", "tr": "tr-TR", "ar": "ar-SA",
        "hi": "hi-IN", "ja": "ja-JP", "ko": "ko-KR", "zh": "zh-CN",
    ]
}

// MARK: - Errors / paths

enum FFError: LocalizedError {
    case message(String)
    var errorDescription: String? {
        switch self { case .message(let m): return m }
    }
}

enum AppPaths {
    static var support: URL {
        let u = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("FreeFlow", isDirectory: true)
        try? FileManager.default.createDirectory(at: u, withIntermediateDirectories: true)
        return u
    }
}

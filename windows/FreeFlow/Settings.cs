using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreeFlow;

public enum TriggerKey { CtrlWin, RightCtrl, RightAlt, CapsLock }
public enum Combo { None, CtrlAltSpace, CtrlShiftSpace, AltShiftSpace, CtrlAltV, CtrlShiftV, AltShiftV, CtrlAltT, AltShiftT }
public enum SttProvider { LocalWhisper, Groq, Custom }
public enum AiProvider { Off, Ollama, Groq, Custom }
public enum InsertMethod { Paste, Type }

public static class Labels
{
    public static string Of(TriggerKey k) => k switch
    {
        TriggerKey.CtrlWin => "Ctrl + Win (hold both)",
        TriggerKey.RightCtrl => "Right Ctrl",
        TriggerKey.RightAlt => "Right Alt / AltGr",
        TriggerKey.CapsLock => "Caps Lock",
        _ => k.ToString(),
    };
    public static string Short(TriggerKey k) => k switch
    {
        TriggerKey.CtrlWin => "Ctrl+Win",
        TriggerKey.RightCtrl => "Right Ctrl",
        TriggerKey.RightAlt => "Right Alt",
        TriggerKey.CapsLock => "Caps Lock",
        _ => k.ToString(),
    };
    public static string Of(Combo c) => c switch
    {
        Combo.None => "None",
        Combo.CtrlAltSpace => "Ctrl + Alt + Space",
        Combo.CtrlShiftSpace => "Ctrl + Shift + Space",
        Combo.AltShiftSpace => "Alt + Shift + Space",
        Combo.CtrlAltV => "Ctrl + Alt + V",
        Combo.CtrlShiftV => "Ctrl + Shift + V",
        Combo.AltShiftV => "Alt + Shift + V",
        Combo.CtrlAltT => "Ctrl + Alt + T",
        Combo.AltShiftT => "Alt + Shift + T",
        _ => c.ToString(),
    };
    public static string Of(SttProvider p) => p switch
    {
        SttProvider.LocalWhisper => "Local Whisper — free, offline, private",
        SttProvider.Groq => "Groq Whisper — free tier, cloud, fastest",
        SttProvider.Custom => "Custom OpenAI-compatible API",
        _ => p.ToString(),
    };
    public static string Of(AiProvider p) => p switch
    {
        AiProvider.Off => "Off — basic cleanup rules only",
        AiProvider.Ollama => "Ollama — free, local",
        AiProvider.Groq => "Groq Llama — free tier, cloud, fastest",
        AiProvider.Custom => "Custom OpenAI-compatible API",
        _ => p.ToString(),
    };
    public static string Of(InsertMethod m) => m == InsertMethod.Paste
        ? "Paste (fast, recommended)" : "Simulate typing (for apps that block paste)";

    public static (uint mods, uint vk) Keys(Combo c)
    {
        const uint ALT = 1, CTRL = 2, SHIFT = 4;
        return c switch
        {
            Combo.CtrlAltSpace => (CTRL | ALT, 0x20),
            Combo.CtrlShiftSpace => (CTRL | SHIFT, 0x20),
            Combo.AltShiftSpace => (ALT | SHIFT, 0x20),
            Combo.CtrlAltV => (CTRL | ALT, 0x56),
            Combo.CtrlShiftV => (CTRL | SHIFT, 0x56),
            Combo.AltShiftV => (ALT | SHIFT, 0x56),
            Combo.CtrlAltT => (CTRL | ALT, 0x54),
            Combo.AltShiftT => (ALT | SHIFT, 0x54),
            _ => (0, 0),
        };
    }
    public static readonly Combo[] ToggleChoices = { Combo.None, Combo.CtrlAltSpace, Combo.CtrlShiftSpace, Combo.AltShiftSpace };
    public static readonly Combo[] PasteChoices = { Combo.None, Combo.CtrlAltV, Combo.CtrlShiftV, Combo.AltShiftV };
    public static readonly Combo[] TranslateChoices = { Combo.None, Combo.CtrlAltT, Combo.AltShiftT };
}

public sealed class Settings
{
    public TriggerKey Trigger { get; set; } = TriggerKey.CtrlWin;
    public Combo ToggleCombo { get; set; } = Combo.CtrlAltSpace;
    public Combo PasteCombo { get; set; } = Combo.CtrlAltV;
    public SttProvider Stt { get; set; } = SttProvider.LocalWhisper;
    public AiProvider Ai { get; set; } = AiProvider.Off;
    public string WhisperModel { get; set; } = "large-v3-turbo-q5_0";
    public string Language { get; set; } = "auto";
    public string TranslateTo { get; set; } = "";
    public string GroqKey { get; set; } = "";
    public string GroqWhisperModel { get; set; } = "whisper-large-v3-turbo";
    public string GroqLlmModel { get; set; } = "llama-3.3-70b-versatile";
    public string OllamaUrl { get; set; } = "http://localhost:11434/v1";
    public string OllamaModel { get; set; } = "llama3.2";
    public string CustomBase { get; set; } = "https://api.openai.com/v1";
    public string CustomKey { get; set; } = "";
    public string CustomSttModel { get; set; } = "gpt-4o-mini-transcribe";
    public string CustomLlmModel { get; set; } = "gpt-4o-mini";
    public bool ContextAware { get; set; } = true;
    public string CustomInstructions { get; set; } = "";
    public string Dictionary { get; set; } = "";
    public string Snippets { get; set; } = "";
    public bool Sounds { get; set; } = true;
    public bool MuteMedia { get; set; } = false;
    public bool RestoreClipboard { get; set; } = true;
    public InsertMethod Insert { get; set; } = InsertMethod.Paste;
    public bool TrailingSpace { get; set; } = true;
    public bool ShowOverlay { get; set; } = true;
    public bool SilenceDetection { get; set; } = true;
    public int MicDevice { get; set; } = -1;   // -1 = Windows default
    public Combo TranslateCombo { get; set; } = Combo.CtrlAltT;
    public bool TranslateAfterDictation { get; set; } = true;
    public bool ShowDock { get; set; } = true;
    public int DockX { get; set; } = -1;
    public int DockY { get; set; } = -1;
    public string[] QuickLangs { get; set; } = { "es-CO", "en-US", "fr-FR", "" };

    [JsonIgnore] public static bool IsFirstRun { get; private set; }
    [JsonIgnore] public static Settings Current { get; } = Load();
    public static event Action Changed;

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    static string FilePath => Path.Combine(Paths.AppData, "settings.json");

    static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Opts) ?? new Settings();
        }
        catch { /* corrupted file → defaults */ }
        IsFirstRun = true;
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts)); } catch { }
        Changed?.Invoke();
    }
}

public static class Paths
{
    public static string AppData
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FreeFlow");
            Directory.CreateDirectory(d);
            return d;
        }
    }
    public static string Models
    {
        get { var d = Path.Combine(AppData, "models"); Directory.CreateDirectory(d); return d; }
    }
    public static string WhisperDir => Path.Combine(System.AppContext.BaseDirectory, "whisper");
}

public record Lang(string Code, string Name, string English);

/// Translation targets with regional varieties (same list as the Android bubble and the Mac app).
public record TLang(string Id, string Flag, string Label, string Prompt)
{
    public string Short => Label.Contains(" (") ? Label[..Label.IndexOf(" (")] : Label;

    public static readonly TLang[] All =
    {
        new("es-CO", "🇨🇴", "Español (Colombia)", "Colombian Spanish, with the natural everyday words and expressions people use in Colombia (paisa/Medellín register when informal)"),
        new("es-419", "🌎", "Español (Latinoamérica)", "neutral Latin American Spanish"),
        new("es-MX", "🇲🇽", "Español (México)", "Mexican Spanish with natural Mexican expressions"),
        new("es-AR", "🇦🇷", "Español (Argentina)", "Rioplatense (Argentine) Spanish using voseo (vos)"),
        new("es-ES", "🇪🇸", "Español (España)", "Peninsular Spanish from Spain, using vosotros"),
        new("en-US", "🇺🇸", "English (US)", "American English"),
        new("en-GB", "🇬🇧", "English (UK)", "British English with British spelling"),
        new("fr-FR", "🇫🇷", "Français (France)", "French as spoken in France"),
        new("fr-CA", "🇨🇦", "Français (Québec)", "Québécois French with natural Québec expressions"),
        new("pt-BR", "🇧🇷", "Português (Brasil)", "Brazilian Portuguese"),
        new("pt-PT", "🇵🇹", "Português (Portugal)", "European Portuguese"),
        new("de", "🇩🇪", "Deutsch", "German"),
        new("it", "🇮🇹", "Italiano", "Italian"),
        new("nl", "🇳🇱", "Nederlands", "Dutch"),
        new("zh", "🇨🇳", "中文", "Simplified Chinese"),
        new("ja", "🇯🇵", "日本語", "Japanese"),
        new("ko", "🇰🇷", "한국어", "Korean"),
        new("ar", "🇸🇦", "العربية", "Modern Standard Arabic"),
        new("hi", "🇮🇳", "हिन्दी", "Hindi"),
        new("ru", "🇷🇺", "Русский", "Russian"),
        new("tr", "🇹🇷", "Türkçe", "Turkish"),
        new("pl", "🇵🇱", "Polski", "Polish"),
        new("id", "🇮🇩", "Indonesia", "Indonesian"),
        new("vi", "🇻🇳", "Tiếng Việt", "Vietnamese"),
        new("th", "🇹🇭", "ไทย", "Thai"),
        new("sv", "🇸🇪", "Svenska", "Swedish"),
        new("uk", "🇺🇦", "Українська", "Ukrainian"),
    };

    public static TLang ById(string id) => All.FirstOrDefault(l => l.Id == id);

    public static List<TLang> Quick()
    {
        var q = Settings.Current.QuickLangs ?? Array.Empty<string>();
        return q.Select(ById).Where(l => l != null).ToList();
    }
}

public static class Languages
{
    public static readonly Lang[] All =
    {
        new("auto", "Auto-detect", ""),
        new("en", "English", "English"), new("fr", "Français", "French"), new("es", "Español", "Spanish"),
        new("pt", "Português", "Portuguese"), new("de", "Deutsch", "German"), new("it", "Italiano", "Italian"),
        new("nl", "Nederlands", "Dutch"), new("pl", "Polski", "Polish"), new("ru", "Русский", "Russian"),
        new("uk", "Українська", "Ukrainian"), new("tr", "Türkçe", "Turkish"), new("ar", "العربية", "Arabic"),
        new("hi", "हिन्दी", "Hindi"), new("ja", "日本語", "Japanese"), new("ko", "한국어", "Korean"),
        new("zh", "中文", "Chinese"),
    };
}

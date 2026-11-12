package com.freeflow.bubble;

/** Translation targets, including regional varieties (same idea as the FreeFlow keyboard). */
public final class Langs {
    public static final class Lang {
        public final String id, flag, label, prompt;
        Lang(String id, String flag, String label, String prompt) { this.id = id; this.flag = flag; this.label = label; this.prompt = prompt; }
        public String chip() { return flag + "  " + label; }
    }

    public static final Lang[] ALL = {
            new Lang("es-CO", "🇨🇴", "Español (Colombia)", "Colombian Spanish, with the natural everyday words and expressions people use in Colombia (paisa/Medellín register when informal)"),
            new Lang("es-419", "🌎", "Español (Latinoamérica)", "neutral Latin American Spanish"),
            new Lang("es-MX", "🇲🇽", "Español (México)", "Mexican Spanish with natural Mexican expressions"),
            new Lang("es-AR", "🇦🇷", "Español (Argentina)", "Rioplatense (Argentine) Spanish using voseo (vos)"),
            new Lang("es-ES", "🇪🇸", "Español (España)", "Peninsular Spanish from Spain, using vosotros"),
            new Lang("en-US", "🇺🇸", "English (US)", "American English"),
            new Lang("en-GB", "🇬🇧", "English (UK)", "British English with British spelling"),
            new Lang("fr-FR", "🇫🇷", "Français (France)", "French as spoken in France"),
            new Lang("fr-CA", "🇨🇦", "Français (Québec)", "Québécois French with natural Québec expressions"),
            new Lang("pt-BR", "🇧🇷", "Português (Brasil)", "Brazilian Portuguese"),
            new Lang("pt-PT", "🇵🇹", "Português (Portugal)", "European Portuguese"),
            new Lang("de", "🇩🇪", "Deutsch", "German"),
            new Lang("it", "🇮🇹", "Italiano", "Italian"),
            new Lang("nl", "🇳🇱", "Nederlands", "Dutch"),
            new Lang("zh", "🇨🇳", "中文", "Simplified Chinese"),
            new Lang("ja", "🇯🇵", "日本語", "Japanese"),
            new Lang("ko", "🇰🇷", "한국어", "Korean"),
            new Lang("ar", "🇸🇦", "العربية", "Modern Standard Arabic"),
            new Lang("hi", "🇮🇳", "हिन्दी", "Hindi"),
            new Lang("ru", "🇷🇺", "Русский", "Russian"),
            new Lang("tr", "🇹🇷", "Türkçe", "Turkish"),
            new Lang("pl", "🇵🇱", "Polski", "Polish"),
            new Lang("id", "🇮🇩", "Indonesia", "Indonesian"),
            new Lang("vi", "🇻🇳", "Tiếng Việt", "Vietnamese"),
            new Lang("th", "🇹🇭", "ไทย", "Thai"),
            new Lang("sv", "🇸🇪", "Svenska", "Swedish"),
            new Lang("uk", "🇺🇦", "Українська", "Ukrainian"),
    };

    /** Languages you can dictate in (Whisper codes). */
    public static final String[][] SPOKEN = {
            {"auto", "Auto-detect"}, {"es", "Español"}, {"en", "English"}, {"fr", "Français"}, {"pt", "Português"},
            {"de", "Deutsch"}, {"it", "Italiano"}, {"nl", "Nederlands"}, {"zh", "中文"}, {"ja", "日本語"},
            {"ko", "한국어"}, {"ar", "العربية"}, {"hi", "हिन्दी"}, {"ru", "Русский"}, {"tr", "Türkçe"}, {"pl", "Polski"},
    };

    public static Lang byId(String id) {
        if (id == null) return null;
        for (Lang l : ALL) if (l.id.equals(id)) return l;
        return null;
    }
}

package com.freeflow.bubble;

import android.content.Context;
import android.content.SharedPreferences;

/** All settings, stored in SharedPreferences. */
public final class Prefs {
    private final SharedPreferences p;

    public Prefs(Context c) { p = c.getSharedPreferences("freeflow", Context.MODE_PRIVATE); }

    public String groqKey() { return p.getString("groqKey", "").trim(); }
    public void groqKey(String v) { p.edit().putString("groqKey", v.trim()).apply(); }

    /** "auto" or an ISO code like "es". */
    public String spokenLanguage() { return p.getString("spoken", "auto"); }
    public void spokenLanguage(String v) { p.edit().putString("spoken", v).apply(); }

    public String whisperModel() { return p.getString("whisperModel", "whisper-large-v3-turbo"); }
    public void whisperModel(String v) { p.edit().putString("whisperModel", v).apply(); }
    public String llmModel() { return p.getString("llmModel", "llama-3.3-70b-versatile"); }
    public void llmModel(String v) { p.edit().putString("llmModel", v).apply(); }

    /** Off by default: dictation comes back exactly as Whisper heard it (minus artifacts). */
    public boolean aiCleanup() { return p.getBoolean("aiCleanup", false); }
    public void aiCleanup(boolean v) { p.edit().putBoolean("aiCleanup", v).apply(); }

    public boolean onlyWithKeyboard() { return p.getBoolean("onlyWithKeyboard", true); }
    public void onlyWithKeyboard(boolean v) { p.edit().putBoolean("onlyWithKeyboard", v).apply(); }

    public boolean largeBubble() { return p.getBoolean("largeBubble", false); }
    public void largeBubble(boolean v) { p.edit().putBoolean("largeBubble", v).apply(); }

    public boolean haptics() { return p.getBoolean("haptics", true); }
    public void haptics(boolean v) { p.edit().putBoolean("haptics", v).apply(); }

    /** Up to 4 quick translation targets (Langs ids). */
    public String[] quickLangs() {
        String[] out = new String[4];
        String[] def = {"es-CO", "en-US", "fr-FR", ""};
        for (int i = 0; i < 4; i++) out[i] = p.getString("quick" + i, def[i]);
        return out;
    }
    public void quickLang(int i, String id) { p.edit().putString("quick" + i, id).apply(); }

    public int bubbleY(int def) { return p.getInt("bubbleY", def); }
    public boolean bubbleLeft() { return p.getBoolean("bubbleLeft", false); }
    public void bubblePos(int y, boolean left) { p.edit().putInt("bubbleY", y).putBoolean("bubbleLeft", left).apply(); }
    public void resetBubble() { p.edit().remove("bubbleY").remove("bubbleLeft").apply(); }
}

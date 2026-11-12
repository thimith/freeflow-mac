package com.freeflow.bubble;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.ComponentName;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.res.ColorStateList;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.PowerManager;
import android.provider.Settings;
import android.text.Editable;
import android.text.InputType;
import android.text.TextWatcher;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.widget.EditText;
import android.widget.HorizontalScrollView;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

/** Setup checklist + settings, in the same dark/gradient look as the bubble. */
public class MainActivity extends Activity {
    // palette
    static final int BG = 0xFF0E0E14, CARD = 0xFF1A1A24, CARD2 = 0xFF242432, LINE = 0x22FFFFFF;
    static final int TEXT = 0xFFFFFFFF, MUTED = 0xFF9A9AB2;
    static final int PURPLE = 0xFF7C4DFF, BLUE = 0xFF286EFF, PINK = 0xFFEC4899, GREEN = 0xFF30C759, ORANGE = 0xFFFF9F0A;

    private Prefs prefs;
    private LinearLayout checklist, langSlots, spokenChips;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        prefs = new Prefs(this);
        RecorderService.ensureChannel(this);
        getWindow().setStatusBarColor(BG);
        getWindow().setNavigationBarColor(BG);

        ScrollView sv = new ScrollView(this);
        sv.setBackgroundColor(BG);
        sv.setFitsSystemWindows(true);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(16), dp(16), dp(16), dp(40));
        sv.addView(root);
        setContentView(sv);

        root.addView(hero());

        // ── setup
        LinearLayout setup = card(root, "🚀", "Setup", "A few one-time steps so the bubble works everywhere");
        checklist = new LinearLayout(this);
        checklist.setOrientation(LinearLayout.VERTICAL);
        setup.addView(checklist);

        // ── key
        LinearLayout key = card(root, "🔑", "Groq API key", "Free — powers voice, translation and ✨ edits");
        EditText keyField = field("gsk_…", prefs.groqKey(), true);
        keyField.addTextChangedListener(new Watcher(s -> { prefs.groqKey(s); buildChecklist(); }));
        key.addView(keyField);
        LinearLayout keyBtns = hrow();
        keyBtns.setPadding(0, dp(10), 0, 0);
        keyBtns.addView(pill("Paste", true, v -> {
            ClipboardManager cm = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
            ClipData clip = cm.getPrimaryClip();
            if (clip != null && clip.getItemCount() > 0) keyField.setText(clip.getItemAt(0).coerceToText(this).toString().trim());
        }));
        keyBtns.addView(pill("Test", false, v -> testKey()));
        keyBtns.addView(pill("Get a free key", false, v -> open("https://console.groq.com/keys")));
        key.addView(scroll(keyBtns));

        // ── voice
        LinearLayout voice = card(root, "🎙", "Voice", "How you talk to the bubble");
        voice.addView(label("I speak"));
        spokenChips = hrow();
        voice.addView(scroll(spokenChips));
        buildSpoken();
        voice.addView(note("Auto-detect follows you between French, English and Spanish."));
        voice.addView(toggle("AI cleanup", "Removes ums, applies self-corrections (“3pm, no actually 4pm” → 4pm), fixes punctuation",
                prefs.aiCleanup(), PURPLE, prefs::aiCleanup));

        // ── translation
        LinearLayout tr = card(root, "🌐", "Translation languages", "Your quick tiles in the 文A menu — up to 4");
        langSlots = new LinearLayout(this);
        langSlots.setOrientation(LinearLayout.VERTICAL);
        tr.addView(langSlots);
        buildSlots();
        tr.addView(note("Every other language stays one tap away under “More languages”."));

        // ── bubble
        LinearLayout bub = card(root, "🫧", "Bubble", "Look and behaviour");
        bub.addView(toggle("Only while typing", "Show the bubble only when the keyboard is open", prefs.onlyWithKeyboard(), BLUE,
                v -> { prefs.onlyWithKeyboard(v); FlowService.reload(); }));
        bub.addView(toggle("Large bubble", "Bigger buttons, easier to hit", prefs.largeBubble(), BLUE,
                v -> { prefs.largeBubble(v); FlowService.reload(); }));
        bub.addView(toggle("Vibrate on tap", null, prefs.haptics(), BLUE, prefs::haptics));
        LinearLayout r = hrow();
        r.setPadding(0, dp(6), 0, 0);
        r.addView(pill("Reset bubble position", false, v -> { prefs.resetBubble(); FlowService.reload(); toast("Bubble moved back to the right edge"); }));
        bub.addView(r);

        // ── try it
        LinearLayout tryCard = card(root, "✍️", "Try it", "Tap the box, then use the bubble");
        EditText tryIt = field("Say something…", "", false);
        tryIt.setMinLines(3);
        tryIt.setGravity(Gravity.TOP | Gravity.START);
        tryCard.addView(tryIt);

        TextView ver = note("FreeFlow Bubble v" + versionName());
        ver.setGravity(Gravity.CENTER);
        ver.setPadding(0, dp(20), 0, 0);
        root.addView(ver);
    }

    @Override
    protected void onResume() {
        super.onResume();
        buildChecklist();
    }

    // ───────── hero ─────────

    private View hero() {
        LinearLayout h = new LinearLayout(this);
        h.setOrientation(LinearLayout.VERTICAL);
        h.setPadding(dp(20), dp(22), dp(20), dp(20));
        GradientDrawable g = new GradientDrawable(GradientDrawable.Orientation.TL_BR, new int[]{PURPLE, BLUE});
        g.setCornerRadius(dp(26));
        h.setBackground(g);

        LinearLayout top = hrow();
        ImageView icon = new ImageView(this);
        icon.setImageResource(R.mipmap.ic_launcher);
        icon.setClipToOutline(true);
        GradientDrawable round = new GradientDrawable();
        round.setCornerRadius(dp(14));
        icon.setBackground(round);
        top.addView(icon, new LinearLayout.LayoutParams(dp(52), dp(52)));
        LinearLayout titles = new LinearLayout(this);
        titles.setOrientation(LinearLayout.VERTICAL);
        titles.setPadding(dp(14), 0, 0, 0);
        titles.addView(text("FreeFlow Bubble", 24, true, TEXT));
        titles.addView(text("Talk instead of type — in any app", 14, false, 0xDDFFFFFF));
        top.addView(titles);
        h.addView(top);

        LinearLayout legend = new LinearLayout(this);
        legend.setOrientation(LinearLayout.VERTICAL);
        legend.setPadding(0, dp(16), 0, 0);
        legend.addView(legendRow(PINK, "✨", "Improve your text by voice"));
        legend.addView(legendRow(0xFF404050, "文A", "Translate what you typed"));
        legend.addView(legendRow(0xFF5B5BFF, "🎙", "Tap to talk · hold to push-to-talk"));
        h.addView(legend);
        return h;
    }

    private View legendRow(int color, String glyph, String what) {
        LinearLayout r = hrow();
        r.setPadding(0, dp(5), 0, dp(5));
        TextView dot = text(glyph, 14, true, TEXT);
        dot.setGravity(Gravity.CENTER);
        GradientDrawable g = new GradientDrawable();
        g.setShape(GradientDrawable.OVAL);
        g.setColor(color);
        g.setStroke(dp(2), 0x55FFFFFF);
        dot.setBackground(g);
        r.addView(dot, new LinearLayout.LayoutParams(dp(34), dp(34)));
        TextView l = text(what, 15, false, TEXT);
        l.setPadding(dp(12), 0, 0, 0);
        r.addView(l);
        return r;
    }

    // ───────── setup checklist ─────────

    private void buildChecklist() {
        if (checklist == null) return;
        checklist.removeAllViews();
        boolean a11y = accessibilityOn();
        boolean micOk = checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED;
        boolean notifOk = Build.VERSION.SDK_INT < 33 || checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED;
        boolean battery = ((PowerManager) getSystemService(POWER_SERVICE)).isIgnoringBatteryOptimizations(getPackageName());
        boolean keyOk = !prefs.groqKey().isEmpty();

        checklist.addView(step(micOk, "Microphone", "Grant", v -> requestPermissions(new String[]{Manifest.permission.RECORD_AUDIO}, 1)));
        checklist.addView(step(notifOk, "Notifications", "Grant", v -> {
            if (Build.VERSION.SDK_INT >= 33) requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 2);
        }));
        checklist.addView(step(a11y, "Turn on FreeFlow Bubble in Accessibility", "Open",
                v -> startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))));
        if (!a11y) {
            checklist.addView(note("Switch greyed out (“Restricted setting”)? Tap App info → ⋮ (top right) → “Allow restricted settings”, then try again."));
            LinearLayout r = hrow();
            r.addView(pill("App info", false, v -> appInfo()));
            checklist.addView(r);
        }
        checklist.addView(step(battery, "Run in the background", "Allow", v -> {
            try {
                startActivity(new Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:" + getPackageName())));
            } catch (Exception e) { appInfo(); }
        }));
        if ("samsung".equalsIgnoreCase(Build.MANUFACTURER)) {
            checklist.addView(step(false, "Samsung: “Never sleeping apps”", "Open", v -> openSamsungBattery()));
            checklist.addView(note("Battery → Background usage limits → Never sleeping apps → + → FreeFlow Bubble. Stops One UI switching the bubble off."));
        }
        checklist.addView(step(keyOk, "Groq key added", null, null));
        if (micOk && a11y && battery && keyOk) {
            TextView done = text("✅  All set — tap the box in “Try it” and the bubble appears.", 14, true, GREEN);
            done.setPadding(0, dp(8), 0, 0);
            checklist.addView(done);
        }
    }

    private View step(boolean done, String title, String action, View.OnClickListener l) {
        LinearLayout r = hrow();
        r.setPadding(dp(12), dp(10), dp(10), dp(10));
        GradientDrawable g = new GradientDrawable();
        g.setCornerRadius(dp(14));
        g.setColor(done ? 0x2230C759 : 0x22FF9F0A);
        g.setStroke(dp(1), done ? 0x5530C759 : 0x55FF9F0A);
        r.setBackground(g);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2);
        lp.bottomMargin = dp(8);
        r.setLayoutParams(lp);
        TextView badge = text(done ? "✓" : "!", 14, true, done ? GREEN : ORANGE);
        badge.setGravity(Gravity.CENTER);
        GradientDrawable bg = new GradientDrawable();
        bg.setShape(GradientDrawable.OVAL);
        bg.setColor(done ? 0x3330C759 : 0x33FF9F0A);
        badge.setBackground(bg);
        r.addView(badge, new LinearLayout.LayoutParams(dp(26), dp(26)));
        TextView t = text(title, 15, false, done ? MUTED : TEXT);
        t.setPadding(dp(12), 0, dp(8), 0);
        r.addView(t, new LinearLayout.LayoutParams(0, -2, 1));
        if (!done && action != null) r.addView(pill(action, true, l));
        return r;
    }

    // ───────── spoken language chips ─────────

    private void buildSpoken() {
        spokenChips.removeAllViews();
        String cur = prefs.spokenLanguage();
        for (String[] l : Langs.SPOKEN) {
            boolean on = l[0].equals(cur);
            TextView chip = chip(l[1], on);
            chip.setOnClickListener(v -> { prefs.spokenLanguage(l[0]); buildSpoken(); });
            spokenChips.addView(chip);
        }
    }

    // ───────── translation slots ─────────

    private void buildSlots() {
        langSlots.removeAllViews();
        String[] q = prefs.quickLangs();
        for (int i = 0; i < 4; i++) {
            final int slot = i;
            Langs.Lang l = Langs.byId(q[i]);
            if (l == null) {
                TextView add = text("+  Add a language", 15, true, 0xFFB9A6FF);
                add.setGravity(Gravity.CENTER);
                add.setPadding(0, dp(16), 0, dp(16));
                GradientDrawable g = new GradientDrawable();
                g.setCornerRadius(dp(16));
                g.setColor(0x0FFFFFFF);
                g.setStroke(dp(2), 0x557C4DFF, dp(8), dp(6));
                add.setBackground(g);
                add.setOnClickListener(v -> pickLanguage(slot));
                langSlots.addView(add, slotLp());
                continue;
            }
            LinearLayout row = hrow();
            row.setPadding(dp(14), dp(12), dp(10), dp(12));
            GradientDrawable g = new GradientDrawable();
            g.setCornerRadius(dp(16));
            g.setColor(CARD2);
            g.setStroke(dp(1), LINE);
            row.setBackground(g);
            row.addView(text(l.flag, 30, false, TEXT));
            LinearLayout names = new LinearLayout(this);
            names.setOrientation(LinearLayout.VERTICAL);
            names.setPadding(dp(14), 0, dp(6), 0);
            int paren = l.label.indexOf(" (");
            names.addView(text(paren > 0 ? l.label.substring(0, paren) : l.label, 17, true, TEXT));
            if (paren > 0) names.addView(text(l.label.substring(paren + 2, l.label.length() - 1), 13, false, MUTED));
            row.addView(names, new LinearLayout.LayoutParams(0, -2, 1));
            row.addView(pill("Change", false, v -> pickLanguage(slot)));
            TextView x = text("✕", 16, true, MUTED);
            x.setPadding(dp(12), dp(6), dp(6), dp(6));
            x.setOnClickListener(v -> { prefs.quickLang(slot, ""); compactSlots(); buildSlots(); });
            row.addView(x);
            langSlots.addView(row, slotLp());
        }
    }

    /** Keep filled slots at the top after a change. */
    private void compactSlots() {
        String[] q = prefs.quickLangs();
        int w = 0;
        for (String id : q) if (Langs.byId(id) != null) prefs.quickLang(w++, id);
        for (; w < 4; w++) prefs.quickLang(w, "");
    }

    private LinearLayout.LayoutParams slotLp() {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2);
        lp.bottomMargin = dp(8);
        return lp;
    }

    private void pickLanguage(int slot) {
        String[] items = new String[Langs.ALL.length];
        for (int i = 0; i < items.length; i++) items[i] = Langs.ALL[i].flag + "   " + Langs.ALL[i].label;
        new AlertDialog.Builder(this, android.R.style.Theme_DeviceDefault_Dialog_Alert)
                .setTitle("Translate to…")
                .setItems(items, (d, which) -> {
                    prefs.quickLang(slot, Langs.ALL[which].id);
                    compactSlots();
                    buildSlots();
                })
                .setNegativeButton("Cancel", null)
                .show();
    }

    // ───────── misc actions ─────────

    private boolean accessibilityOn() {
        if (FlowService.instance != null) return true;
        String enabled = Settings.Secure.getString(getContentResolver(), Settings.Secure.ENABLED_ACCESSIBILITY_SERVICES);
        if (enabled == null) return false;
        ComponentName me = new ComponentName(this, FlowService.class);
        return enabled.contains(me.flattenToString()) || enabled.contains(me.flattenToShortString());
    }

    private void appInfo() {
        startActivity(new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:" + getPackageName())));
    }

    private void openSamsungBattery() {
        String[][] targets = {
                {"com.samsung.android.lool", "com.samsung.android.sm.battery.ui.BatteryActivity"},
                {"com.samsung.android.lool", "com.samsung.android.sm.ui.battery.BatteryActivity"},
        };
        for (String[] t : targets) {
            try { startActivity(new Intent().setComponent(new ComponentName(t[0], t[1]))); return; } catch (Exception ignored) { }
        }
        try { startActivity(new Intent(Settings.ACTION_SETTINGS)); } catch (Exception ignored) { }
    }

    private void testKey() {
        toast("Testing…");
        new Thread(() -> {
            String msg;
            try {
                String out = Groq.translate(prefs, "Hello, how are you?", Langs.byId("es-CO"));
                msg = "✅ Key works (" + prefs.llmModel() + "): “" + out + "”";
            } catch (Exception e) {
                msg = "⚠ " + e.getMessage();
            }
            final String m = msg;
            runOnUiThread(() -> Toast.makeText(this, m, Toast.LENGTH_LONG).show());
        }).start();
    }

    @Override
    public void onRequestPermissionsResult(int code, String[] perms, int[] results) {
        super.onRequestPermissionsResult(code, perms, results);
        buildChecklist();
    }

    // ───────── UI kit ─────────

    private interface BoolCb { void on(boolean b); }
    private interface StrCb { void on(String s); }

    private static final class Watcher implements TextWatcher {
        final StrCb cb;
        Watcher(StrCb cb) { this.cb = cb; }
        public void beforeTextChanged(CharSequence s, int a, int b, int c) { }
        public void onTextChanged(CharSequence s, int a, int b, int c) { }
        public void afterTextChanged(Editable e) { cb.on(e.toString()); }
    }

    private int dp(float v) { return (int) TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v, getResources().getDisplayMetrics()); }

    private TextView text(String s, float size, boolean bold, int color) {
        TextView t = new TextView(this);
        t.setText(s);
        t.setTextSize(size);
        t.setTextColor(color);
        if (bold) t.setTypeface(Typeface.create(Typeface.DEFAULT, Typeface.BOLD));
        return t;
    }

    private TextView label(String s) {
        TextView t = text(s, 13, true, MUTED);
        t.setAllCaps(true);
        t.setLetterSpacing(0.06f);
        t.setPadding(0, dp(4), 0, dp(8));
        return t;
    }

    private TextView note(String s) {
        TextView t = text(s, 13, false, MUTED);
        t.setPadding(0, dp(6), 0, dp(6));
        return t;
    }

    private LinearLayout hrow() {
        LinearLayout r = new LinearLayout(this);
        r.setOrientation(LinearLayout.HORIZONTAL);
        r.setGravity(Gravity.CENTER_VERTICAL);
        return r;
    }

    private HorizontalScrollView scroll(View content) {
        HorizontalScrollView h = new HorizontalScrollView(this);
        h.setHorizontalScrollBarEnabled(false);
        h.addView(content);
        return h;
    }

    /** A card with an emoji badge, title and subtitle; returns the body to add content to. */
    private LinearLayout card(LinearLayout parent, String emoji, String title, String subtitle) {
        LinearLayout c = new LinearLayout(this);
        c.setOrientation(LinearLayout.VERTICAL);
        c.setPadding(dp(16), dp(16), dp(16), dp(14));
        GradientDrawable g = new GradientDrawable();
        g.setCornerRadius(dp(22));
        g.setColor(CARD);
        g.setStroke(dp(1), LINE);
        c.setBackground(g);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-1, -2);
        lp.topMargin = dp(14);
        parent.addView(c, lp);

        LinearLayout head = hrow();
        head.setPadding(0, 0, 0, dp(12));
        TextView badge = text(emoji, 18, false, TEXT);
        badge.setGravity(Gravity.CENTER);
        GradientDrawable bg = new GradientDrawable(GradientDrawable.Orientation.TL_BR, new int[]{0x557C4DFF, 0x55286EFF});
        bg.setCornerRadius(dp(12));
        badge.setBackground(bg);
        head.addView(badge, new LinearLayout.LayoutParams(dp(40), dp(40)));
        LinearLayout titles = new LinearLayout(this);
        titles.setOrientation(LinearLayout.VERTICAL);
        titles.setPadding(dp(12), 0, 0, 0);
        titles.addView(text(title, 18, true, TEXT));
        if (subtitle != null) titles.addView(text(subtitle, 13, false, MUTED));
        head.addView(titles);
        c.addView(head);

        LinearLayout body = new LinearLayout(this);
        body.setOrientation(LinearLayout.VERTICAL);
        c.addView(body);
        return body;
    }

    private TextView pill(String label, boolean primary, View.OnClickListener l) {
        TextView p = text(label, 14, true, TEXT);
        p.setGravity(Gravity.CENTER);
        p.setPadding(dp(16), dp(9), dp(16), dp(9));
        GradientDrawable g = primary
                ? new GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT, new int[]{PURPLE, BLUE})
                : new GradientDrawable();
        if (!primary) { g.setColor(CARD2); g.setStroke(dp(1), 0x33FFFFFF); }
        g.setCornerRadius(dp(20));
        p.setBackground(g);
        p.setOnClickListener(l);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-2, -2);
        lp.rightMargin = dp(8);
        p.setLayoutParams(lp);
        return p;
    }

    private TextView chip(String label, boolean on) {
        TextView c = text(label, 14, on, on ? TEXT : 0xFFCFCFE0);
        c.setPadding(dp(14), dp(8), dp(14), dp(8));
        GradientDrawable g = on
                ? new GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT, new int[]{PURPLE, BLUE})
                : new GradientDrawable();
        if (!on) { g.setColor(CARD2); g.setStroke(dp(1), 0x22FFFFFF); }
        g.setCornerRadius(dp(18));
        c.setBackground(g);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(-2, -2);
        lp.rightMargin = dp(8);
        c.setLayoutParams(lp);
        return c;
    }

    private EditText field(String hint, String value, boolean secret) {
        EditText e = new EditText(this);
        e.setHint(hint);
        e.setText(value);
        e.setTextColor(TEXT);
        e.setHintTextColor(0xFF6A6A80);
        e.setTextSize(15);
        e.setPadding(dp(14), dp(12), dp(14), dp(12));
        if (secret) {
            e.setSingleLine(true);
            e.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        }
        GradientDrawable g = new GradientDrawable();
        g.setCornerRadius(dp(14));
        g.setColor(0xFF12121A);
        g.setStroke(dp(1), 0x33FFFFFF);
        e.setBackground(g);
        e.setLayoutParams(new LinearLayout.LayoutParams(-1, -2));
        return e;
    }

    private View toggle(String title, String sub, boolean value, int accent, BoolCb cb) {
        LinearLayout r = hrow();
        r.setPadding(0, dp(10), 0, dp(10));
        LinearLayout t = new LinearLayout(this);
        t.setOrientation(LinearLayout.VERTICAL);
        t.addView(text(title, 16, true, TEXT));
        if (sub != null) {
            TextView s = text(sub, 13, false, MUTED);
            s.setPadding(0, dp(2), dp(10), 0);
            t.addView(s);
        }
        r.addView(t, new LinearLayout.LayoutParams(0, -2, 1));
        Switch sw = new Switch(this);
        sw.setChecked(value);
        int[][] states = {{android.R.attr.state_checked}, {}};
        sw.setThumbTintList(new ColorStateList(states, new int[]{Color.WHITE, 0xFFB0B0C0}));
        sw.setTrackTintList(new ColorStateList(states, new int[]{accent, 0xFF3A3A4A}));
        sw.setOnCheckedChangeListener((v, on) -> cb.on(on));
        r.addView(sw);
        r.setOnClickListener(v -> sw.toggle());
        return r;
    }

    private void toast(String s) { Toast.makeText(this, s, Toast.LENGTH_SHORT).show(); }

    private void open(String url) {
        try { startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url))); } catch (Exception ignored) { }
    }

    private String versionName() {
        try { return getPackageManager().getPackageInfo(getPackageName(), 0).versionName; } catch (Exception e) { return "?"; }
    }
}

package com.freeflow.bubble;

import android.Manifest;
import android.app.Activity;
import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.ComponentName;
import android.content.Intent;
import android.content.pm.PackageManager;
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
import android.widget.AdapterView;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.Switch;
import android.widget.TextView;
import android.widget.Toast;

import java.util.ArrayList;
import java.util.List;

/** Setup checklist + settings. */
public class MainActivity extends Activity {
    private Prefs prefs;
    private LinearLayout checklist;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        prefs = new Prefs(this);
        RecorderService.ensureChannel(this);

        ScrollView sv = new ScrollView(this);
        sv.setFitsSystemWindows(true);
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(dp(20), dp(24), dp(20), dp(40));
        sv.addView(root);
        setContentView(sv);

        TextView title = new TextView(this);
        title.setText("FreeFlow Bubble");
        title.setTextSize(28);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        root.addView(title);
        root.addView(note("Voice typing and translation over any app — keep Gboard as your keyboard. "
                + "Tap the bubble to talk (tap again to finish) or hold it while you speak. Drag it to move it. "
                + "The 文A button above it translates what you've typed."));

        // ── setup checklist
        root.addView(header("Setup"));
        checklist = new LinearLayout(this);
        checklist.setOrientation(LinearLayout.VERTICAL);
        root.addView(checklist);

        // ── Groq key
        root.addView(header("Groq API key"));
        root.addView(note("Same free key as your FreeFlow keyboard. Powers speech-to-text, translation and voice edits."));
        EditText key = new EditText(this);
        key.setHint("gsk_…");
        key.setSingleLine(true);
        key.setInputType(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD);
        key.setText(prefs.groqKey());
        key.addTextChangedListener(new Watcher(s -> prefs.groqKey(s)));
        root.addView(key);
        LinearLayout keyRow = row();
        keyRow.addView(button("Paste", v -> {
            ClipboardManager cm = (ClipboardManager) getSystemService(CLIPBOARD_SERVICE);
            ClipData clip = cm.getPrimaryClip();
            if (clip != null && clip.getItemCount() > 0) key.setText(clip.getItemAt(0).coerceToText(this).toString().trim());
        }));
        keyRow.addView(button("Test", v -> testKey()));
        keyRow.addView(button("Get a free key", v -> open("https://console.groq.com/keys")));
        root.addView(keyRow);

        // ── voice
        root.addView(header("Voice"));
        List<String> spoken = new ArrayList<>();
        int spokenIdx = 0;
        for (int i = 0; i < Langs.SPOKEN.length; i++) {
            spoken.add(Langs.SPOKEN[i][1]);
            if (Langs.SPOKEN[i][0].equals(prefs.spokenLanguage())) spokenIdx = i;
        }
        root.addView(label("I speak"));
        root.addView(spinner(spoken, spokenIdx, i -> prefs.spokenLanguage(Langs.SPOKEN[i][0])));
        root.addView(note("Auto-detect handles switching between French, English and Spanish."));
        root.addView(toggle("AI cleanup (remove ums, fix punctuation)", prefs.aiCleanup(), prefs::aiCleanup));
        root.addView(note("Off = your words exactly as Whisper heard them."));

        // ── translation
        root.addView(header("Translation languages"));
        root.addView(note("Up to 4 shown as quick tiles. Every language stays one tap away under “More languages”."));
        List<String> choices = new ArrayList<>();
        choices.add("— none —");
        for (Langs.Lang l : Langs.ALL) choices.add(l.chip());
        String[] quick = prefs.quickLangs();
        for (int slot = 0; slot < 4; slot++) {
            int sel = 0;
            for (int j = 0; j < Langs.ALL.length; j++) if (Langs.ALL[j].id.equals(quick[slot])) sel = j + 1;
            final int s = slot;
            root.addView(spinner(choices, sel, i -> prefs.quickLang(s, i == 0 ? "" : Langs.ALL[i - 1].id)));
        }

        // ── bubble
        root.addView(header("Bubble"));
        root.addView(toggle("Only show while the keyboard is open", prefs.onlyWithKeyboard(), v -> { prefs.onlyWithKeyboard(v); FlowService.reload(); }));
        root.addView(toggle("Large bubble", prefs.largeBubble(), v -> { prefs.largeBubble(v); FlowService.reload(); }));
        root.addView(toggle("Vibrate on tap", prefs.haptics(), prefs::haptics));
        root.addView(button("Reset bubble position", v -> { prefs.resetBubble(); FlowService.reload(); }));

        // ── try it
        root.addView(header("Try it"));
        EditText tryIt = new EditText(this);
        tryIt.setHint("Tap here, then tap the bubble and talk");
        tryIt.setMinLines(3);
        root.addView(tryIt);
        root.addView(note("Version " + versionName()));
    }

    @Override
    protected void onResume() {
        super.onResume();
        buildChecklist();
    }

    // ───────── checklist ─────────

    private void buildChecklist() {
        checklist.removeAllViews();
        boolean a11y = accessibilityOn();
        boolean micOk = checkSelfPermission(Manifest.permission.RECORD_AUDIO) == PackageManager.PERMISSION_GRANTED;
        boolean notifOk = Build.VERSION.SDK_INT < 33 || checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED;
        PowerManager pm = (PowerManager) getSystemService(POWER_SERVICE);
        boolean battery = pm.isIgnoringBatteryOptimizations(getPackageName());

        checklist.addView(step(micOk, "1. Allow the microphone", "Grant", v ->
                requestPermissions(new String[]{Manifest.permission.RECORD_AUDIO}, 1)));
        checklist.addView(step(notifOk, "2. Allow notifications", "Grant", v -> {
            if (Build.VERSION.SDK_INT >= 33) requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, 2);
        }));
        checklist.addView(step(a11y, "3. Turn on FreeFlow Bubble in Accessibility", "Open", v ->
                startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS))));
        if (!a11y) {
            checklist.addView(note("Switch greyed out (“Restricted setting”)? Android does this for apps installed outside the Play Store. "
                    + "Tap “App info” below → ⋮ (top right) → “Allow restricted settings”, then try again."));
            checklist.addView(button("App info", v -> appInfo()));
        }
        checklist.addView(step(battery, "4. Let it run in the background", "Allow", v -> {
            try {
                startActivity(new Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS, Uri.parse("package:" + getPackageName())));
            } catch (Exception e) { appInfo(); }
        }));
        if ("samsung".equalsIgnoreCase(Build.MANUFACTURER)) {
            checklist.addView(step(false, "5. Samsung: add to “Never sleeping apps”", "Open", v -> openSamsungBattery()));
            checklist.addView(note("Settings → Battery → Background usage limits → Never sleeping apps → + → FreeFlow Bubble. "
                    + "This stops One UI from switching the bubble off after a few days."));
        }
        if (micOk && a11y && battery && !prefs.groqKey().isEmpty())
            checklist.addView(note("✅ All set — tap the box under “Try it” and the bubble appears."));
        else if (prefs.groqKey().isEmpty())
            checklist.addView(note("Then paste your Groq key below."));
    }

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
        Toast.makeText(this, "Testing…", Toast.LENGTH_SHORT).show();
        new Thread(() -> {
            String msg;
            try {
                String out = Groq.translate(prefs, "Hello, how are you?", Langs.byId("es-CO"));
                msg = "✅ Key works: “" + out + "”";
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

    // ───────── tiny UI kit ─────────

    private interface IntCb { void on(int i); }
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

    private TextView header(String t) {
        TextView v = new TextView(this);
        v.setText(t);
        v.setTextSize(19);
        v.setTypeface(Typeface.DEFAULT_BOLD);
        v.setPadding(0, dp(26), 0, dp(6));
        return v;
    }

    private TextView label(String t) {
        TextView v = new TextView(this);
        v.setText(t);
        v.setTextSize(15);
        v.setPadding(0, dp(6), 0, dp(2));
        return v;
    }

    private TextView note(String t) {
        TextView v = new TextView(this);
        v.setText(t);
        v.setTextSize(13);
        v.setAlpha(0.7f);
        v.setPadding(0, dp(2), 0, dp(8));
        return v;
    }

    private LinearLayout row() {
        LinearLayout r = new LinearLayout(this);
        r.setOrientation(LinearLayout.HORIZONTAL);
        r.setGravity(Gravity.CENTER_VERTICAL);
        return r;
    }

    private Button button(String t, View.OnClickListener l) {
        Button b = new Button(this);
        b.setText(t);
        b.setAllCaps(false);
        b.setOnClickListener(l);
        return b;
    }

    private View step(boolean done, String text, String action, View.OnClickListener l) {
        LinearLayout r = row();
        r.setPadding(dp(12), dp(8), dp(8), dp(8));
        GradientDrawable g = new GradientDrawable();
        g.setCornerRadius(dp(12));
        g.setColor(done ? Color.argb(40, 48, 199, 89) : Color.argb(40, 255, 159, 10));
        r.setBackground(g);
        LinearLayout.LayoutParams rp = new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        rp.bottomMargin = dp(8);
        r.setLayoutParams(rp);
        TextView t = new TextView(this);
        t.setText((done ? "✅  " : "⬜  ") + text);
        t.setTextSize(15);
        r.addView(t, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WRAP_CONTENT, 1));
        if (!done) r.addView(button(action, l));
        return r;
    }

    private Switch toggle(String t, boolean value, BoolCb cb) {
        Switch s = new Switch(this);
        s.setText(t);
        s.setTextSize(15);
        s.setChecked(value);
        s.setPadding(0, dp(8), 0, dp(8));
        s.setOnCheckedChangeListener((v, on) -> cb.on(on));
        return s;
    }

    private Spinner spinner(List<String> items, int selected, IntCb cb) {
        Spinner sp = new Spinner(this);
        ArrayAdapter<String> a = new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item, items);
        sp.setAdapter(a);
        sp.setSelection(selected, false);
        sp.setOnItemSelectedListener(new AdapterView.OnItemSelectedListener() {
            @Override public void onItemSelected(AdapterView<?> p, View v, int pos, long id) { cb.on(pos); }
            @Override public void onNothingSelected(AdapterView<?> p) { }
        });
        return sp;
    }

    private void open(String url) {
        try { startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url))); } catch (Exception ignored) { }
    }

    private String versionName() {
        try { return getPackageManager().getPackageInfo(getPackageName(), 0).versionName; } catch (Exception e) { return "?"; }
    }
}

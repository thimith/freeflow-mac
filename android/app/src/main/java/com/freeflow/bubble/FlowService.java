package com.freeflow.bubble;

import android.Manifest;
import android.accessibilityservice.AccessibilityService;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.drawable.GradientDrawable;
import android.os.Handler;
import android.os.Looper;
import android.util.DisplayMetrics;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.HapticFeedbackConstants;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;
import android.view.WindowManager;
import android.view.accessibility.AccessibilityEvent;
import android.view.accessibility.AccessibilityNodeInfo;
import android.view.accessibility.AccessibilityWindowInfo;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.widget.Toast;

import java.io.File;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * The heart of FreeFlow Bubble. Being an Accessibility service means Android itself keeps it
 * running (no battery-killer games), lets it draw the bubble over any app without extra
 * permissions, and lets it type into the focused field — so you keep Gboard as your keyboard.
 */
public class FlowService extends AccessibilityService implements Mic.Listener {
    static FlowService instance;

    private enum Mode { DICTATE, EDIT }

    private final Handler h = new Handler(Looper.getMainLooper());
    private final ExecutorService io = Executors.newSingleThreadExecutor();
    private Prefs prefs;
    private WindowManager wm;

    private LinearLayout bubble;
    private BubbleButton mic, tr, edit;
    /** The button showing the current recording (mic for dictation, ✨ for voice edits). */
    private BubbleButton active;
    private WindowManager.LayoutParams lp;
    private boolean shown;

    private View popup;
    private long popupClosedAt;
    private View undoChip;
    private TextTarget undoTarget;
    private String undoText;

    private TextTarget target;
    private Mode mode = Mode.DICTATE;
    private String editSource;
    private boolean recording, busy, triedTrampoline;

    // ───────── lifecycle ─────────

    @Override
    protected void onServiceConnected() {
        instance = this;
        prefs = new Prefs(this);
        wm = (WindowManager) getSystemService(WINDOW_SERVICE);
        Mic.setListener(this);
        buildBubble();
        refresh();
    }

    @Override
    public void onAccessibilityEvent(AccessibilityEvent e) {
        h.removeCallbacks(refreshTask);
        h.postDelayed(refreshTask, 120);
    }

    @Override public void onInterrupt() { }

    @Override
    public void onDestroy() {
        removeAll();
        if (Mic.isRunning()) Mic.cancel();
        RecorderService.stop(this);
        instance = null;
        super.onDestroy();
    }

    /** Called by the settings screen after changing bubble options. */
    static void reload() {
        FlowService s = instance;
        if (s == null) return;
        s.removeAll();
        s.buildBubble();
        s.refresh();
    }

    private final Runnable refreshTask = this::refresh;

    private void refresh() {
        if (recording || busy) return;   // never yank the bubble away mid-dictation
        AccessibilityNodeInfo f = focusedEditable();
        boolean show = f != null && !f.isPassword() && (!prefs.onlyWithKeyboard() || keyboardVisible());
        if (f != null && !f.isPassword()) target = new TextTarget(f);
        setShown(show);
        if (!show) closePopup();
    }

    private AccessibilityNodeInfo focusedEditable() {
        try {
            AccessibilityNodeInfo root = getRootInActiveWindow();
            if (root != null) {
                AccessibilityNodeInfo n = root.findFocus(AccessibilityNodeInfo.FOCUS_INPUT);
                if (n != null && n.isEditable()) return n;
            }
            List<AccessibilityWindowInfo> ws = getWindows();
            for (AccessibilityWindowInfo w : ws) {
                if (w.getType() != AccessibilityWindowInfo.TYPE_APPLICATION) continue;
                AccessibilityNodeInfo r = w.getRoot();
                if (r == null) continue;
                AccessibilityNodeInfo n = r.findFocus(AccessibilityNodeInfo.FOCUS_INPUT);
                if (n != null && n.isEditable()) return n;
            }
        } catch (Exception ignored) { }
        return null;
    }

    private boolean keyboardVisible() {
        try {
            for (AccessibilityWindowInfo w : getWindows())
                if (w.getType() == AccessibilityWindowInfo.TYPE_INPUT_METHOD) return true;
        } catch (Exception ignored) { }
        return false;
    }

    // ───────── bubble UI ─────────

    private int dp(float v) { return (int) TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v, getResources().getDisplayMetrics()); }

    private DisplayMetrics screen() { return getResources().getDisplayMetrics(); }

    private void buildBubble() {
        int size = dp(prefs.largeBubble() ? 66 : 56);
        int small = dp(prefs.largeBubble() ? 46 : 40);
        bubble = new LinearLayout(this);
        bubble.setOrientation(LinearLayout.VERTICAL);
        bubble.setGravity(Gravity.CENTER_HORIZONTAL);
        bubble.setPadding(dp(5), dp(6), dp(5), dp(6));
        bubble.setClipChildren(false);
        bubble.setClipToPadding(false);
        // One solid capsule holding both buttons, with a light rim so it stands out on dark and light apps.
        GradientDrawable capsule = new GradientDrawable();
        capsule.setColor(Color.argb(240, 18, 18, 24));
        capsule.setCornerRadius(dp(40));
        capsule.setStroke(dp(2), Color.argb(110, 255, 255, 255));
        bubble.setBackground(capsule);
        bubble.setElevation(dp(12));

        edit = new BubbleButton(this, BubbleButton.Kind.EDIT);
        LinearLayout.LayoutParams elp = new LinearLayout.LayoutParams(small, small);
        elp.bottomMargin = dp(6);
        bubble.addView(edit, elp);

        tr = new BubbleButton(this, BubbleButton.Kind.TRANSLATE);
        LinearLayout.LayoutParams tlp = new LinearLayout.LayoutParams(small, small);
        tlp.bottomMargin = dp(6);
        bubble.addView(tr, tlp);

        mic = new BubbleButton(this, BubbleButton.Kind.MIC);
        bubble.addView(mic, new LinearLayout.LayoutParams(size, size));

        mic.setOnTouchListener(new Toucher(this::micTap, this::micHoldStart, this::micHoldEnd));
        tr.setOnTouchListener(new Toucher(this::translateTap, null, null));
        edit.setOnTouchListener(new Toucher(this::editTap, this::editHoldStart, this::micHoldEnd));
        active = mic;

        lp = new WindowManager.LayoutParams(
                WindowManager.LayoutParams.WRAP_CONTENT, WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL,
                PixelFormat.TRANSLUCENT);
        lp.gravity = Gravity.TOP | (prefs.bubbleLeft() ? Gravity.LEFT : Gravity.RIGHT);
        lp.x = dp(2);
        lp.y = prefs.bubbleY((int) (screen().heightPixels * 0.28f));
    }

    private void setShown(boolean show) {
        try {
            if (show && !shown) { wm.addView(bubble, lp); shown = true; }
            else if (!show && shown) { wm.removeView(bubble); shown = false; }
        } catch (Exception ignored) { }
    }

    private void removeAll() {
        closePopup();
        hideUndo();
        setShown(false);
    }

    /** Tap / hold / drag handling shared by both buttons. Drag moves the bubble; release snaps it to the nearest edge. */
    private class Toucher implements View.OnTouchListener {
        final Runnable tap, holdStart, holdEnd;
        float downX, downY;
        int startY;
        boolean dragging, holding;
        final int slop = ViewConfiguration.get(FlowService.this).getScaledTouchSlop();
        final Runnable holdTask = new Runnable() {
            @Override public void run() { holding = true; if (holdStart != null) holdStart.run(); }
        };

        Toucher(Runnable tap, Runnable holdStart, Runnable holdEnd) { this.tap = tap; this.holdStart = holdStart; this.holdEnd = holdEnd; }

        @Override
        public boolean onTouch(View v, MotionEvent e) {
            switch (e.getActionMasked()) {
                case MotionEvent.ACTION_DOWN:
                    downX = e.getRawX(); downY = e.getRawY(); startY = lp.y;
                    dragging = false; holding = false;
                    if (holdStart != null && !recording && !busy) h.postDelayed(holdTask, 380);
                    return true;
                case MotionEvent.ACTION_MOVE:
                    float dx = e.getRawX() - downX, dy = e.getRawY() - downY;
                    if (!dragging && !holding && (Math.abs(dy) > slop * 1.5f || Math.abs(dx) > slop * 1.5f)) {
                        dragging = true;
                        h.removeCallbacks(holdTask);
                        closePopup();
                    }
                    if (dragging) {
                        int maxY = screen().heightPixels - bubble.getHeight() - dp(24);
                        lp.y = Math.max(dp(24), Math.min(maxY, (int) (startY + dy)));
                        try { wm.updateViewLayout(bubble, lp); } catch (Exception ignored) { }
                    }
                    return true;
                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_CANCEL:
                    h.removeCallbacks(holdTask);
                    if (dragging) {
                        boolean left = e.getRawX() < screen().widthPixels / 2f;
                        lp.gravity = Gravity.TOP | (left ? Gravity.LEFT : Gravity.RIGHT);
                        try { wm.updateViewLayout(bubble, lp); } catch (Exception ignored) { }
                        prefs.bubblePos(lp.y, left);
                    } else if (holding) {
                        if (holdEnd != null) holdEnd.run();
                    } else if (e.getActionMasked() == MotionEvent.ACTION_UP) {
                        tap.run();
                    }
                    return true;
            }
            return false;
        }
    }

    private void haptic(View v) {
        if (prefs.haptics()) v.performHapticFeedback(HapticFeedbackConstants.CONTEXT_CLICK);
    }

    private void flash(BubbleButton b, BubbleButton.State s, long ms) {
        b.setState(s);
        h.postDelayed(() -> { if (b.state() == s) b.setState(BubbleButton.State.IDLE); }, ms);
    }

    private void error(String msg) {
        flash(active != null ? active : mic, BubbleButton.State.ERROR, 1400);
        Toast.makeText(this, msg, Toast.LENGTH_LONG).show();
    }

    // ───────── dictation ─────────

    private boolean holdMode;

    private void micTap() {
        if (busy) return;
        closePopup();
        if (recording) { holdMode = false; stopRecording(); }
        else { holdMode = false; startRecording(Mode.DICTATE); }
    }

    private void editTap() {
        if (busy) return;
        closePopup();
        if (recording) { holdMode = false; stopRecording(); return; }
        holdMode = false;
        startRecording(Mode.EDIT);
    }

    private void editHoldStart() { holdMode = true; startRecording(Mode.EDIT); }

    private void micHoldStart() { holdMode = true; startRecording(Mode.DICTATE); }

    private void micHoldEnd() { if (recording && holdMode) stopRecording(); holdMode = false; }

    private boolean ready() {
        if (checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            openApp();
            error("Allow the microphone in the FreeFlow app");
            return false;
        }
        if (prefs.groqKey().isEmpty()) {
            openApp();
            error("Add your Groq API key in the FreeFlow app");
            return false;
        }
        if (target == null || !target.alive()) {
            AccessibilityNodeInfo f = focusedEditable();
            if (f == null) { error("Tap a text field first"); return false; }
            target = new TextTarget(f);
        }
        return true;
    }

    private void startRecording(Mode m) {
        if (recording || !ready()) return;
        mode = m;
        editSource = m == Mode.EDIT ? target.selectedOrAll() : null;
        recording = true;
        triedTrampoline = false;
        active = m == Mode.EDIT ? edit : mic;
        active.setState(m == Mode.EDIT ? BubbleButton.State.EDIT_RECORDING : BubbleButton.State.RECORDING);
        haptic(active);
        try {
            startForegroundService(new Intent(this, RecorderService.class));
        } catch (Exception e) {
            trampoline();
        }
        h.postDelayed(micCheck, 900);
    }

    /** If Android silently feeds us silence (mic blocked for "background" apps), retry through the trampoline. */
    private final Runnable micCheck = () -> {
        if (!recording || triedTrampoline) return;
        if (!Mic.isRunning() || Mic.maxAbs() == 0) {
            Mic.cancel();
            RecorderService.stop(this);
            trampoline();
        }
    };

    private void trampoline() {
        triedTrampoline = true;
        try {
            Intent i = new Intent(this, TrampolineActivity.class)
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_NO_ANIMATION);
            startActivity(i);
        } catch (Exception e) {
            onMicFailed("Android blocked the microphone");
        }
    }

    /** RecorderService couldn't get the mic. */
    void onMicBlocked() {
        if (!recording) return;
        if (!triedTrampoline) trampoline();
        else onMicFailed("Android blocked the microphone — open FreeFlow once and try again");
    }

    void onMicFailed(String why) {
        recording = false;
        holdMode = false;
        if (Mic.isRunning()) Mic.cancel();
        RecorderService.stop(this);
        error(why);
    }

    @Override public void onLevel(float level) { if (recording) active.setLevel(level); }
    @Override public void onStarted() { }
    @Override public void onFailed(String why) { if (recording) onMicFailed(why); }

    private void stopRecording() {
        if (!recording) return;
        recording = false;
        h.removeCallbacks(micCheck);
        File wav = Mic.stop(getCacheDir());
        float peak = Mic.peak();
        RecorderService.stop(this);
        final BubbleButton b = active;
        haptic(b);
        if (wav == null || peak < 0.15f) {
            if (wav != null) wav.delete();
            flash(b, BubbleButton.State.ERROR, 900);
            Toast.makeText(this, "No speech detected", Toast.LENGTH_SHORT).show();
            return;
        }
        busy = true;
        b.setState(BubbleButton.State.BUSY);
        final TextTarget t = target;
        final Mode m = mode;
        final String source = editSource;
        io.execute(() -> {
            try {
                String raw = Groq.transcribe(prefs, wav);
                if (raw.isEmpty()) { h.post(() -> { busy = false; error("No speech detected"); }); return; }
                final String out = m == Mode.EDIT ? Groq.voiceEdit(prefs, raw, source)
                        : prefs.aiCleanup() ? Groq.cleanup(prefs, raw) : raw;
                h.post(() -> {
                    busy = false;
                    boolean ok;
                    if (m == Mode.EDIT) {
                        rememberUndo(t);
                        ok = t.replaceSelectedOrAll(this, out);
                        if (ok) showUndo();
                    } else ok = t.insert(this, out);
                    if (ok) flash(b, BubbleButton.State.DONE, 700);
                    else error("This app didn't accept the text — it's on your clipboard");
                    refresh();
                });
            } catch (Exception e) {
                h.post(() -> { busy = false; error(e.getMessage() == null ? "Something went wrong" : e.getMessage()); refresh(); });
            } finally {
                wav.delete();
            }
        });
    }

    // ───────── translate menu ─────────

    private void translateTap() {
        if (busy || recording) return;
        if (popup != null) { closePopup(); return; }
        if (System.currentTimeMillis() - popupClosedAt < 350) return;   // this tap just closed it (outside-touch)
        if (!ready()) return;
        haptic(tr);
        showMenu(false);
    }

    private void showMenu(boolean all) {
        closePopup();
        LinearLayout card = new LinearLayout(this);
        card.setOrientation(LinearLayout.VERTICAL);
        card.setPadding(dp(12), dp(10), dp(12), dp(12));
        GradientDrawable bg = new GradientDrawable();
        bg.setColor(Color.rgb(28, 28, 34));
        bg.setCornerRadius(dp(18));
        card.setBackground(bg);
        card.setElevation(dp(10));

        TextView title = new TextView(this);
        title.setText(all ? "All languages" : "Translate to");
        title.setTextColor(Color.argb(170, 255, 255, 255));
        title.setTextSize(12);
        title.setPadding(dp(4), 0, 0, dp(6));
        card.addView(title);

        if (all) {
            ScrollView sv = new ScrollView(this);
            LinearLayout list = new LinearLayout(this);
            list.setOrientation(LinearLayout.VERTICAL);
            for (Langs.Lang l : Langs.ALL) list.addView(tile(l.chip(), () -> translate(l)), full());
            sv.addView(list);
            card.addView(sv, new LinearLayout.LayoutParams(dp(230), Math.min(dp(380), (int) (screen().heightPixels * 0.5f))));
        } else {
            LinearLayout row = null;
            int n = 0;
            for (String id : prefs.quickLangs()) {
                Langs.Lang l = Langs.byId(id);
                if (l == null) continue;
                if (n % 2 == 0) { row = new LinearLayout(this); card.addView(row); }
                row.addView(tile(l.flag + "  " + shortName(l), () -> translate(l)), half());
                n++;
            }
            card.addView(tile("🌐  More languages", () -> showMenu(true)), full());
        }

        FrameLayout root = new FrameLayout(this);
        root.addView(card);
        root.setOnTouchListener((v, e) -> {
            if (e.getActionMasked() == MotionEvent.ACTION_OUTSIDE) { closePopup(); return true; }
            return false;
        });

        WindowManager.LayoutParams p = new WindowManager.LayoutParams(
                WindowManager.LayoutParams.WRAP_CONTENT, WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_WATCH_OUTSIDE_TOUCH,
                PixelFormat.TRANSLUCENT);
        p.gravity = lp.gravity;
        p.x = lp.x + (bubble.getWidth() > 0 ? bubble.getWidth() : dp(70)) + dp(4);
        p.y = Math.max(dp(24), lp.y - dp(10));
        try { wm.addView(root, p); popup = root; } catch (Exception ignored) { }
    }

    private static String shortName(Langs.Lang l) {
        // "Español (Colombia)" → "Español" on the quick tiles (the flag already says Colombia)
        int i = l.label.indexOf(" (");
        return i > 0 ? l.label.substring(0, i) : l.label;
    }

    private LinearLayout.LayoutParams half() {
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(dp(112), dp(46));
        p.setMargins(dp(3), dp(3), dp(3), dp(3));
        return p;
    }

    private LinearLayout.LayoutParams full() {
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(dp(230), dp(46));
        p.setMargins(dp(3), dp(3), dp(3), dp(3));
        return p;
    }

    private TextView tile(String label, Runnable onTap) {
        TextView t = new TextView(this);
        t.setText(label);
        t.setTextColor(Color.WHITE);
        t.setTextSize(15);
        t.setGravity(Gravity.CENTER_VERTICAL);
        t.setPadding(dp(12), 0, dp(8), 0);
        t.setSingleLine(true);
        GradientDrawable g = new GradientDrawable();
        g.setColor(Color.rgb(48, 48, 58));
        g.setCornerRadius(dp(12));
        t.setBackground(g);
        t.setOnClickListener(v -> { haptic(v); onTap.run(); });
        return t;
    }

    private void closePopup() {
        if (popup == null) return;
        try { wm.removeView(popup); } catch (Exception ignored) { }
        popup = null;
        popupClosedAt = System.currentTimeMillis();
    }

    private void translate(Langs.Lang lang) {
        closePopup();
        if (target == null || !target.alive()) { error("Tap a text field first"); return; }
        final TextTarget t = target;
        final String src = t.selectedOrAll();
        if (src.trim().isEmpty()) { error("Nothing to translate yet"); return; }
        busy = true;
        tr.setState(BubbleButton.State.BUSY);
        io.execute(() -> {
            try {
                String out = Groq.translate(prefs, src, lang);
                h.post(() -> {
                    busy = false;
                    rememberUndo(t);
                    if (t.replaceSelectedOrAll(this, out)) { flash(tr, BubbleButton.State.DONE, 700); showUndo(); }
                    else { tr.setState(BubbleButton.State.IDLE); error("This app didn't accept the translation"); }
                });
            } catch (Exception e) {
                h.post(() -> { busy = false; tr.setState(BubbleButton.State.IDLE); error(e.getMessage() == null ? "Translation failed" : e.getMessage()); });
            }
        });
    }


    // ───────── undo ─────────

    private void rememberUndo(TextTarget t) {
        undoTarget = t;
        undoText = t.text();
    }

    private final Runnable hideUndoTask = this::hideUndo;

    private void showUndo() {
        hideUndo();
        TextView chip = new TextView(this);
        chip.setText("↶  Undo");
        chip.setTextColor(Color.WHITE);
        chip.setTextSize(14);
        chip.setPadding(dp(14), dp(8), dp(14), dp(8));
        GradientDrawable g = new GradientDrawable();
        g.setColor(Color.rgb(28, 28, 34));
        g.setCornerRadius(dp(20));
        chip.setBackground(g);
        chip.setElevation(dp(8));
        chip.setOnClickListener(v -> {
            haptic(v);
            if (undoTarget != null && undoText != null && undoTarget.alive()) undoTarget.setText(undoText, undoText.length());
            hideUndo();
        });
        WindowManager.LayoutParams p = new WindowManager.LayoutParams(
                WindowManager.LayoutParams.WRAP_CONTENT, WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.TYPE_ACCESSIBILITY_OVERLAY,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL,
                PixelFormat.TRANSLUCENT);
        p.gravity = lp.gravity;
        p.x = lp.x + (bubble.getWidth() > 0 ? bubble.getWidth() : dp(70)) + dp(4);
        p.y = lp.y + (bubble.getHeight() > 0 ? bubble.getHeight() : dp(120)) - dp(44);
        try { wm.addView(chip, p); undoChip = chip; } catch (Exception ignored) { }
        h.postDelayed(hideUndoTask, 6000);
    }

    private void hideUndo() {
        h.removeCallbacks(hideUndoTask);
        if (undoChip == null) return;
        try { wm.removeView(undoChip); } catch (Exception ignored) { }
        undoChip = null;
    }

    private void openApp() {
        try {
            startActivity(new Intent(this, MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
        } catch (Exception ignored) { }
    }
}

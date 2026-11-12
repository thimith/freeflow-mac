package com.freeflow.bubble;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Paint;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.os.SystemClock;
import android.view.MotionEvent;
import android.view.View;

import java.util.Random;

/**
 * Slides out beside the bubble while you talk: live waveform + timer + ✕ (cancel).
 * In the Matrix theme your voice becomes a stream of "secret language" glyphs instead.
 * While transcribing it shows a short status ("Transcribing…" / "DECRYPTING…").
 */
public class RecordingStrip extends View {
    private final Theme theme;
    private final float dp;
    private final Runnable onCancel;
    private final Paint bg = new Paint(Paint.ANTI_ALIAS_FLAG), stroke = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint bar = new Paint(Paint.ANTI_ALIAS_FLAG), txt = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final float[] levels = new float[26];
    private final StringBuilder glyphs = new StringBuilder();
    private final Random rnd = new Random();
    private long start = SystemClock.elapsedRealtime();
    private String status;   // non-null → busy text instead of the waveform
    private float spin;
    private boolean pressedX;

    // half-width katakana, digits and symbols — the classic "digital rain" alphabet
    private static final String ALPHABET = "ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ0123456789Z:・.=*+-<>¦|";

    public RecordingStrip(Context c, Theme theme, Runnable onCancel) {
        super(c);
        this.theme = theme;
        this.onCancel = onCancel;
        dp = c.getResources().getDisplayMetrics().density;
        bg.setColor(theme.stripBg);
        stroke.setStyle(Paint.Style.STROKE);
        stroke.setStrokeWidth(theme.strokeDp * dp);
        stroke.setColor(theme.stroke);
        bar.setColor(theme.stripFg);
        txt.setColor(theme.stripFg);
        txt.setTypeface(theme.matrix ? Typeface.MONOSPACE : Typeface.create(Typeface.DEFAULT, Typeface.BOLD));
        setLayerType(LAYER_TYPE_HARDWARE, null);
    }

    public void reset() {
        start = SystemClock.elapsedRealtime();
        status = null;
        glyphs.setLength(0);
        for (int i = 0; i < levels.length; i++) levels[i] = 0;
        invalidate();
    }

    public void setStatus(String s) { status = s; invalidate(); }

    public void push(float level) {
        System.arraycopy(levels, 1, levels, 0, levels.length - 1);
        levels[levels.length - 1] = level;
        if (theme.matrix) {
            // louder → more glyphs; silence → the stream pauses
            int n = level < 0.12f ? 0 : 1 + (int) (level * 3.2f);
            for (int i = 0; i < n; i++) glyphs.append(ALPHABET.charAt(rnd.nextInt(ALPHABET.length())));
            if (level > 0.5f && rnd.nextInt(4) == 0) glyphs.append(' ');
            if (glyphs.length() > 120) glyphs.delete(0, glyphs.length() - 120);
        }
        invalidate();
    }

    private float corner() { return theme.cornerDp < 0 ? getHeight() / 2f : theme.cornerDp * dp; }

    private RectF xBox() {
        float h = getHeight(), s = h - 8 * dp;
        return new RectF(getWidth() - 4 * dp - s, 4 * dp, getWidth() - 4 * dp, 4 * dp + s);
    }

    @Override
    protected void onDraw(Canvas c) {
        float w = getWidth(), h = getHeight(), cy = h / 2f;
        RectF body = new RectF(dp, dp, w - dp, h - dp);
        c.drawRoundRect(body, corner(), corner(), bg);
        c.drawRoundRect(body, corner(), corner(), stroke);

        // ✕ cancel
        RectF x = xBox();
        Paint xb = new Paint(Paint.ANTI_ALIAS_FLAG);
        xb.setColor(pressedX ? theme.stripDim : theme.chip);
        if (theme.square) c.drawRoundRect(x, 3 * dp, 3 * dp, xb); else c.drawOval(x, xb);
        Paint xl = new Paint(Paint.ANTI_ALIAS_FLAG);
        xl.setColor(theme.iconChip);
        xl.setStrokeWidth(2.2f * dp);
        xl.setStrokeCap(Paint.Cap.ROUND);
        float k = x.width() * 0.2f;
        c.drawLine(x.centerX() - k, x.centerY() - k, x.centerX() + k, x.centerY() + k, xl);
        c.drawLine(x.centerX() - k, x.centerY() + k, x.centerX() + k, x.centerY() - k, xl);

        float left = 12 * dp, right = x.left - 8 * dp;

        if (status != null) {
            txt.setTextSize(13 * dp);
            txt.setTextAlign(Paint.Align.LEFT);
            txt.setColor(theme.stripFg);
            // little spinner
            Paint sp = new Paint(Paint.ANTI_ALIAS_FLAG);
            sp.setStyle(Paint.Style.STROKE);
            sp.setStrokeWidth(2 * dp);
            sp.setStrokeCap(Paint.Cap.ROUND);
            sp.setColor(theme.stripFg);
            float r = 7 * dp;
            c.drawArc(new RectF(left, cy - r, left + 2 * r, cy + r), spin, 260, false, sp);
            spin = (spin + 9) % 360;
            c.drawText(status, left + 2 * r + 8 * dp, cy + 4.5f * dp, txt);
            postInvalidateOnAnimation();
            return;
        }

        // timer on the right of the voice area
        long secs = (SystemClock.elapsedRealtime() - start) / 1000;
        String t = (secs / 60) + ":" + String.format("%02d", secs % 60);
        txt.setTextAlign(Paint.Align.RIGHT);
        txt.setTextSize(12 * dp);
        txt.setColor(theme.stripDim);
        c.drawText(t, right, cy + 4 * dp, txt);
        right -= txt.measureText("00:00") + 8 * dp;

        if (theme.matrix) {
            // the secret language: newest glyph brightest, older ones fade to the left
            txt.setTextAlign(Paint.Align.RIGHT);
            txt.setTextSize(15 * dp);
            float cw = txt.measureText("ｱ");
            int max = (int) ((right - left) / cw);
            int n = Math.min(max, glyphs.length());
            for (int i = 0; i < n; i++) {
                char g = glyphs.charAt(glyphs.length() - 1 - i);
                int alpha = i == 0 ? 255 : Math.max(40, 230 - i * 9);
                txt.setColor(i == 0 ? 0xFFCCFFCC : theme.stripFg);
                txt.setAlpha(alpha);
                // a little vertical jitter for the "live" feel
                float jy = (i < 3) ? (rnd.nextFloat() - 0.5f) * 2 * dp : 0;
                c.drawText(String.valueOf(g), right - i * cw, cy + 5.5f * dp + jy, txt);
            }
            txt.setAlpha(255);
            if (n == 0) {
                txt.setColor(theme.stripDim);
                txt.setTextSize(12 * dp);
                c.drawText("> listening_", right, cy + 4 * dp, txt);
            }
        } else {
            // live waveform: newest on the right
            float bw = 3 * dp, gap = 2.4f * dp;
            int fit = (int) ((right - left) / (bw + gap));
            int count = Math.min(fit, levels.length);
            for (int i = 0; i < count; i++) {
                float v = levels[levels.length - 1 - i];
                float bh = Math.max(3 * dp, Math.min(h - 18 * dp, v * (h - 12 * dp)));
                float bx = right - (i + 1) * (bw + gap) + gap;
                bar.setAlpha(i == 0 ? 255 : Math.max(110, 255 - i * 5));
                c.drawRoundRect(new RectF(bx, cy - bh / 2, bx + bw, cy + bh / 2), bw / 2, bw / 2, bar);
            }
            bar.setAlpha(255);
        }
        postInvalidateDelayed(250);   // keep the timer ticking even in silence
    }

    @Override
    public boolean onTouchEvent(MotionEvent e) {
        RectF x = xBox();
        x.inset(-6 * dp, -6 * dp);
        switch (e.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
                pressedX = x.contains(e.getX(), e.getY());
                invalidate();
                return true;
            case MotionEvent.ACTION_UP:
                boolean hit = pressedX && x.contains(e.getX(), e.getY());
                pressedX = false;
                invalidate();
                if (hit && onCancel != null) onCancel.run();
                return true;
            case MotionEvent.ACTION_CANCEL:
                pressedX = false;
                invalidate();
                return true;
        }
        return false;
    }
}

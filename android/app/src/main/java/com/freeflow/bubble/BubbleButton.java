package com.freeflow.bubble;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.view.View;

/** A self-drawn bubble button (mic, translate, ✨ edit), styled by the current Theme. */
public class BubbleButton extends View {
    public enum Kind { MIC, TRANSLATE, EDIT }
    public enum State { IDLE, RECORDING, EDIT_RECORDING, BUSY, DONE, ERROR }

    private final Kind kind;
    private Theme theme;
    private State state = State.IDLE;
    private float level, spin;
    private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint ring = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint icon = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint text = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final float dp;

    public BubbleButton(Context c, Kind kind, Theme theme) {
        super(c);
        this.kind = kind;
        this.theme = theme;
        dp = c.getResources().getDisplayMetrics().density;
        icon.setStyle(Paint.Style.STROKE);
        icon.setStrokeCap(Paint.Cap.ROUND);
        icon.setStrokeJoin(Paint.Join.ROUND);
        text.setTextAlign(Paint.Align.CENTER);
        setLayerType(LAYER_TYPE_HARDWARE, null);
    }

    public void setState(State s) {
        state = s;
        if (s != State.RECORDING && s != State.EDIT_RECORDING) level = 0;
        invalidate();
    }

    public State state() { return state; }

    public void setLevel(float l) { level = level * 0.5f + l * 0.5f; invalidate(); }

    private boolean recording() { return state == State.RECORDING || state == State.EDIT_RECORDING; }

    /** Button outline: circle, or a rounded square for the Matrix theme. */
    private void shape(Canvas c, float cx, float cy, float r, Paint p) {
        if (theme.square) {
            float k = 3 * dp;
            c.drawRoundRect(new RectF(cx - r, cy - r, cx + r, cy + r), k, k, p);
        } else c.drawCircle(cx, cy, r, p);
    }

    @Override
    protected void onDraw(Canvas c) {
        float w = getWidth(), h = getHeight();
        float cx = w / 2f, cy = h / 2f;
        float r = Math.min(w, h) / 2f - 2 * dp;

        // body
        float br = recording() ? r * (0.90f + 0.08f * level) : r;
        fill.setShader(null);
        int iconColor = theme.iconChip;
        switch (state) {
            case RECORDING: fill.setColor(theme.rec); iconColor = theme.matrix ? theme.iconAccent : 0xFFFFFFFF; break;
            case EDIT_RECORDING: fill.setColor(theme.editRec); iconColor = theme.matrix ? theme.iconAccent : 0xFFFFFFFF; break;
            case DONE: fill.setColor(theme.done); iconColor = theme.matrix ? 0xFF000000 : 0xFFFFFFFF; break;
            case ERROR: fill.setColor(theme.err); iconColor = 0xFF000000; break;
            default:
                if (kind == Kind.MIC) {
                    fill.setShader(new LinearGradient(0, 0, w, h, theme.micA, theme.micB, Shader.TileMode.CLAMP));
                    iconColor = theme.iconAccent;
                } else if (kind == Kind.EDIT) {
                    fill.setShader(new LinearGradient(0, 0, w, h, theme.editA, theme.editB, Shader.TileMode.CLAMP));
                    iconColor = theme.matrix ? theme.iconAccent : 0xFFFFFFFF;
                } else fill.setColor(theme.chip);
        }
        shape(c, cx, cy, br, fill);
        fill.setShader(null);
        if (theme.matrix) {
            ring.setStyle(Paint.Style.STROKE);
            ring.setStrokeWidth(1.2f * dp);
            ring.setColor(theme.stroke);
            shape(c, cx, cy, br, ring);
        }

        icon.setColor(iconColor);
        text.setColor(iconColor);
        icon.setStrokeWidth(Math.max(2f, br * 0.11f));

        if (state == State.BUSY) {
            ring.setStyle(Paint.Style.STROKE);
            ring.setColor(iconColor);
            ring.setStrokeWidth(2.4f * dp);
            ring.setStrokeCap(Paint.Cap.ROUND);
            float sr = br * 0.55f;
            c.drawArc(new RectF(cx - sr, cy - sr, cx + sr, cy + sr), spin, 260, false, ring);
            spin = (spin + 10) % 360;
            postInvalidateOnAnimation();
            return;
        }
        if (state == State.DONE) {
            Path p = new Path();
            p.moveTo(cx - br * 0.36f, cy + br * 0.02f);
            p.lineTo(cx - br * 0.08f, cy + br * 0.28f);
            p.lineTo(cx + br * 0.38f, cy - br * 0.24f);
            c.drawPath(p, icon);
            return;
        }
        if (state == State.ERROR) {
            text.setTypeface(Typeface.DEFAULT_BOLD);
            text.setTextSize(br * 1.1f);
            c.drawText("!", cx, cy + br * 0.38f, text);
            return;
        }
        if (recording()) {   // stop square
            icon.setStyle(Paint.Style.FILL);
            float s = br * 0.34f;
            c.drawRoundRect(new RectF(cx - s, cy - s, cx + s, cy + s), s * 0.3f, s * 0.3f, icon);
            icon.setStyle(Paint.Style.STROKE);
            return;
        }
        switch (kind) {
            case MIC: drawMic(c, cx, cy, br); break;
            case EDIT: drawEdit(c, cx, cy, br); break;
            default: drawTranslate(c, cx, cy, br);
        }
    }

    private void drawMic(Canvas c, float cx, float cy, float r) {
        float cw = r * 0.24f, top = cy - r * 0.52f, bot = cy + r * 0.10f;
        icon.setStyle(Paint.Style.FILL);
        c.drawRoundRect(new RectF(cx - cw, top, cx + cw, bot), cw, cw, icon);
        icon.setStyle(Paint.Style.STROKE);
        float ar = r * 0.42f;
        c.drawArc(new RectF(cx - ar, cy - ar - r * 0.12f, cx + ar, cy + ar - r * 0.12f), 15, 150, false, icon);
        c.drawLine(cx, cy + ar - r * 0.12f, cx, cy + r * 0.48f, icon);
    }

    private void drawTranslate(Canvas c, float cx, float cy, float r) {
        text.setTypeface(theme.matrix ? Typeface.MONOSPACE : Typeface.create(Typeface.DEFAULT, Typeface.BOLD));
        text.setTextSize(r * 0.70f);
        c.drawText("文", cx - r * 0.22f, cy + r * 0.04f, text);
        text.setTextSize(r * 0.62f);
        c.drawText("A", cx + r * 0.30f, cy + r * 0.48f, text);
    }

    /** Pencil + sparkles: "improve my text by voice". */
    private void drawEdit(Canvas c, float cx, float cy, float r) {
        float s = r * 0.46f;
        icon.setStyle(Paint.Style.STROKE);
        c.drawLine(cx - s * 0.8f, cy + s * 0.8f, cx + s * 0.45f, cy - s * 0.45f, icon);
        icon.setStyle(Paint.Style.FILL);
        star(c, cx + s * 0.55f, cy - s * 0.80f, r * 0.22f);
        star(c, cx + s * 1.0f, cy + s * 0.05f, r * 0.13f);
        icon.setStyle(Paint.Style.STROKE);
    }

    private void star(Canvas c, float x, float y, float s) {
        Path p = new Path();
        p.moveTo(x, y - s); p.quadTo(x, y, x + s, y); p.quadTo(x, y, x, y + s); p.quadTo(x, y, x - s, y); p.quadTo(x, y, x, y - s);
        c.drawPath(p, icon);
    }
}

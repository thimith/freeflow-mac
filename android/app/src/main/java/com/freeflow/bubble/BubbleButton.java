package com.freeflow.bubble;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.view.View;

/** A round, self-drawn button: the mic bubble or the translate button above it. */
public class BubbleButton extends View {
    public enum Kind { MIC, TRANSLATE }
    public enum State { IDLE, RECORDING, EDIT_RECORDING, BUSY, DONE, ERROR }

    private final Kind kind;
    private State state = State.IDLE;
    private float level;
    private float spin;
    private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint ring = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint icon = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final Paint text = new Paint(Paint.ANTI_ALIAS_FLAG);
    private final float dp;

    public BubbleButton(Context c, Kind kind) {
        super(c);
        this.kind = kind;
        dp = c.getResources().getDisplayMetrics().density;
        ring.setStyle(Paint.Style.STROKE);
        icon.setColor(Color.WHITE);
        icon.setStyle(Paint.Style.STROKE);
        icon.setStrokeCap(Paint.Cap.ROUND);
        text.setColor(Color.WHITE);
        text.setTextAlign(Paint.Align.CENTER);
        text.setTypeface(Typeface.create(Typeface.DEFAULT, Typeface.BOLD));
        setElevation(6 * dp);
    }

    public void setState(State s) {
        state = s;
        if (s != State.RECORDING && s != State.EDIT_RECORDING) level = 0;
        invalidate();
    }

    public State state() { return state; }

    public void setLevel(float l) { level = level * 0.5f + l * 0.5f; invalidate(); }

    @Override
    protected void onDraw(Canvas c) {
        float w = getWidth(), h = getHeight();
        float cx = w / 2f, cy = h / 2f;
        float r = Math.min(w, h) / 2f - 3 * dp;

        // halo that follows your voice
        if (state == State.RECORDING || state == State.EDIT_RECORDING) {
            int col = state == State.RECORDING ? Color.argb(90, 255, 69, 58) : Color.argb(90, 175, 130, 255);
            ring.setColor(col);
            ring.setStyle(Paint.Style.FILL);
            c.drawCircle(cx, cy, r * (0.92f + 0.08f * level) + 2 * dp * level, ring);
            ring.setStyle(Paint.Style.STROKE);
        }

        // body
        float br = (state == State.RECORDING || state == State.EDIT_RECORDING) ? r * (0.86f + 0.06f * level) : r * 0.92f;
        switch (state) {
            case RECORDING: fill.setShader(null); fill.setColor(Color.rgb(255, 59, 48)); break;
            case EDIT_RECORDING: fill.setShader(null); fill.setColor(Color.rgb(150, 90, 255)); break;
            case DONE: fill.setShader(null); fill.setColor(Color.rgb(48, 199, 89)); break;
            case ERROR: fill.setShader(null); fill.setColor(Color.rgb(255, 159, 10)); break;
            default:
                if (kind == Kind.MIC)
                    fill.setShader(new LinearGradient(0, 0, w, h, Color.rgb(124, 77, 255), Color.rgb(40, 110, 255), Shader.TileMode.CLAMP));
                else { fill.setShader(null); fill.setColor(Color.rgb(32, 32, 38)); }
        }
        c.drawCircle(cx, cy, br, fill);
        fill.setShader(null);

        // busy spinner
        if (state == State.BUSY) {
            ring.setColor(Color.WHITE);
            ring.setStrokeWidth(2.5f * dp);
            float sr = br - 4 * dp;
            c.drawArc(new RectF(cx - sr, cy - sr, cx + sr, cy + sr), spin, 260, false, ring);
            spin = (spin + 12) % 360;
            postInvalidateOnAnimation();
        }

        // icon
        icon.setStrokeWidth(Math.max(2f, br * 0.09f));
        if (state == State.DONE) {
            Path p = new Path();
            p.moveTo(cx - br * 0.32f, cy);
            p.lineTo(cx - br * 0.08f, cy + br * 0.24f);
            p.lineTo(cx + br * 0.34f, cy - br * 0.22f);
            c.drawPath(p, icon);
        } else if (state == State.ERROR) {
            text.setTextSize(br * 1.0f);
            c.drawText("!", cx, cy + br * 0.35f, text);
        } else if (state != State.BUSY) {
            if (kind == Kind.MIC) drawMic(c, cx, cy, br);
            else drawTranslate(c, cx, cy, br);
        }
    }

    private void drawMic(Canvas c, float cx, float cy, float r) {
        if (state == State.RECORDING || state == State.EDIT_RECORDING) {   // stop square
            icon.setStyle(Paint.Style.FILL);
            float s = r * 0.32f;
            c.drawRoundRect(new RectF(cx - s, cy - s, cx + s, cy + s), s * 0.3f, s * 0.3f, icon);
            icon.setStyle(Paint.Style.STROKE);
            return;
        }
        float cw = r * 0.22f, ch = r * 0.40f;
        icon.setStyle(Paint.Style.FILL);
        c.drawRoundRect(new RectF(cx - cw, cy - ch - r * 0.12f, cx + cw, cy + ch * 0.55f - r * 0.12f), cw, cw, icon);
        icon.setStyle(Paint.Style.STROKE);
        float ar = r * 0.38f;
        c.drawArc(new RectF(cx - ar, cy - ar - r * 0.12f, cx + ar, cy + ar - r * 0.12f), 15, 150, false, icon);
        c.drawLine(cx, cy + ar - r * 0.12f, cx, cy + r * 0.42f, icon);
        c.drawLine(cx - r * 0.18f, cy + r * 0.42f, cx + r * 0.18f, cy + r * 0.42f, icon);
    }

    private void drawTranslate(Canvas c, float cx, float cy, float r) {
        text.setTextSize(r * 0.62f);
        c.drawText("文", cx - r * 0.2f, cy + r * 0.02f, text);
        text.setTextSize(r * 0.56f);
        c.drawText("A", cx + r * 0.28f, cy + r * 0.42f, text);
        // tiny sparkle = AI
        Paint sp = new Paint(Paint.ANTI_ALIAS_FLAG);
        sp.setColor(Color.rgb(190, 160, 255));
        float sx = cx + r * 0.42f, sy = cy - r * 0.38f, s = r * 0.16f;
        Path p = new Path();
        p.moveTo(sx, sy - s); p.quadTo(sx, sy, sx + s, sy); p.quadTo(sx, sy, sx, sy + s); p.quadTo(sx, sy, sx - s, sy); p.quadTo(sx, sy, sx, sy - s);
        c.drawPath(p, sp);
    }
}

package com.freeflow.bubble;

import android.graphics.Color;

/** Five looks for the bubble. */
public final class Theme {
    public final String id, name, blurb;
    public final int capsule, stroke;
    public final float strokeDp, cornerDp;       // cornerDp < 0 → fully rounded pill
    public final int micA, micB, chip, editA, editB;
    public final int iconAccent, iconChip;       // icon colour on gradient buttons / on plain chips
    public final int rec, editRec, done, err;
    public final boolean square, matrix;
    public final int stripBg, stripFg, stripDim;

    private Theme(String id, String name, String blurb, int capsule, int stroke, float strokeDp, float cornerDp,
                  int micA, int micB, int chip, int editA, int editB, int iconAccent, int iconChip,
                  int rec, int editRec, int done, int err, boolean square, boolean matrix,
                  int stripBg, int stripFg, int stripDim) {
        this.id = id; this.name = name; this.blurb = blurb;
        this.capsule = capsule; this.stroke = stroke; this.strokeDp = strokeDp; this.cornerDp = cornerDp;
        this.micA = micA; this.micB = micB; this.chip = chip; this.editA = editA; this.editB = editB;
        this.iconAccent = iconAccent; this.iconChip = iconChip;
        this.rec = rec; this.editRec = editRec; this.done = done; this.err = err;
        this.square = square; this.matrix = matrix;
        this.stripBg = stripBg; this.stripFg = stripFg; this.stripDim = stripDim;
    }

    public static final Theme[] ALL = {
            new Theme("aurora", "Aurora", "The original: deep night with a purple-blue glow",
                    Color.argb(240, 18, 18, 24), Color.argb(175, 255, 255, 255), 2f, -1,
                    Color.rgb(124, 77, 255), Color.rgb(40, 110, 255), Color.rgb(64, 64, 80),
                    Color.rgb(170, 110, 255), Color.rgb(236, 72, 153), Color.WHITE, Color.WHITE,
                    Color.rgb(255, 59, 48), Color.rgb(150, 90, 255), Color.rgb(48, 199, 89), Color.rgb(255, 159, 10),
                    false, false, Color.argb(240, 18, 18, 24), Color.WHITE, Color.argb(120, 255, 255, 255)),
            new Theme("glass", "Glass", "Light and airy, frosted white",
                    Color.argb(238, 250, 250, 253), Color.argb(90, 0, 0, 30), 1.5f, -1,
                    Color.rgb(91, 140, 255), Color.rgb(110, 205, 255), Color.rgb(228, 230, 239),
                    Color.rgb(255, 154, 213), Color.rgb(177, 140, 255), Color.WHITE, Color.rgb(32, 32, 48),
                    Color.rgb(255, 69, 58), Color.rgb(177, 140, 255), Color.rgb(52, 199, 89), Color.rgb(255, 159, 10),
                    false, false, Color.argb(238, 250, 250, 253), Color.rgb(32, 32, 48), Color.argb(110, 32, 32, 48)),
            new Theme("sunset", "Sunset", "Warm coral and pink",
                    Color.argb(242, 40, 16, 26), Color.argb(170, 255, 205, 175), 2f, -1,
                    Color.rgb(255, 122, 89), Color.rgb(255, 61, 127), Color.rgb(92, 42, 58),
                    Color.rgb(255, 199, 95), Color.rgb(255, 111, 145), Color.WHITE, Color.WHITE,
                    Color.rgb(255, 59, 48), Color.rgb(255, 150, 80), Color.rgb(80, 210, 120), Color.rgb(255, 200, 60),
                    false, false, Color.argb(242, 40, 16, 26), Color.rgb(255, 235, 225), Color.argb(120, 255, 205, 175)),
            new Theme("mono", "Mono", "Minimal black and white",
                    Color.argb(246, 0, 0, 0), Color.argb(235, 255, 255, 255), 2f, -1,
                    Color.WHITE, Color.rgb(220, 220, 220), Color.rgb(44, 44, 44),
                    Color.rgb(200, 200, 200), Color.rgb(150, 150, 150), Color.BLACK, Color.WHITE,
                    Color.rgb(255, 59, 48), Color.rgb(170, 170, 170), Color.WHITE, Color.rgb(255, 159, 10),
                    false, false, Color.argb(246, 0, 0, 0), Color.WHITE, Color.argb(120, 255, 255, 255)),
            new Theme("matrix", "Matrix", "Old-school terminal — and your voice in a secret language",
                    Color.argb(246, 0, 8, 0), Color.argb(210, 0, 255, 65), 1.5f, 5,
                    Color.rgb(0, 40, 6), Color.rgb(0, 70, 12), Color.rgb(0, 24, 4),
                    Color.rgb(0, 55, 10), Color.rgb(0, 90, 20), Color.rgb(0, 255, 65), Color.rgb(0, 255, 65),
                    Color.rgb(0, 120, 25), Color.rgb(0, 95, 20), Color.rgb(0, 255, 65), Color.rgb(200, 255, 0),
                    true, true, Color.argb(246, 0, 8, 0), Color.rgb(0, 255, 65), Color.argb(150, 0, 160, 40)),
    };

    public static Theme byId(String id) {
        for (Theme t : ALL) if (t.id.equals(id)) return t;
        return ALL[0];
    }
}

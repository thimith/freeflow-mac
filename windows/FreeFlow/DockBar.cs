namespace FreeFlow;

/// The floating bubble — and the one place that shows what FreeFlow is doing.
/// Idle: 🎙 · 文A · 📋.  Recording: ■ · waveform + timer · ✕.  Working: spinner · "Transcribing…".
/// Done: ✓ pop · "Inserted · 1.2s" · 文A · 📋.  Springy width changes, hover/press bounce. Never takes focus.
sealed class DockBar : LayeredWindow
{
    public enum DockState { Idle, Recording, Busy }

    public Action Mic, Translate, Recent, Cancel;
    public Func<bool> CardOpen = () => false;

    Phase phase = Phase.Done;
    bool idle = true;
    string text = "";
    readonly float[] levels = new float[24];
    DateTime start = DateTime.Now, doneAt = DateTime.MinValue;
    int spin, hot = -1, downId = -1;
    bool dragging, pressed, transient;
    Point downScreen, downOrigin;

    // animation state
    float curW = -1, tweenFrom, tweenTo = -1;
    DateTime tweenStart;
    const float TweenMs = 300;
    float appear = 1;
    float level;
    readonly float[] hov = new float[4], pressS = new float[4];
    int pressedId = -1;
    readonly System.Windows.Forms.Timer frame = new() { Interval = 16 };
    readonly System.Windows.Forms.Timer back = new();

    readonly List<(RectangleF r, int id)> hits = new();
    const int HitMic = 0, HitTranslate = 1, HitRecent = 2, HitCancel = 3;

    static readonly Color Bg = Color.FromArgb(246, 20, 20, 26);
    static readonly Color Rim = Color.FromArgb(190, 255, 255, 255);
    static readonly Color Purple = Color.FromArgb(124, 77, 255), Blue = Color.FromArgb(40, 110, 255);
    static readonly Color Chip = Color.FromArgb(62, 62, 78);
    static readonly Color Green = Color.FromArgb(48, 199, 89);

    public DockBar()
    {
        frame.Tick += (_, _) => Tick();
        back.Tick += (_, _) => { back.Stop(); GoIdle(); };
    }

    bool Recording => !idle && phase is Phase.Listening or Phase.HandsFree or Phase.Command;
    bool Busy => !idle && phase is Phase.Transcribing or Phase.Polishing or Phase.Thinking or Phase.Translating;
    bool ShowTranslate => Settings.Current.TranslateAfterDictation;
    bool JustDone => phase == Phase.Done && !idle && (DateTime.Now - doneAt).TotalMilliseconds < 900;

    // ───────── public API ─────────

    public void ShowDock()
    {
        transient = false;
        if (!Settings.Current.ShowDock) { if (idle) Hide(); return; }
        if (!Visible) PopIn();
        Kick();
    }

    public void SetState(DockState s) { }

    public void ShowPhase(Phase p, string msg = "")
    {
        lock (levels) Array.Clear(levels);
        start = DateTime.Now;
        SetPhase(p, msg);
    }

    public void SetPhase(Phase p, string msg = "")
    {
        back.Stop();
        idle = false;
        phase = p;
        text = msg ?? "";
        if (p == Phase.Done) doneAt = DateTime.Now;
        if (!Settings.Current.ShowDock && !Visible) transient = true;
        if (p == Phase.Done) { back.Interval = transient ? 5000 : 2600; back.Start(); }
        if (p == Phase.Message) { back.Interval = 3000; back.Start(); }
        if (!Visible) PopIn();
        Kick();
    }

    public void GoIdle()
    {
        back.Stop();
        idle = true;
        text = "";
        if (transient || !Settings.Current.ShowDock)
        {
            if (CardOpen()) { back.Interval = 1500; back.Start(); return; }
            transient = false;
            Hide();
            return;
        }
        Kick();
    }

    public void PushLevel(float l)
    {
        lock (levels)
        {
            Array.Copy(levels, 1, levels, 0, levels.Length - 1);
            levels[^1] = l;
        }
    }

    void PopIn()
    {
        appear = 0.9f;
        Show();
    }

    /// Start the animation loop (it stops itself once everything has settled).
    void Kick()
    {
        if (curW < 0) curW = TargetWidth();
        if (!frame.Enabled) frame.Start();
        Tick();
    }

    void Tick()
    {
        float target = TargetWidth();
        if (Math.Abs(target - tweenTo) > 0.5f)
        {
            // new size: ease from wherever we are now to the new width
            tweenFrom = curW < 0 ? target : curW;
            tweenTo = target;
            tweenStart = DateTime.Now;
        }
        float tw = (float)(DateTime.Now - tweenStart).TotalMilliseconds / TweenMs;
        curW = tweenFrom + (tweenTo - tweenFrom) * EaseInOut(tw);
        appear += (1f - appear) * 0.22f;
        for (int i = 0; i < 4; i++)
        {
            hov[i] += ((hot == i ? 1f : 0f) - hov[i]) * 0.22f;
            pressS[i] += ((pressedId == i ? -0.07f : 0f) - pressS[i]) * 0.3f;
        }
        float lv;
        lock (levels) lv = levels[^1];
        level += (lv - level) * 0.35f;
        spin++;

        if (Visible) Redraw();

        bool settled = tw >= 1 && Math.Abs(appear - 1) < 0.003f
                       && hov.All(h => h < 0.01f || h > 0.99f) && pressS.All(v => Math.Abs(v) < 0.002f || pressedId >= 0);
        if (settled && !Recording && !Busy && !JustDone)
        {
            curW = target;
            frame.Stop();
            if (Visible) Redraw();
        }
    }

    // ───────── layout ─────────

    int H => D(56);
    int M => D(10);   // margin so bounces and pops never get clipped
    int MicD => D(46);
    int ChipD => D(38);
    int Pad => D(5);
    int Gap => D(6);
    const int Bars = 22;
    float BarW => 3 * S;
    float BarGap => 2.2f * S;
    int WaveArea => (int)Math.Ceiling(Bars * (BarW + BarGap)) + D(10) + D(40);   // bars · gap · timer

    static Font LabelFont => new("Segoe UI Semibold", 9.5f);

    string Label => phase switch
    {
        Phase.Transcribing => "Transcribing…",
        Phase.Polishing => "Polishing…",
        Phase.Thinking => "Thinking…",
        Phase.Translating => "Translating…",
        Phase.Done => string.IsNullOrEmpty(text) ? "Inserted" : $"Inserted · {text}",
        Phase.Message => text,
        _ => "",
    };

    float LabelWidth()
    {
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        using var f = LabelFont;
        return Math.Min(D(380), g.MeasureString(Label, f).Width + D(14));
    }

    bool ShowsLabel => Busy || (!idle && Settings.Current.ShowOverlay);

    float TargetWidth()
    {
        float w = Pad + MicD;
        if (Recording) w += Gap + WaveArea + Gap + ChipD;
        else
        {
            if (ShowsLabel) w += Gap + LabelWidth();
            if (!Busy)
            {
                if (ShowTranslate) w += Gap + ChipD;
                w += Gap + ChipD;
            }
        }
        return w + Pad + D(2);
    }

    int CompactWidth => Pad + MicD + (ShowTranslate ? Gap + ChipD : 0) + Gap + ChipD + Pad + D(2);

    /// Top-left of the compact bubble (saved when you drag it).
    Point Origin
    {
        get
        {
            var s = Settings.Current;
            var wa = Screen.PrimaryScreen.WorkingArea;
            var def = new Point(wa.Right - CompactWidth - D(24), wa.Bottom - H - D(24));
            var p = s.DockX >= 0 ? new Point(s.DockX, s.DockY) : def;
            if (!Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(p, new Size(CompactWidth, H))))) p = def;
            return p;
        }
    }

    bool GrowLeft
    {
        get
        {
            var o = Origin;
            var wa = Screen.FromPoint(o).WorkingArea;
            return o.X + CompactWidth / 2 > wa.Left + wa.Width / 2;
        }
    }

    /// Screen rectangle of the capsule (without the animation margin).
    public Rectangle CapsuleBounds
    {
        get
        {
            var o = Origin;
            int w = (int)Math.Ceiling(Math.Max(curW < 0 ? TargetWidth() : curW, MicD + 2 * Pad));
            int x = GrowLeft ? o.X + CompactWidth - w : o.X;
            var wa = Screen.FromPoint(o).WorkingArea;
            x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - w));
            return new Rectangle(x, o.Y, w, H);
        }
    }

    void Redraw()
    {
        var c = CapsuleBounds;
        Present(new Rectangle(c.X - M, c.Y - M, c.Width + 2 * M, c.Height + 2 * M));
    }

    // ───────── drawing ─────────

    RectangleF innerBox;

    /// How much of an element fits inside the capsule right now (0..1) — elements grow in as the bubble opens.
    float Fit(RectangleF r)
    {
        if (innerBox.Width <= 0) return 1;
        float visible = Math.Min(r.Right, innerBox.Right) - Math.Max(r.Left, innerBox.Left);
        return Math.Clamp(visible / r.Width, 0, 1);
    }

    RectangleF Scaled(RectangleF r, int id, float extra = 0)
    {
        float fit = Fit(r);
        float s = (1 + 0.06f * hov[id] + pressS[id] + extra) * (0.35f + 0.65f * EaseOut(fit));
        if (fit <= 0.02f) return RectangleF.Empty;
        if (fit < 1)
        {
            // keep the shrunken element inside the capsule
            float w0 = r.Width * s, cxx = Math.Clamp(r.X + r.Width / 2, innerBox.Left + w0 / 2, innerBox.Right - w0 / 2);
            return new RectangleF(cxx - w0 / 2, r.Y + (r.Height - w0) / 2, w0, w0);
        }
        float w = r.Width * s, h = r.Height * s;
        return new RectangleF(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
    }

    protected override void Render(Graphics g, Size size)
    {
        hits.Clear();
        // pop-in: scale the whole bubble around its centre
        if (Math.Abs(appear - 1) > 0.001f)
        {
            g.TranslateTransform(size.Width / 2f, size.Height / 2f);
            g.ScaleTransform(appear, appear);
            g.TranslateTransform(-size.Width / 2f, -size.Height / 2f);
        }

        var body = new RectangleF(M, M, size.Width - 2 * M - 1, size.Height - 2 * M - 1);
        using (var path = Capsule(body))
        {
            using var b = new SolidBrush(Bg);
            g.FillPath(b, path);
            using var pen = new Pen(Rim, Math.Max(2f, 2.4f * S));
            g.DrawPath(pen, path);
        }

        // content is laid out for the final width and pinned to the bubble's fixed edge while the capsule springs
        float target = TargetWidth();
        float dx = GrowLeft ? body.Width - target : 0;
        var clip = g.Save();
        var inner = RectangleF.Inflate(body, -D(2), -D(2));
        using (var cp = Capsule(inner)) g.SetClip(cp);

        innerBox = new RectangleF(body.X + Pad * 0.5f, body.Y, body.Width - Pad, body.Height);
        float cy = body.Y + body.Height / 2f;
        float x = body.X + dx + Pad + D(1);

        // ── mic / stop / spinner / ✓
        var mic = new RectangleF(x, cy - MicD / 2f, MicD, MicD);
        if (Recording)
        {
            float halo = MicD / 2f + D(2) + level * D(9);
            using var hb = new SolidBrush(Color.FromArgb(70, 255, 69, 58));
            g.FillEllipse(hb, mic.X + mic.Width / 2 - halo, mic.Y + mic.Height / 2 - halo, halo * 2, halo * 2);
        }
        float donePop = 0;
        if (JustDone)
        {
            float t = (float)(DateTime.Now - doneAt).TotalMilliseconds / 900f;
            donePop = 0.04f * EaseInOut(MathF.Min(1, t * 3)) * (1 - EaseInOut(t));
        }
        var micR = Scaled(mic, HitMic, donePop);
        if (Recording) Circle(g, micR, Color.FromArgb(255, 69, 58), Color.FromArgb(230, 40, 40));
        else if (Busy) Circle(g, micR, Color.FromArgb(70, 70, 88), Color.FromArgb(70, 70, 88));
        else if (JustDone) Circle(g, micR, Green, Color.FromArgb(30, 170, 80));
        else Circle(g, micR, Purple, Blue);
        if (micR.Width < 2) { }
        else if (Recording)
        {
            float s = micR.Width * 0.30f;
            using var w = new SolidBrush(Color.White);
            using var sq = RoundRect(new RectangleF(micR.X + micR.Width / 2 - s / 2, micR.Y + micR.Height / 2 - s / 2, s, s), s * 0.2f);
            g.FillPath(w, sq);
        }
        else if (Busy)
        {
            using var pen = new Pen(Color.White, 2.4f * S) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            g.DrawArc(pen, RectangleF.Inflate(micR, -MicD * 0.28f, -MicD * 0.28f), (spin * 14) % 360, 270);
        }
        else if (JustDone)
        {
            using var pen = new Pen(Color.White, 2.8f * S) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            float cx = micR.X + micR.Width / 2, ccy = micR.Y + micR.Height / 2, r = micR.Width / 2;
            g.DrawLines(pen, new[] { new PointF(cx - r * 0.34f, ccy + r * 0.02f), new PointF(cx - r * 0.08f, ccy + r * 0.28f), new PointF(cx + r * 0.36f, ccy - r * 0.24f) });
        }
        else if (micR.Width > 1) MicIcon(g, micR, Color.White);
        if (Fit(mic) > 0.9f) hits.Add((mic, HitMic));
        x += MicD;

        if (Recording)
        {
            x += Gap;
            float[] snap;
            lock (levels) snap = (float[])levels.Clone();
            using (var wb = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                for (int i = 0; i < Bars; i++)
                {
                    float v = snap[snap.Length - Bars + i];
                    float bh = Math.Max(3 * S, Math.Min(26 * S, v * 30 * S));
                    using var bar = RoundRect(new RectangleF(x + i * (BarW + BarGap), cy - bh / 2, BarW, bh), BarW / 2);
                    g.FillPath(wb, bar);
                }
            var secs = (int)(DateTime.Now - start).TotalSeconds;
            float timerX = x + Bars * (BarW + BarGap) + D(10);
            using (var tf = new Font("Segoe UI Semibold", 9f))
                Text(g, $"{secs / 60}:{secs % 60:00}", new RectangleF(timerX, cy - D(10), D(40), D(20)), tf, Color.FromArgb(185, 255, 255, 255), StringAlignment.Near);
            x += WaveArea + Gap;
            var cancel = new RectangleF(x, cy - ChipD / 2f, ChipD, ChipD);
            var cr = Scaled(cancel, HitCancel);
            if (cr.Width > 1) { Circle(g, cr, Chip, Chip); CrossIcon(g, cr, Color.White); }
            if (Fit(cancel) > 0.9f) hits.Add((cancel, HitCancel));
            g.Restore(clip);
            return;
        }

        if (ShowsLabel)
        {
            x += Gap;
            float lw = LabelWidth();
            var color = phase == Phase.Message ? Color.FromArgb(255, 214, 10) : phase == Phase.Done ? Color.FromArgb(130, 235, 160) : Color.White;
            using (var f = LabelFont)
                Text(g, Label, new RectangleF(x, cy - D(12), lw, D(24)), f, color, StringAlignment.Near);
            x += lw;
        }

        if (!Busy)
        {
            if (ShowTranslate)
            {
                x += Gap;
                var tr = new RectangleF(x, cy - ChipD / 2f, ChipD, ChipD);
                var trR = Scaled(tr, HitTranslate);
                if (trR.Width > 1)
                {
                    Circle(g, trR, Chip, Chip);
                    using var f = new Font("Segoe UI", Math.Max(1f, 9.5f * (trR.Width / ChipD)), FontStyle.Bold);
                    Text(g, "文A", trR, f, Color.White);
                }
                if (Fit(tr) > 0.9f) hits.Add((tr, HitTranslate));
                x += ChipD;
            }
            x += Gap;
            var rc = new RectangleF(x, cy - ChipD / 2f, ChipD, ChipD);
            var rcR = Scaled(rc, HitRecent);
            if (rcR.Width > 1) { Circle(g, rcR, Chip, Chip); ClipboardIcon(g, rcR, Color.White); }
            if (Fit(rc) > 0.9f) hits.Add((rc, HitRecent));
        }
        g.Restore(clip);
    }

    // ───────── input ─────────

    int HitTest(Point p)
    {
        foreach (var (r, id) in hits) if (RectangleF.Inflate(r, D(2), D(2)).Contains(p)) return id;
        return -1;
    }

    protected override void OnMouseLeave(EventArgs e) { if (hot != -1) { hot = -1; Kick(); } base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        pressed = true;
        dragging = false;
        downScreen = Cursor.Position;
        downOrigin = Origin;
        downId = HitTest(e.Location);
        if (downId >= 0) { pressedId = downId; Kick(); }   // gentle press-in
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != hot) { hot = h; Kick(); }
        if (!pressed) return;
        var c = Cursor.Position;
        if (!dragging && (Math.Abs(c.X - downScreen.X) > D(5) || Math.Abs(c.Y - downScreen.Y) > D(5))) dragging = true;
        if (dragging)
        {
            var s = Settings.Current;
            s.DockX = downOrigin.X + (c.X - downScreen.X);
            s.DockY = downOrigin.Y + (c.Y - downScreen.Y);
            Redraw();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!pressed) return;
        pressed = false;
        pressedId = -1;
        Kick();
        if (dragging) { dragging = false; Settings.Current.Save(); return; }
        int id = HitTest(e.Location);
        switch (id)
        {
            case HitMic: Mic?.Invoke(); break;
            case HitTranslate: Translate?.Invoke(); break;
            case HitRecent: Recent?.Invoke(); break;
            case HitCancel: Cancel?.Invoke(); break;
        }
    }
}

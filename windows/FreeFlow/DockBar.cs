namespace FreeFlow;

/// The floating bubble — and the one place that shows what FreeFlow is doing.
/// Idle: 🎙 · 文A · 📋.  Recording: ■ · waveform + timer · ✕.  Working: spinner · "Transcribing…".
/// Done: 🎙 · "Inserted · 1.2s" · 文A · 📋.  Drag it anywhere; it never takes focus.
sealed class DockBar : LayeredWindow
{
    public enum DockState { Idle, Recording, Busy }

    public Action Mic, Translate, Recent, Cancel;
    public Func<bool> CardOpen = () => false;

    Phase phase = Phase.Done;
    bool idle = true;
    string text = "";
    readonly float[] levels = new float[24];
    DateTime start = DateTime.Now;
    int spin, hot = -1;
    bool dragging, pressed;
    Point downScreen, downOrigin;
    bool transient;   // shown only for this activity (bubble turned off in settings)

    readonly System.Windows.Forms.Timer anim = new() { Interval = 33 };
    readonly System.Windows.Forms.Timer back = new();

    readonly List<(RectangleF r, int id)> hits = new();
    const int HitMic = 0, HitTranslate = 1, HitRecent = 2, HitCancel = 3;

    static readonly Color Bg = Color.FromArgb(246, 20, 20, 26);
    static readonly Color Rim = Color.FromArgb(120, 255, 255, 255);
    static readonly Color Purple = Color.FromArgb(124, 77, 255), Blue = Color.FromArgb(40, 110, 255);
    static readonly Color Chip = Color.FromArgb(62, 62, 78);

    public DockBar()
    {
        anim.Tick += (_, _) => { spin++; Redraw(); };
        back.Tick += (_, _) => { back.Stop(); GoIdle(); };
    }

    bool Recording => !idle && phase is Phase.Listening or Phase.HandsFree or Phase.Command;
    bool Busy => !idle && phase is Phase.Transcribing or Phase.Polishing or Phase.Thinking or Phase.Translating;
    bool ShowTranslate => Settings.Current.TranslateAfterDictation;

    // ───────── public API (used by Overlay + TrayApp) ─────────

    public void ShowDock()
    {
        transient = false;
        if (!Settings.Current.ShowDock) { if (idle) Hide(); return; }
        Redraw();
        if (!Visible) Show();
    }

    public void SetState(DockState s) { /* state is driven by phases now */ }

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
        if (!Settings.Current.ShowDock && !Visible) transient = true;
        if (Recording || Busy) anim.Start(); else anim.Stop();
        if (p == Phase.Done) { back.Interval = transient ? 5000 : 2500; back.Start(); }
        if (p == Phase.Message) { back.Interval = 3000; back.Start(); }
        Redraw();
        if (!Visible) Show();
    }

    public void GoIdle()
    {
        back.Stop();
        anim.Stop();
        idle = true;
        text = "";
        if (transient || !Settings.Current.ShowDock)
        {
            if (CardOpen()) { back.Interval = 1500; back.Start(); return; }
            transient = false;
            Hide();
            return;
        }
        Redraw();
    }

    public void PushLevel(float l)
    {
        lock (levels)
        {
            Array.Copy(levels, 1, levels, 0, levels.Length - 1);
            levels[^1] = l;
        }
    }

    // ───────── layout ─────────

    int H => D(56);
    int MicD => D(46);
    int ChipD => D(38);
    int Pad => D(5);
    int Gap => D(6);

    Font LabelFont => new("Segoe UI Semibold", 9.5f);

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

    /// Width for the current state (computed from the same layout used to draw).
    int CurrentWidth()
    {
        float w = Pad + MicD;
        if (Recording) w += Gap + D(150) + Gap + ChipD;
        else if (Busy) w += Gap + LabelWidth();
        else
        {
            if (!idle && Settings.Current.ShowOverlay) w += Gap + LabelWidth();
            if (ShowTranslate) w += Gap + ChipD;
            w += Gap + ChipD;
        }
        return (int)Math.Ceiling(w + Pad + D(2));
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

    Rectangle TargetBounds()
    {
        var o = Origin;
        int w = CurrentWidth();
        var wa = Screen.FromPoint(o).WorkingArea;
        // grow toward the free side: leftward when the bubble sits on the right half of the screen
        bool growLeft = o.X + CompactWidth / 2 > wa.Left + wa.Width / 2;
        int x = growLeft ? o.X + CompactWidth - w : o.X;
        x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - w));
        return new Rectangle(x, o.Y, w, H);
    }

    void Redraw() => Present(TargetBounds());

    // ───────── drawing ─────────

    protected override void Render(Graphics g, Size size)
    {
        hits.Clear();
        var body = new RectangleF(D(1), D(1), size.Width - D(2) - 1, size.Height - D(2) - 1);
        using (var path = Capsule(body))
        {
            using var b = new SolidBrush(Bg);
            g.FillPath(b, path);
            using var pen = new Pen(Rim, Math.Max(1f, 1.4f * S));
            g.DrawPath(pen, path);
        }

        float cy = size.Height / 2f;
        float x = Pad + D(1);

        // mic / stop / spinner
        var mic = new RectangleF(x, cy - MicD / 2f, MicD, MicD);
        if (Recording) Circle(g, mic, Color.FromArgb(255, 69, 58), Color.FromArgb(230, 40, 40));
        else if (Busy) Circle(g, mic, Color.FromArgb(70, 70, 88), Color.FromArgb(70, 70, 88));
        else Circle(g, mic, Purple, Blue);
        if (hot == HitMic) Ring(g, mic);
        if (Recording)
        {
            float s = MicD * 0.30f;
            using var w = new SolidBrush(Color.White);
            using var sq = RoundRect(new RectangleF(mic.X + mic.Width / 2 - s / 2, mic.Y + mic.Height / 2 - s / 2, s, s), s * 0.2f);
            g.FillPath(w, sq);
        }
        else if (Busy)
        {
            using var pen = new Pen(Color.White, 2.4f * S) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            var r = RectangleF.Inflate(mic, -MicD * 0.28f, -MicD * 0.28f);
            g.DrawArc(pen, r, (spin * 22) % 360, 270);
        }
        else
        {
            using var f = new Font("Segoe UI Emoji", 14f);
            Text(g, "🎙", mic, f, Color.White);
        }
        hits.Add((mic, HitMic));
        x += MicD;

        if (Recording)
        {
            // waveform + timer
            x += Gap;
            float[] snap;
            lock (levels) snap = (float[])levels.Clone();
            float bw = 3 * S, bg = 2.2f * S;
            using (var wb = new SolidBrush(Color.FromArgb(235, 255, 255, 255)))
                for (int i = 0; i < snap.Length; i++)
                {
                    float bh = Math.Max(3 * S, Math.Min(26 * S, snap[i] * 30 * S));
                    using var bar = RoundRect(new RectangleF(x + i * (bw + bg), cy - bh / 2, bw, bh), bw / 2);
                    g.FillPath(wb, bar);
                }
            var secs = (int)(DateTime.Now - start).TotalSeconds;
            using (var tf = new Font("Consolas", 9f))
                Text(g, $"{secs / 60}:{secs % 60:00}", new RectangleF(x + D(150) - D(38), cy - D(10), D(38), D(20)), tf, Color.FromArgb(170, 255, 255, 255), StringAlignment.Far);
            x += D(150) + Gap;
            var cancel = new RectangleF(x, cy - ChipD / 2f, ChipD, ChipD);
            Circle(g, cancel, Chip, Chip);
            if (hot == HitCancel) Ring(g, cancel);
            using (var f = new Font("Segoe UI", 11f, FontStyle.Bold)) Text(g, "✕", cancel, f, Color.White);
            hits.Add((cancel, HitCancel));
            return;
        }

        if (Busy || (!idle && Settings.Current.ShowOverlay))
        {
            x += Gap;
            float lw = LabelWidth();
            var color = phase == Phase.Message ? Color.FromArgb(255, 214, 10) : phase == Phase.Done ? Color.FromArgb(120, 230, 150) : Color.White;
            using (var f = LabelFont)
                Text(g, Label, new RectangleF(x, cy - D(12), lw, D(24)), f, color, StringAlignment.Near);
            x += lw;
            if (Busy) return;
        }

        if (ShowTranslate)
        {
            x += Gap;
            var tr = new RectangleF(x, cy - ChipD / 2f, ChipD, ChipD);
            Circle(g, tr, Chip, Chip);
            if (hot == HitTranslate) Ring(g, tr);
            using (var f = new Font("Segoe UI", 9.5f, FontStyle.Bold)) Text(g, "文A", tr, f, Color.White);
            hits.Add((tr, HitTranslate));
            x += ChipD;
        }
        x += Gap;
        var rc = new RectangleF(x, cy - ChipD / 2f, ChipD, ChipD);
        Circle(g, rc, Chip, Chip);
        if (hot == HitRecent) Ring(g, rc);
        using (var f = new Font("Segoe UI Emoji", 11f)) Text(g, "📋", rc, f, Color.White);
        hits.Add((rc, HitRecent));
    }

    void Ring(Graphics g, RectangleF r)
    {
        using var p = new Pen(Color.FromArgb(210, 255, 255, 255), 2f * S);
        g.DrawEllipse(p, RectangleF.Inflate(r, -1, -1));
    }

    // ───────── input ─────────

    int HitTest(Point p)
    {
        foreach (var (r, id) in hits) if (r.Contains(p)) return id;
        return -1;
    }

    protected override void OnMouseLeave(EventArgs e) { if (hot != -1) { hot = -1; Redraw(); } base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        pressed = true;
        dragging = false;
        downScreen = Cursor.Position;
        downOrigin = Origin;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != hot) { hot = h; Redraw(); }
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
        if (dragging) { dragging = false; Settings.Current.Save(); return; }
        switch (HitTest(e.Location))
        {
            case HitMic: Mic?.Invoke(); break;
            case HitTranslate: Translate?.Invoke(); break;
            case HitRecent: Recent?.Invoke(); break;
            case HitCancel: Cancel?.Invoke(); break;
        }
    }
}

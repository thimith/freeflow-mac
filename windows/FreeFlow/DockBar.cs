using System.Drawing.Drawing2D;

namespace FreeFlow;

/// The always-on floating bubble: a small mic you can drag anywhere. Hover (or finish a dictation)
/// and 文A + 📋 slide out above it. Never takes focus from the app you're typing in.
sealed class DockBar : Form
{
    public enum DockState { Idle, Recording, Busy }

    public Action Mic, Translate, Recent;
    /// True while a card (languages / recent dictations) is open next to the bubble — keeps it expanded.
    public Func<bool> CardOpen = () => false;

    DockState state = DockState.Idle;
    bool dragging, pressed;
    Point downScreen, downLocation;
    int hot = -1;   // hovered button index

    const int Size1 = 48, Gap = 6;
    static readonly Color Purple = Color.FromArgb(124, 77, 255), Blue = Color.FromArgb(40, 110, 255);

    public DockBar()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(22, 22, 28);
        DoubleBuffered = true;
        Opacity = 0.96;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/ | 0x8 /*TOPMOST*/;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
        base.WndProc(ref m);
    }

    float S => DeviceDpi / 96f;
    int D(float v) => (int)(v * S);

    // ───────── placement ─────────

    int Btn => D(40);
    int MicSz => D(Size1);
    bool HasTranslate => Settings.Current.TranslateAfterDictation;
    int Count => HasTranslate ? 3 : 2;
    int DockW => D(6) + MicSz + (Count - 1) * (D(Gap) + Btn) + D(6);
    int DockH => MicSz + D(8);

    /// Top-left of the bubble, in screen coordinates (saved when you drag it).
    Point Origin
    {
        get
        {
            var s = Settings.Current;
            var wa = Screen.PrimaryScreen.WorkingArea;
            var def = new Point(wa.Right - DockW - D(24), wa.Bottom - DockH - D(24));
            var p = s.DockX >= 0 ? new Point(s.DockX, s.DockY) : def;
            if (!Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(p, new Size(DockW, DockH))))) p = def;
            return p;
        }
    }

    public void ShowDock()
    {
        if (!Settings.Current.ShowDock) { Hide(); return; }
        Layout2();
        if (!Visible) Show();
        TopMost = true;
    }

    void Layout2()
    {
        Bounds = new Rectangle(Origin, new Size(DockW, DockH));
        using var path = Capsule(new Rectangle(0, 0, DockW, DockH));
        Region = new Region(path);
        Invalidate();
    }

    public void SetState(DockState s) { state = s; Invalidate(); }

    /// Kept for compatibility: the bubble is always fully visible now.
    public void ExpandFor(int ms) { if (Settings.Current.ShowDock && !Visible) ShowDock(); TopMost = true; }

    // ───────── input ─────────

    Rectangle MicRect => new(D(6), D(4), MicSz, MicSz);
    Rectangle SmallRect(int i) => new(D(6) + MicSz + D(Gap) + i * (Btn + D(Gap)), (DockH - Btn) / 2, Btn, Btn);

    /// 0 = mic, 1 = 文A (if shown), 2 = 📋
    int HitTest(Point p)
    {
        if (MicRect.Contains(p)) return 0;
        if (HasTranslate)
        {
            if (SmallRect(0).Contains(p)) return 1;
            if (SmallRect(1).Contains(p)) return 2;
        }
        else if (SmallRect(0).Contains(p)) return 2;
        return -1;
    }

    protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        pressed = true;
        dragging = false;
        downScreen = Cursor.Position;
        downLocation = Origin;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != hot) { hot = h; Invalidate(); }
        if (!pressed) return;
        var c = Cursor.Position;
        if (!dragging && (Math.Abs(c.X - downScreen.X) > D(5) || Math.Abs(c.Y - downScreen.Y) > D(5))) dragging = true;
        if (dragging)
        {
            var s = Settings.Current;
            s.DockX = downLocation.X + (c.X - downScreen.X);
            s.DockY = downLocation.Y + (c.Y - downScreen.Y);
            Location = new Point(s.DockX, s.DockY);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!pressed) return;
        pressed = false;
        if (dragging) { dragging = false; Settings.Current.Save(); return; }
        switch (HitTest(e.Location))
        {
            case 0: Mic?.Invoke(); break;
            case 1: Translate?.Invoke(); break;
            case 2: Recent?.Invoke(); break;
        }
    }

    // ───────── drawing ─────────

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using (var rim = new Pen(Color.FromArgb(110, 255, 255, 255), D(1.5f)))
        using (var path = Capsule(new Rectangle(0, 0, Width - 1, Height - 1)))
            g.DrawPath(rim, path);

        var mic = MicRect;
        if (state == DockState.Recording) Fill(g, mic, Color.FromArgb(255, 59, 48), Color.FromArgb(255, 59, 48));
        else if (state == DockState.Busy) Fill(g, mic, Color.FromArgb(255, 159, 10), Color.FromArgb(255, 120, 10));
        else Fill(g, mic, Purple, Blue);
        if (hot == 0) Ring(g, mic);
        Glyph(g, state == DockState.Recording ? "■" : state == DockState.Busy ? "…" : "🎙", mic, 15f);

        int i = 0;
        if (HasTranslate)
        {
            var tr = SmallRect(i++);
            Fill(g, tr, Color.FromArgb(64, 64, 80), Color.FromArgb(64, 64, 80));
            if (hot == 1) Ring(g, tr);
            Glyph(g, "文A", tr, 10f, bold: true);
        }
        var rc = SmallRect(i);
        Fill(g, rc, Color.FromArgb(64, 64, 80), Color.FromArgb(64, 64, 80));
        if (hot == 2) Ring(g, rc);
        Glyph(g, "📋", rc, 12f);
    }

    static void Fill(Graphics g, Rectangle r, Color a, Color b)
    {
        using var br = new LinearGradientBrush(r, a, b, LinearGradientMode.ForwardDiagonal);
        g.FillEllipse(br, r);
    }

    void Ring(Graphics g, Rectangle r)
    {
        using var p = new Pen(Color.FromArgb(200, 255, 255, 255), D(2));
        g.DrawEllipse(p, r);
    }

    static void Glyph(Graphics g, string t, Rectangle r, float size, bool bold = false)
    {
        using var f = new Font(t == "文A" ? "Segoe UI" : "Segoe UI Emoji", size, bold ? FontStyle.Bold : FontStyle.Regular);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(t, f, Brushes.White, r, sf);
    }

    static GraphicsPath Capsule(Rectangle r)
    {
        var p = new GraphicsPath();
        int d = Math.Min(r.Width, r.Height);
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }
}

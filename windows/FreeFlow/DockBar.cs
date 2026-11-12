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
    bool expanded, hover, dragging, pressed;
    Point downScreen, downLocation;
    int pinnedUntilTick;
    int hot = -1;   // hovered button index
    readonly System.Windows.Forms.Timer collapseTimer = new() { Interval = 400 };

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
        collapseTimer.Tick += (_, _) => MaybeCollapse();
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
    int Step => D(Size1 + Gap);

    // ───────── placement ─────────

    /// Where the mic circle sits (top-left), in screen coordinates.
    Point MicOrigin
    {
        get
        {
            var s = Settings.Current;
            var wa = Screen.PrimaryScreen.WorkingArea;
            var p = s.DockX >= 0 ? new Point(s.DockX, s.DockY) : new Point(wa.Right - D(Size1) - D(24), wa.Bottom - D(Size1) - D(120));
            // keep it on some screen
            if (!Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(p, new Size(D(Size1), D(Size1))))))
                p = new Point(wa.Right - D(Size1) - D(24), wa.Bottom - D(Size1) - D(120));
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
        var mic = MicOrigin;
        int w = D(Size1) + D(8), h = expanded ? D(Size1) + 2 * Step + D(8) : D(Size1) + D(8);
        Bounds = new Rectangle(mic.X - D(4), mic.Y - (h - D(Size1) - D(4)), w, h);
        using var path = Capsule(new Rectangle(0, 0, w, h));
        Region = new Region(path);
        Invalidate();
    }

    public void SetState(DockState s) { state = s; Invalidate(); }

    /// Open up for a few seconds (after a dictation).
    public void ExpandFor(int ms)
    {
        if (!Visible) return;
        pinnedUntilTick = Environment.TickCount + ms;
        Expand();
    }

    void Expand()
    {
        if (!expanded) { expanded = true; Layout2(); }
        collapseTimer.Start();
    }

    void MaybeCollapse()
    {
        if (!expanded) { collapseTimer.Stop(); return; }
        bool cursorIn = Bounds.Contains(Cursor.Position);
        if (cursorIn || CardOpen() || Environment.TickCount < pinnedUntilTick || dragging) return;
        expanded = false;
        collapseTimer.Stop();
        Layout2();
    }

    // ───────── input ─────────

    /// 0 = 📋 recent, 1 = 文A translate, 2 = mic (bottom).
    int HitTest(Point p)
    {
        int micTop = Height - D(4) - D(Size1);
        if (p.Y >= micTop) return 2;
        if (!expanded) return -1;
        if (p.Y >= micTop - Step) return 1;
        if (p.Y >= micTop - 2 * Step) return 0;
        return -1;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Expand(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; hot = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        pressed = true;
        dragging = false;
        downScreen = Cursor.Position;
        downLocation = MicOrigin;
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
            Layout2();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!pressed) return;
        pressed = false;
        if (dragging) { dragging = false; Settings.Current.Save(); return; }
        switch (HitTest(e.Location))
        {
            case 2: Mic?.Invoke(); break;
            case 1: Translate?.Invoke(); break;
            case 0: Recent?.Invoke(); break;
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

        int x = D(4), micTop = Height - D(4) - D(Size1), sz = D(Size1);
        // mic
        var micRect = new Rectangle(x, micTop, sz, sz);
        if (state == DockState.Recording) Fill(g, micRect, Color.FromArgb(255, 59, 48), Color.FromArgb(255, 59, 48));
        else if (state == DockState.Busy) Fill(g, micRect, Color.FromArgb(255, 159, 10), Color.FromArgb(255, 120, 10));
        else Fill(g, micRect, Purple, Blue);
        if (hot == 2) Ring(g, micRect);
        Glyph(g, state == DockState.Recording ? "■" : state == DockState.Busy ? "…" : "🎙", micRect, 15f);

        if (!expanded) return;
        var tr = new Rectangle(x + D(4), micTop - Step + D(4), sz - D(8), sz - D(8));
        Fill(g, tr, Color.FromArgb(64, 64, 80), Color.FromArgb(64, 64, 80));
        if (hot == 1) Ring(g, tr);
        Glyph(g, "文A", tr, 10.5f, bold: true);

        var rc = new Rectangle(x + D(4), micTop - 2 * Step + D(4), sz - D(8), sz - D(8));
        Fill(g, rc, Color.FromArgb(64, 64, 80), Color.FromArgb(64, 64, 80));
        if (hot == 0) Ring(g, rc);
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
        p.AddArc(r.X, r.Y, d, d, 180, 180);
        p.AddArc(r.X, r.Bottom - d, d, d, 0, 180);
        p.CloseFigure();
        return p;
    }
}

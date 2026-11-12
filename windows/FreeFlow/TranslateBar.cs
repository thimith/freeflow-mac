using System.Drawing.Drawing2D;

namespace FreeFlow;

/// "文A Translate" button shown for 5 s after each dictation, and the language card it opens.
/// Clickable but never takes focus away from the app you're typing in.
sealed class TranslateBar : Form
{
    public Action<TLang> OnTranslate;
    public Action OnImprove;
    public Action<string> OnCopied;

    enum Mode { Compact, Menu, All, Recent }
    Mode mode;
    Rectangle? anchor;   // the floating bubble, when the card opens from it
    readonly System.Windows.Forms.Timer hideTimer = new();
    readonly System.Windows.Forms.Timer outsideTimer = new() { Interval = 120 };
    readonly FlowLayoutPanel body = new() { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent };

    static readonly Color CardBg = Color.FromArgb(28, 28, 36);
    static readonly Color TileBg = Color.FromArgb(48, 48, 60);
    static readonly Color Purple = Color.FromArgb(124, 77, 255);
    static readonly Color Blue = Color.FromArgb(40, 110, 255);
    static readonly Color Pink = Color.FromArgb(236, 72, 153);

    public TranslateBar()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = CardBg;
        DoubleBuffered = true;
        Font = new Font("Segoe UI", 10f);
        Controls.Add(body);
        hideTimer.Tick += (_, _) => HideBar();
        // close cards when you click somewhere else (but not on the bubble)
        outsideTimer.Tick += (_, _) =>
        {
            if (!Visible || mode == Mode.Compact) return;
            if ((Control.MouseButtons & (MouseButtons.Left | MouseButtons.Right)) == 0) return;
            var c = Cursor.Position;
            if (Bounds.Contains(c) || (anchor is { } a && Rectangle.Inflate(a, D(8), D(8)).Contains(c))) return;
            HideBar();
        };
    }

    public bool CardOpen => Visible && mode != Mode.Compact;

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

    public void ShowCompact()
    {
        mode = Mode.Compact;
        anchor = null;
        body.Controls.Clear();
        body.FlowDirection = FlowDirection.LeftToRight;
        var pill = new Label
        {
            Text = "文A  Translate", ForeColor = Color.White, BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(D(140), D(40)), Margin = Padding.Empty, Cursor = Cursors.Hand,
        };
        pill.Click += (_, _) => ShowMenu();
        var copy = new Label
        {
            Text = "📋", ForeColor = Color.White, BackColor = Color.Transparent,
            Font = new Font("Segoe UI Emoji", 12f), TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(D(46), D(40)), Margin = Padding.Empty, Cursor = Cursors.Hand,
        };
        copy.Click += (_, _) => ShowRecent();
        body.Controls.Add(pill);
        body.Controls.Add(copy);
        body.Padding = Padding.Empty;
        Place(new Size(D(186), D(40)), pill: true);
        StartHide(5000);
    }

    public void ShowMenuNear(Rectangle bubble) { anchor = bubble; ShowMenu(keepAnchor: true); }
    public void ShowRecentNear(Rectangle bubble) { anchor = bubble; ShowRecent(keepAnchor: true); }

    /// Your last 5 dictations — click one to copy it. Stays open until you close it.
    public void ShowRecent(bool keepAnchor = false)
    {
        if (!keepAnchor) anchor = null;
        mode = Mode.Recent;
        hideTimer.Stop();
        body.Controls.Clear();
        body.FlowDirection = FlowDirection.TopDown;
        body.Padding = new Padding(D(10));
        var head = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Margin = Padding.Empty, BackColor = Color.Transparent };
        var cap = Caption("Recent dictations — click to copy");
        cap.Width = D(240); cap.AutoSize = false; cap.Height = D(22);
        head.Controls.Add(cap);
        var close = new Label { Text = "✕", ForeColor = Color.FromArgb(170, 255, 255, 255), Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                                Size = new Size(D(26), D(22)), TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand, BackColor = Color.Transparent };
        close.Click += (_, _) => HideBar();
        head.Controls.Add(close);
        body.Controls.Add(head);
        var items = HistoryStore.Shared.Entries.Take(5).ToList();
        if (items.Count == 0) body.Controls.Add(Caption("Nothing dictated yet."));
        foreach (var e in items)
        {
            var text = e.Text;
            var preview = text.Replace("\r", " ").Replace("\n", " ⏎ ");
            Label tile = null;
            tile = Tile(preview, D(268), TileBg, () =>
            {
                Inserter.SetClipboard(text, false);
                OnCopied?.Invoke(text);
                tile.Text = "✓  Copied";
                var t = new System.Windows.Forms.Timer { Interval = 900 };
                t.Tick += (_, _) => { t.Stop(); t.Dispose(); if (!tile.IsDisposed) tile.Text = preview; };
                t.Start();
            });
            tile.Height = D(52);
            tile.Font = new Font("Segoe UI", 9.5f);
            tile.TextAlign = ContentAlignment.MiddleLeft;
            body.Controls.Add(tile);
        }
        body.PerformLayout();
        Place(new Size(D(292), body.PreferredSize.Height), pill: false);
        outsideTimer.Start();
    }

    public void ShowMenu() => ShowMenu(false);

    void ShowMenu(bool keepAnchor)
    {
        if (!keepAnchor) anchor = null;
        mode = Mode.Menu;
        body.FlowDirection = FlowDirection.TopDown;
        body.Controls.Clear();
        body.Padding = new Padding(D(10));
        body.Controls.Add(Caption("Translate to"));
        var grid = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Width = D(272), AutoSize = true, Margin = Padding.Empty };
        foreach (var l in TLang.Quick())
            grid.Controls.Add(Tile($"{l.Flag}  {l.Short}", D(132), TileBg, () => Pick(l)));
        body.Controls.Add(grid);
        body.Controls.Add(Tile("🌐  More languages", D(268), TileBg, ShowAll));
        body.Controls.Add(Tile("✨  Improve by voice", D(268), Pink, () => { HideBar(); OnImprove?.Invoke(); }));
        body.PerformLayout();
        Place(new Size(D(292), body.PreferredSize.Height), pill: false);
        StartHide(20000);
        outsideTimer.Start();
    }

    void ShowAll()
    {
        mode = Mode.All;
        body.FlowDirection = FlowDirection.TopDown;
        body.Controls.Clear();
        body.Padding = new Padding(D(10));
        body.Controls.Add(Caption("All languages"));
        var list = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Width = D(280), Height = D(360), Margin = Padding.Empty,
        };
        foreach (var l in TLang.All) list.Controls.Add(Tile($"{l.Flag}  {l.Label}", D(250), TileBg, () => Pick(l)));
        body.Controls.Add(list);
        body.PerformLayout();
        Place(new Size(D(300), body.PreferredSize.Height), pill: false);
        StartHide(30000);
        outsideTimer.Start();
    }

    void Pick(TLang l) { HideBar(); OnTranslate?.Invoke(l); }

    public void HideBar()
    {
        hideTimer.Stop();
        outsideTimer.Stop();
        Hide();
    }

    void StartHide(int ms)
    {
        hideTimer.Stop();
        hideTimer.Interval = ms;
        hideTimer.Start();
    }

    void Place(Size size, bool pill)
    {
        if (anchor is { } a && !pill)
        {
            // next to the floating bubble: to its left if there's room, else to its right; bottoms aligned
            var wa2 = Screen.FromRectangle(a).WorkingArea;
            int x = a.Left - size.Width - D(10);
            if (x < wa2.Left) x = a.Right + D(10);
            int y = Math.Clamp(a.Bottom - size.Height, wa2.Top, wa2.Bottom - size.Height);
            Bounds = new Rectangle(x, y, size.Width, size.Height);
        }
        else
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Bounds = new Rectangle(wa.Left + (wa.Width - size.Width) / 2, wa.Bottom - size.Height - D(28), size.Width, size.Height);
        }
        BackColor = pill ? Purple : CardBg;
        using var path = Rounded(new Rectangle(0, 0, size.Width, size.Height), pill ? size.Height / 2 : D(16));
        Region = new Region(path);
        if (!Visible) Show();
        TopMost = true;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (mode == Mode.Compact)
        {
            using var b = new LinearGradientBrush(ClientRectangle, Purple, Blue, LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(b, ClientRectangle);
        }
        else base.OnPaintBackground(e);
    }

    Label Caption(string t) => new()
    {
        Text = t, ForeColor = Color.FromArgb(150, 255, 255, 255), AutoSize = true,
        Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Margin = new Padding(D(2), 0, 0, D(6)), BackColor = Color.Transparent,
    };

    Label Tile(string text, int width, Color bg, Action onClick)
    {
        var t = new Label
        {
            Text = text, ForeColor = Color.White, BackColor = bg, AutoSize = false,
            Size = new Size(width, D(38)), TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(D(10), 0, 0, 0), Margin = new Padding(0, 0, D(4), D(4)),
            Font = new Font("Segoe UI Emoji", 10f), Cursor = Cursors.Hand, AutoEllipsis = true,
        };
        void Round() { using var p = Rounded(new Rectangle(0, 0, t.Width, t.Height), D(10)); t.Region = new Region(p); }
        Round();
        t.Resize += (_, _) => Round();
        t.MouseEnter += (_, _) => t.BackColor = ControlPaint.Light(bg, 0.25f);
        t.MouseLeave += (_, _) => t.BackColor = bg;
        t.Click += (_, _) => onClick();
        return t;
    }

    static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = Math.Max(1, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

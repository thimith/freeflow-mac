using System.Drawing.Drawing2D;

namespace FreeFlow;

/// "文A Translate" button shown for 5 s after each dictation, and the language card it opens.
/// Clickable but never takes focus away from the app you're typing in.
sealed class TranslateBar : Form
{
    public Action<TLang> OnTranslate;
    public Action OnImprove;

    enum Mode { Compact, Menu, All }
    Mode mode;
    readonly System.Windows.Forms.Timer hideTimer = new();
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

    public void ShowCompact()
    {
        mode = Mode.Compact;
        body.Controls.Clear();
        var pill = new Label
        {
            Text = "文A  Translate", ForeColor = Color.White, BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter,
            Size = new Size(D(150), D(40)), Margin = Padding.Empty, Cursor = Cursors.Hand,
        };
        pill.Click += (_, _) => ShowMenu();
        body.Controls.Add(pill);
        body.Padding = Padding.Empty;
        Place(new Size(D(150), D(40)), pill: true);
        StartHide(5000);
    }

    public void ShowMenu()
    {
        mode = Mode.Menu;
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
    }

    void ShowAll()
    {
        mode = Mode.All;
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
    }

    void Pick(TLang l) { HideBar(); OnTranslate?.Invoke(l); }

    public void HideBar()
    {
        hideTimer.Stop();
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
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Bounds = new Rectangle(wa.Left + (wa.Width - size.Width) / 2, wa.Bottom - size.Height - D(28), size.Width, size.Height);
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

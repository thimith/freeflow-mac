namespace FreeFlow;

/// The cards that open from the bubble: translate languages, all languages, and your last 5 dictations.
/// Custom-drawn with smooth edges; clicking never steals focus from the app you're typing in.
sealed class TranslateBar : LayeredWindow
{
    public Action<TLang> OnTranslate;
    public Action OnImprove;
    public Action<string> OnCopied;

    enum Mode { Menu, All, Recent }
    Mode mode;
    Func<Rectangle> anchorFn;
    Rectangle? anchor => anchorFn?.Invoke();
    float openT = 1;   // 0 → 1 open animation
    readonly System.Windows.Forms.Timer openTimer = new() { Interval = 16 };
    int hot = -1, scroll, copiedIndex = -1;

    sealed class Item
    {
        public RectangleF R;
        public string Title, Sub, Badge;
        public Color Bg;
        public bool Accent, Close;
        public Action Act;
        public bool Scrolls;
    }
    readonly List<Item> items = new();
    string caption = "";
    float listTop, listBottom, contentHeight;
    int totalHeight;

    readonly System.Windows.Forms.Timer hideTimer = new();
    readonly System.Windows.Forms.Timer outsideTimer = new() { Interval = 120 };
    readonly System.Windows.Forms.Timer copiedTimer = new() { Interval = 900 };

    static readonly Color CardBg = Color.FromArgb(250, 26, 26, 34);
    static readonly Color Rim = Color.FromArgb(150, 255, 255, 255);
    static readonly Color Tile = Color.FromArgb(46, 46, 58);
    static readonly Color TileHot = Color.FromArgb(70, 70, 88);
    static readonly Color Pink1 = Color.FromArgb(170, 110, 255), Pink2 = Color.FromArgb(236, 72, 153);

    public TranslateBar()
    {
        hideTimer.Tick += (_, _) => HideBar();
        openTimer.Tick += (_, _) =>
        {
            openT = Math.Min(1, openT + 0.07f);
            WindowAlpha = (byte)(255 * EaseOut(Math.Min(1, openT * 1.4f)));
            Redraw();
            if (openT >= 1) openTimer.Stop();
        };
        copiedTimer.Tick += (_, _) => { copiedTimer.Stop(); copiedIndex = -1; Redraw(); };
        outsideTimer.Tick += (_, _) =>
        {
            if (!Visible) return;
            if ((Control.MouseButtons & (MouseButtons.Left | MouseButtons.Right)) == 0) return;
            var c = Cursor.Position;
            if (Bounds.Contains(c) || (anchor is { } a && Rectangle.Inflate(a, D(8), D(8)).Contains(c))) return;
            HideBar();
        };
    }

    public bool CardOpen => Visible;

    public void ShowMenu() { anchorFn = null; Open(Mode.Menu); }
    public void ShowMenuNear(Func<Rectangle> bubble) { anchorFn = bubble; Open(Mode.Menu); }
    public void ShowRecentNear(Func<Rectangle> bubble) { anchorFn = bubble; Open(Mode.Recent); }

    public void HideBar()
    {
        openTimer.Stop();
        hideTimer.Stop();
        outsideTimer.Stop();
        Hide();
    }

    void Open(Mode m)
    {
        bool switching = Visible;
        mode = m;
        scroll = 0;
        hot = -1;
        copiedIndex = -1;
        openT = switching ? 0.55f : 0f;   // pop open (a smaller hop when switching cards)
        WindowAlpha = switching ? (byte)255 : (byte)0;
        Redraw();
        if (!Visible) Show();
        openTimer.Start();
        hideTimer.Stop();
        if (m == Mode.Menu) { hideTimer.Interval = 20000; hideTimer.Start(); }
        if (m == Mode.All) { hideTimer.Interval = 45000; hideTimer.Start(); }
        // Recent stays open until you close it
        outsideTimer.Start();
    }

    // ───────── layout ─────────

    int W => D(296);
    int P => D(12);
    int TileH => D(40);

    static string Code(TLang l)
    {
        var i = l.Id.IndexOf('-');
        var c = i > 0 ? l.Id[(i + 1)..] : l.Id;
        return c == "419" ? "LA" : c.ToUpperInvariant();
    }

    static string Region(TLang l)
    {
        int i = l.Label.IndexOf(" (");
        return i > 0 ? l.Label[(i + 2)..^1] : null;
    }

    void Build()
    {
        items.Clear();
        float y = P + D(26);
        float inner = W - 2 * P;
        switch (mode)
        {
            case Mode.Menu:
            {
                caption = "Translate to";
                var quick = TLang.Quick();
                float half = (inner - D(8)) / 2;
                for (int i = 0; i < quick.Count; i++)
                {
                    var l = quick[i];
                    float x = P + (i % 2) * (half + D(8));
                    if (i > 0 && i % 2 == 0) y += TileH + D(8);
                    items.Add(new Item { R = new RectangleF(x, y, half, TileH), Title = l.Short, Sub = Region(l), Badge = Code(l), Bg = Tile, Act = () => Pick(l) });
                }
                if (quick.Count > 0) y += TileH + D(8);
                items.Add(new Item { R = new RectangleF(P, y, inner, TileH), Title = "🌐  More languages", Bg = Tile, Act = () => Open(Mode.All) });
                y += TileH + D(8);
                items.Add(new Item { R = new RectangleF(P, y, inner, TileH), Title = "✨  Improve by voice", Accent = true, Act = () => { HideBar(); OnImprove?.Invoke(); } });
                y += TileH;
                break;
            }
            case Mode.All:
            {
                caption = "All languages";
                listTop = y;
                foreach (var l in TLang.All)
                {
                    var ll = l;
                    items.Add(new Item { R = new RectangleF(P, y, inner, TileH), Title = l.Short, Sub = Region(l), Badge = Code(l), Bg = Tile, Scrolls = true, Act = () => Pick(ll) });
                    y += TileH + D(6);
                }
                contentHeight = y - listTop;
                y = listTop + Math.Min(contentHeight, D(380));
                listBottom = y;
                break;
            }
            case Mode.Recent:
            {
                caption = "Recent dictations — click to copy";
                items.Add(new Item { R = new RectangleF(W - P - D(26), P - D(2), D(26), D(26)), Title = "✕", Close = true, Act = HideBar });
                var list = HistoryStore.Shared.Entries.Take(5).ToList();
                if (list.Count == 0)
                {
                    items.Add(new Item { R = new RectangleF(P, y, inner, TileH), Title = "Nothing dictated yet", Bg = Tile });
                    y += TileH;
                }
                for (int i = 0; i < list.Count; i++)
                {
                    var text = list[i].Text;
                    int idx = items.Count;
                    var age = DateTime.Now - list[i].Date;
                    string when = age.TotalMinutes < 1 ? "just now" : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago"
                                : age.TotalDays < 1 ? $"{(int)age.TotalHours} h ago" : list[i].Date.ToString("d MMM");
                    items.Add(new Item
                    {
                        R = new RectangleF(P, y, inner, D(58)), Title = text.Replace("\r", " ").Replace("\n", " ⏎ "),
                        Sub = (string.IsNullOrEmpty(list[i].App) ? "" : list[i].App + " · ") + when, Bg = Tile,
                        Act = () =>
                        {
                            Inserter.SetClipboard(text, false);
                            OnCopied?.Invoke(text);
                            copiedIndex = idx;
                            copiedTimer.Stop(); copiedTimer.Start();
                            Redraw();
                        },
                    });
                    y += D(58) + D(6);
                }
                break;
            }
        }
        totalHeight = (int)Math.Ceiling(y + P);
    }

    Rectangle TargetBounds()
    {
        Build();
        var size = new Size(W, totalHeight);
        Rectangle wa;
        int x, y;
        if (anchor is { } a)
        {
            // centred directly above the bubble (below it only if there's no room above)
            wa = Screen.FromRectangle(a).WorkingArea;
            x = Math.Clamp(a.Left + a.Width / 2 - size.Width / 2, wa.Left, wa.Right - size.Width);
            y = a.Top - size.Height - D(12);
            if (y < wa.Top) y = Math.Min(a.Bottom + D(12), wa.Bottom - size.Height);
        }
        else
        {
            wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            x = wa.Left + (wa.Width - size.Width) / 2;
            y = wa.Bottom - size.Height - D(96);
        }
        return new Rectangle(x, y, size.Width, size.Height);
    }

    void Redraw() => Present(TargetBounds());

    // ───────── drawing ─────────

    protected override void Render(Graphics g, Size size)
    {
        if (openT < 1)
        {
            // ease up out of the bubble: grow from the bottom-centre, no overshoot
            float e = EaseOut(openT);
            float sc = 0.94f + 0.06f * e;
            bool above = anchor is { } a2 && Bounds.Bottom <= a2.Top + D(4);
            float oy = above ? size.Height : 0;
            g.TranslateTransform(size.Width / 2f, oy);
            g.ScaleTransform(sc, sc);
            g.TranslateTransform(-size.Width / 2f, -oy + (1 - e) * D(10) * (above ? 1 : -1));
        }
        var card = new RectangleF(D(1), D(1), size.Width - D(2) - 1, size.Height - D(2) - 1);
        using (var path = RoundRect(card, D(16)))
        {
            using var b = new SolidBrush(CardBg);
            g.FillPath(b, path);
            using var pen = new Pen(Rim, Math.Max(1.6f, 2f * S));
            g.DrawPath(pen, path);
        }
        using (var f = new Font("Segoe UI Semibold", 8.75f))
            Text(g, caption, new RectangleF(P + D(2), P - D(2), W - 2 * P - D(30), D(22)), f, Color.FromArgb(160, 255, 255, 255), StringAlignment.Near);

        var state = g.Save();
        if (mode == Mode.All) g.SetClip(new RectangleF(0, listTop - D(2), size.Width, listBottom - listTop + D(4)));
        for (int i = 0; i < items.Count; i++) DrawItem(g, items[i], i);
        g.Restore(state);

        if (mode == Mode.All && contentHeight > listBottom - listTop)
        {
            float track = listBottom - listTop;
            float thumbH = Math.Max(D(24), track * track / contentHeight);
            float maxScroll = contentHeight - track;
            float thumbY = listTop + (track - thumbH) * (maxScroll <= 0 ? 0 : scroll / maxScroll);
            using var tb = new SolidBrush(Color.FromArgb(90, 255, 255, 255));
            using var tp = RoundRect(new RectangleF(size.Width - D(7), thumbY, D(3), thumbH), D(1.5f));
            g.FillPath(tb, tp);
        }
    }

    RectangleF Shown(Item it) => it.Scrolls ? new RectangleF(it.R.X, it.R.Y - scroll, it.R.Width, it.R.Height) : it.R;

    void DrawItem(Graphics g, Item it, int index)
    {
        var r = Shown(it);
        if (it.Close)
        {
            CrossIcon(g, r, hot == index ? Color.White : Color.FromArgb(160, 255, 255, 255), 0.18f);
            return;
        }
        using (var path = RoundRect(r, D(10)))
        {
            if (it.Accent)
            {
                using var gb = new System.Drawing.Drawing2D.LinearGradientBrush(r, Pink1, Pink2, System.Drawing.Drawing2D.LinearGradientMode.Horizontal);
                g.FillPath(gb, path);
                if (hot == index) { using var hl = new SolidBrush(Color.FromArgb(40, 255, 255, 255)); g.FillPath(hl, path); }
            }
            else
            {
                using var b = new SolidBrush(hot == index && it.Act != null ? TileHot : it.Bg);
                g.FillPath(b, path);
            }
        }

        float tx = r.X + D(12);
        if (it.Badge != null)
        {
            var br = new RectangleF(r.X + D(8), r.Y + (r.Height - D(26)) / 2, D(26), D(26));
            using (var bb = new SolidBrush(Color.FromArgb(124, 77, 255))) g.FillEllipse(bb, br);
            using (var bf = new Font("Segoe UI", it.Badge.Length > 2 ? 6.5f : 7.5f, FontStyle.Bold)) Text(g, it.Badge, br, bf, Color.White);
            tx = br.Right + D(8);
        }

        if (copiedIndex == index)
        {
            using var f = new Font("Segoe UI Semibold", 10f);
            Text(g, "✓  Copied", new RectangleF(tx, r.Y, r.Right - tx - D(8), r.Height), f, Color.FromArgb(120, 230, 150), StringAlignment.Near);
            return;
        }

        if (mode == Mode.Recent && it.Sub != null)
        {
            using var f = new Font("Segoe UI", 9.5f);
            using var sf = new Font("Segoe UI", 8f);
            Text(g, it.Title, new RectangleF(tx, r.Y + D(5), r.Right - tx - D(10), D(32)), f, Color.White, StringAlignment.Near, wrap: true);
            Text(g, it.Sub, new RectangleF(tx, r.Bottom - D(20), r.Right - tx - D(10), D(16)), sf, Color.FromArgb(140, 255, 255, 255), StringAlignment.Near);
        }
        else if (it.Sub != null)
        {
            using var f = new Font("Segoe UI Semibold", 9.75f);
            using var sf = new Font("Segoe UI", 7.75f);
            Text(g, it.Title, new RectangleF(tx, r.Y + D(3), r.Right - tx - D(6), D(20)), f, Color.White, StringAlignment.Near);
            Text(g, it.Sub, new RectangleF(tx, r.Y + D(20), r.Right - tx - D(6), D(16)), sf, Color.FromArgb(150, 255, 255, 255), StringAlignment.Near);
        }
        else
        {
            string title = it.Title;
            if (title.StartsWith("🌐") || title.StartsWith("✨"))
            {
                var icon = new RectangleF(tx, r.Y + (r.Height - D(18)) / 2, D(18), D(18));
                if (title.StartsWith("🌐")) GlobeIcon(g, icon, Color.White); else SparkleIcon(g, icon, Color.White);
                title = title.Substring(2).TrimStart();
                tx += D(26);
            }
            using var f = new Font("Segoe UI Semibold", 9.75f);
            Text(g, title, new RectangleF(tx, r.Y, r.Right - tx - D(6), r.Height), f, Color.White, StringAlignment.Near);
        }
    }

    // ───────── input ─────────

    int HitTest(Point p)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var it = items[i];
            if (it.Act == null) continue;
            if (it.Scrolls && (p.Y < listTop || p.Y > listBottom)) continue;
            if (Shown(it).Contains(p)) return i;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int h = HitTest(e.Location);
        if (h != hot) { hot = h; Redraw(); }
    }

    protected override void OnMouseLeave(EventArgs e) { if (hot != -1) { hot = -1; Redraw(); } }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int h = HitTest(e.Location);
        if (h >= 0) items[h].Act?.Invoke();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (mode != Mode.All) return;
        float max = Math.Max(0, contentHeight - (listBottom - listTop));
        scroll = (int)Math.Clamp(scroll - e.Delta / 120f * D(46), 0, max);
        hot = HitTest(PointToClient(Cursor.Position));
        Redraw();
        hideTimer.Stop(); hideTimer.Start();
    }

    void Pick(TLang l) { HideBar(); OnTranslate?.Invoke(l); }
}

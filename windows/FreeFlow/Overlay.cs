using System.Drawing.Drawing2D;

namespace FreeFlow;

public enum Phase { Listening, HandsFree, Command, Transcribing, Polishing, Thinking, Done, Message }

/// The floating pill at the bottom of the screen. Never takes focus, click-through.
sealed class Overlay : Form
{
    Phase phase;
    string text = "";
    readonly float[] levels = new float[26];
    DateTime start = DateTime.Now;
    int spin;
    readonly System.Windows.Forms.Timer anim = new() { Interval = 33 };
    readonly System.Windows.Forms.Timer hideTimer = new();

    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(22, 22, 24);
        Opacity = 0.95;
        DoubleBuffered = true;
        Size = new Size(10, 10);
        anim.Tick += (_, _) => { spin++; Invalidate(); };
        hideTimer.Tick += (_, _) => { hideTimer.Stop(); HidePill(); };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/ | 0x20 /*TRANSPARENT*/ | 0x8 /*TOPMOST*/;
            return cp;
        }
    }

    float Scale => DeviceDpi / 96f;
    bool Recording => phase is Phase.Listening or Phase.HandsFree or Phase.Command;

    public void PushLevel(float l)
    {
        lock (levels)
        {
            Array.Copy(levels, 1, levels, 0, levels.Length - 1);
            levels[^1] = l;
        }
    }

    public void ShowPhase(Phase p)
    {
        lock (levels) Array.Clear(levels);
        start = DateTime.Now;
        Set(p);
    }

    public void Flash(string message) => Set(Phase.Message, message);

    public void Set(Phase p, string msg = "")
    {
        hideTimer.Stop();
        phase = p;
        text = msg ?? "";
        if (!Settings.Current.ShowOverlay) return;
        Layout2();
        if (!Visible) Show();
        TopMost = true;
        anim.Start();
        if (p == Phase.Done) { hideTimer.Interval = 1100; hideTimer.Start(); }
        if (p == Phase.Message) { hideTimer.Interval = 2600; hideTimer.Start(); }
        Invalidate();
    }

    public void HidePill()
    {
        hideTimer.Stop();
        anim.Stop();
        Hide();
    }

    string Label => phase switch
    {
        Phase.Transcribing => "Transcribing…",
        Phase.Polishing => "Polishing…",
        Phase.Thinking => "Thinking…",
        Phase.Done => string.IsNullOrEmpty(text) ? "Inserted" : $"Inserted · {text}",
        Phase.Message => text,
        _ => "",
    };

    void Layout2()
    {
        float s = Scale;
        int h = (int)(40 * s);
        int w;
        if (Recording) w = (int)(250 * s);
        else
        {
            using var g = CreateGraphics();
            using var f = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            w = (int)(g.MeasureString(Label, f).Width + 64 * s);
            w = Math.Clamp(w, (int)(140 * s), (int)(560 * s));
        }
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Bounds = new Rectangle(wa.Left + (wa.Width - w) / 2, wa.Bottom - h - (int)(28 * s), w, h);
        using var path = Pill(new RectangleF(0, 0, w, h));
        Region = new Region(path);
    }

    static GraphicsPath Pill(RectangleF r)
    {
        var p = new GraphicsPath();
        float d = r.Height;
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        float s = Scale;
        float cy = Height / 2f;
        float x = 16 * s;

        // leading icon
        switch (phase)
        {
            case Phase.Listening: Dot(g, Color.FromArgb(255, 69, 58), x, cy, 4.5f * s); break;
            case Phase.HandsFree: Dot(g, Color.FromArgb(255, 159, 10), x, cy, 4.5f * s); break;
            case Phase.Command: Dot(g, Color.FromArgb(191, 153, 255), x, cy, 4.5f * s); break;
            case Phase.Transcribing:
            case Phase.Polishing:
            case Phase.Thinking:
                using (var pen = new Pen(Color.White, 2 * s))
                    g.DrawArc(pen, x - 6 * s, cy - 6 * s, 12 * s, 12 * s, (spin * 24) % 360, 270);
                break;
            case Phase.Done:
                Dot(g, Color.FromArgb(48, 209, 88), x, cy, 7 * s);
                using (var pen = new Pen(Color.FromArgb(22, 22, 24), 1.8f * s))
                    g.DrawLines(pen, new[] { new PointF(x - 3.5f * s, cy), new PointF(x - 1 * s, cy + 2.5f * s), new PointF(x + 3.5f * s, cy - 2.5f * s) });
                break;
            case Phase.Message:
                Dot(g, Color.FromArgb(255, 214, 10), x, cy, 7 * s);
                using (var f = new Font("Segoe UI", 8f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.FromArgb(22, 22, 24)))
                {
                    var sz = g.MeasureString("!", f);
                    g.DrawString("!", f, b, x - sz.Width / 2, cy - sz.Height / 2);
                }
                break;
        }

        float tx = x + 14 * s;
        if (Recording)
        {
            float[] snap;
            lock (levels) snap = (float[])levels.Clone();
            float bw = 3 * s, gap = 2.2f * s;
            using var b = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
            for (int i = 0; i < snap.Length; i++)
            {
                float bh = Math.Max(3 * s, Math.Min(24 * s, snap[i] * 28 * s));
                var r = new RectangleF(tx + i * (bw + gap), cy - bh / 2, bw, bh);
                g.FillRectangle(b, r);
            }
            var secs = (int)(DateTime.Now - start).TotalSeconds;
            using var f = new Font("Consolas", 9f);
            using var tb = new SolidBrush(Color.FromArgb(170, 255, 255, 255));
            var label = $"{secs / 60}:{secs % 60:00}";
            var sz = g.MeasureString(label, f);
            g.DrawString(label, f, tb, Width - sz.Width - 14 * s, cy - sz.Height / 2);
        }
        else
        {
            using var f = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            var sz = g.MeasureString(Label, f);
            g.DrawString(Label, f, Brushes.White, tx, cy - sz.Height / 2);
        }
    }

    static void Dot(Graphics g, Color c, float x, float y, float r)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, x - r, y - r, r * 2, r * 2);
    }
}

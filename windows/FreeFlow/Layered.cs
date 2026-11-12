using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FreeFlow;

/// A borderless, always-on-top window drawn with per-pixel transparency (smooth, anti-aliased edges),
/// clickable but never taking focus from the app you're typing in.
abstract class LayeredWindow : Form
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int W, H; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION { public byte Op, Flags, Alpha, Format; }

    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);

    protected LayeredWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 /*LAYERED*/ | 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/ | 0x8 /*TOPMOST*/;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
        base.WndProc(ref m);
    }

    /// Whole-window opacity (for fade-in animations).
    protected byte WindowAlpha = 255;

    // ───────── animation helpers ─────────

    /// Critically-underdamped spring: returns the new value, updates velocity. Gives a light, bouncy overshoot.
    protected static float Spring(float value, ref float velocity, float target, float stiffness = 0.22f, float damping = 0.68f)
    {
        velocity = (velocity + (target - value) * stiffness) * damping;
        return value + velocity;
    }

    protected static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f, c3 = c1 + 1;
        return 1 + c3 * MathF.Pow(t - 1, 3) + c1 * MathF.Pow(t - 1, 2);
    }

    protected float S => DeviceDpi / 96f;
    protected int D(float v) => (int)Math.Round(v * S);

    /// Draw the window content (transparent background) at the given size.
    protected abstract void Render(Graphics g, Size size);

    /// Move/resize and repaint in one go.
    protected void Present(Rectangle bounds)
    {
        if (bounds.Width < 1 || bounds.Height < 1) return;
        if (!IsHandleCreated) CreateHandle();
        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Color.Transparent);
            Render(g, bounds.Size);
        }
        IntPtr screen = GetDC(IntPtr.Zero), mem = CreateCompatibleDC(screen), hbmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hbmp = bmp.GetHbitmap(Color.FromArgb(0));
            old = SelectObject(mem, hbmp);
            var size = new SIZE { W = bounds.Width, H = bounds.Height };
            var src = new POINT();
            var dst = new POINT { X = bounds.X, Y = bounds.Y };
            var blend = new BLENDFUNCTION { Op = 0, Flags = 0, Alpha = WindowAlpha, Format = 1 /*AC_SRC_ALPHA*/ };
            UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, 2 /*ULW_ALPHA*/);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screen);
            if (hbmp != IntPtr.Zero) { SelectObject(mem, old); DeleteObject(hbmp); }
            DeleteDC(mem);
        }
        if (Bounds != bounds) SetBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    // ───────── drawing helpers ─────────

    protected static GraphicsPath Capsule(RectangleF r)
    {
        var p = new GraphicsPath();
        float d = Math.Min(r.Width, r.Height);
        p.AddArc(r.X, r.Y, d, d, 90, 180);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180);
        p.CloseFigure();
        return p;
    }

    protected static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Max(1, radius * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected static void Circle(Graphics g, RectangleF r, Color a, Color b)
    {
        using var br = new LinearGradientBrush(r, a, b, LinearGradientMode.ForwardDiagonal);
        g.FillEllipse(br, r);
    }

    // ───────── vector icons (smooth at any size, unlike emoji glyphs) ─────────

    static Pen IconPen(Color c, float w) => new(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    protected static void MicIcon(Graphics g, RectangleF r, Color c)
    {
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, u = r.Width;
        using var b = new SolidBrush(c);
        using var body = RoundRect(new RectangleF(cx - u * 0.11f, cy - u * 0.27f, u * 0.22f, u * 0.34f), u * 0.11f);
        g.FillPath(b, body);
        using var p = IconPen(c, Math.Max(1.6f, u * 0.055f));
        g.DrawArc(p, cx - u * 0.19f, cy - u * 0.23f, u * 0.38f, u * 0.38f, 15, 150);
        g.DrawLine(p, cx, cy + u * 0.15f, cx, cy + u * 0.25f);
        g.DrawLine(p, cx - u * 0.10f, cy + u * 0.25f, cx + u * 0.10f, cy + u * 0.25f);
    }

    protected static void ClipboardIcon(Graphics g, RectangleF r, Color c)
    {
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, u = r.Width;
        using var p = IconPen(c, Math.Max(1.5f, u * 0.055f));
        using (var board = RoundRect(new RectangleF(cx - u * 0.19f, cy - u * 0.20f, u * 0.38f, u * 0.46f), u * 0.06f))
            g.DrawPath(p, board);
        using (var b = new SolidBrush(c))
        using (var clip = RoundRect(new RectangleF(cx - u * 0.10f, cy - u * 0.27f, u * 0.20f, u * 0.11f), u * 0.035f))
            g.FillPath(b, clip);
        g.DrawLine(p, cx - u * 0.09f, cy - u * 0.02f, cx + u * 0.09f, cy - u * 0.02f);
        g.DrawLine(p, cx - u * 0.09f, cy + u * 0.09f, cx + u * 0.05f, cy + u * 0.09f);
    }

    protected static void CrossIcon(Graphics g, RectangleF r, Color c, float scale = 0.22f)
    {
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = r.Width * scale;
        using var p = IconPen(c, Math.Max(1.6f, r.Width * 0.06f));
        g.DrawLine(p, cx - s, cy - s, cx + s, cy + s);
        g.DrawLine(p, cx - s, cy + s, cx + s, cy - s);
    }

    protected static void GlobeIcon(Graphics g, RectangleF r, Color c)
    {
        float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, rad = r.Width / 2 * 0.86f;
        using var p = IconPen(c, Math.Max(1.3f, r.Width * 0.07f));
        g.DrawEllipse(p, cx - rad, cy - rad, rad * 2, rad * 2);
        g.DrawEllipse(p, cx - rad * 0.45f, cy - rad, rad * 0.9f, rad * 2);
        g.DrawLine(p, cx - rad, cy, cx + rad, cy);
    }

    protected static void SparkleIcon(Graphics g, RectangleF r, Color c)
    {
        using var b = new SolidBrush(c);
        void Star(float x, float y, float s)
        {
            using var p = new GraphicsPath();
            p.AddBezier(x, y - s, x, y, x, y, x + s, y);
            p.AddBezier(x + s, y, x, y, x, y, x, y + s);
            p.AddBezier(x, y + s, x, y, x, y, x - s, y);
            p.AddBezier(x - s, y, x, y, x, y, x, y - s);
            p.CloseFigure();
            g.FillPath(b, p);
        }
        float u = r.Width;
        Star(r.X + u * 0.42f, r.Y + u * 0.56f, u * 0.36f);
        Star(r.X + u * 0.80f, r.Y + u * 0.22f, u * 0.18f);
    }

    protected static void Text(Graphics g, string t, RectangleF r, Font f, Color c, StringAlignment h = StringAlignment.Center, bool wrap = false)
    {
        using var sf = new StringFormat
        {
            Alignment = h, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = wrap ? 0 : StringFormatFlags.NoWrap,
        };
        using var b = new SolidBrush(c);
        g.DrawString(t, f, b, r, sf);
    }
}

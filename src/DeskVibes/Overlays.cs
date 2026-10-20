using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace DeskVibes;

/// <summary>A see-through window drawn from a bitmap with per-pixel alpha. Clicks pass through unless told otherwise.</summary>
class LayeredForm : Form
{
    readonly bool clickThrough;
    public LayeredForm(bool clickThrough = true)
    {
        this.clickThrough = clickThrough;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            if (clickThrough) cp.ExStyle |= Native.WS_EX_TRANSPARENT;
            return cp;
        }
    }

    public void SetBitmap(Bitmap bmp, byte alpha)
    {
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        IntPtr memDc = Native.CreateCompatibleDC(screenDc);
        IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            hBmp = bmp.GetHbitmap(Color.FromArgb(0));
            old = Native.SelectObject(memDc, hBmp);
            var size = new Native.SIZE(bmp.Width, bmp.Height);
            var src = new Native.POINT(0, 0);
            var dst = new Native.POINT(Left, Top);
            var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
            Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
        }
        finally
        {
            Native.ReleaseDC(IntPtr.Zero, screenDc);
            if (hBmp != IntPtr.Zero) { Native.SelectObject(memDc, old); Native.DeleteObject(hBmp); }
            Native.DeleteDC(memDc);
        }
    }

    /// <summary>Changes only the overall see-through amount (cheap, for pulsing).</summary>
    public void SetAlpha(byte alpha)
    {
        var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 };
        Native.UpdateLayeredAlpha(Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, 2);
    }
}

/// <summary>Screen-edge glow, screen flash, dimming and banners.</summary>
public static class Overlays
{
    static readonly List<LayeredForm> edges = new();
    static System.Windows.Forms.Timer? edgeTimer;

    public static bool EdgesShowing => edges.Count > 0;

    /// <summary>Lights up the edges of every screen. seconds = 0 keeps it until the mouse or keyboard is used.</summary>
    public static void Edges(Color color, string style, int size, double seconds)
    {
        ClearEdges();
        size = Math.Clamp(size, 4, 200);
        foreach (var scr in Screen.AllScreens)
        {
            var f = new LayeredForm { Bounds = scr.Bounds };
            f.Show();
            using var bmp = EdgeBitmap(scr.Bounds.Size, color, size);
            f.SetBitmap(bmp, 0);
            edges.Add(f);
        }
        var started = DateTime.Now;
        uint input = Native.LastInputTick();
        bool ending = false; DateTime endAt = DateTime.MaxValue;
        edgeTimer = new System.Windows.Forms.Timer { Interval = 33 };
        edgeTimer.Tick += (_, _) =>
        {
            var t = (DateTime.Now - started).TotalSeconds;
            if (!ending)
            {
                bool done = seconds > 0 ? t >= seconds
                    : t > 1.5 && Native.LastInputTick() != input || t > 3600;
                if (t <= 1.5) input = Native.LastInputTick();
                if (done) { ending = true; endAt = DateTime.Now; }
            }
            double fadeIn = Math.Min(1, t / 0.25);
            double fadeOut = ending ? 1 - (DateTime.Now - endAt).TotalSeconds / 0.6 : 1;
            if (fadeOut <= 0) { ClearEdges(); return; }
            double level = style switch
            {
                "Blink" => (int)(t / 0.45) % 2 == 0 ? 1 : 0.08,
                "Solid" => 1,
                _ => 0.55 + 0.45 * Math.Sin(t * Math.PI * 1.2),
            };
            byte a = (byte)Math.Clamp(255 * level * fadeIn * fadeOut, 0, 255);
            foreach (var f in edges) f.SetAlpha(a);
        };
        edgeTimer.Start();
    }

    public static void ClearEdges()
    {
        edgeTimer?.Stop(); edgeTimer?.Dispose(); edgeTimer = null;
        foreach (var f in edges) f.Close();
        edges.Clear();
    }

    static Bitmap EdgeBitmap(Size s, Color c, int size)
    {
        var bmp = new Bitmap(s.Width, s.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        for (int i = 0; i < size; i++)
        {
            double k = 1 - (double)i / size;
            int a = (int)(235 * Math.Pow(k, 1.8));
            if (a <= 0) continue;
            using var pen = new Pen(Color.FromArgb(a, c), 1);
            g.DrawRectangle(pen, i, i, s.Width - 1 - 2 * i, s.Height - 1 - 2 * i);
        }
        // a thin bright line right at the edge
        using var bright = new Pen(Color.FromArgb(255, Mix(c, Color.White, 0.35)), 2);
        g.DrawRectangle(bright, 1, 1, s.Width - 3, s.Height - 3);
        return bmp;
    }

    public static Color Mix(Color a, Color b, double t) =>
        Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

    public static bool IsLight(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000 > 150;

    /// <summary>Flashes every screen a few times.</summary>
    public static void Flash(Color color, int times)
    {
        times = Math.Clamp(times, 1, 10);
        var forms = new List<LayeredForm>();
        foreach (var scr in Screen.AllScreens)
        {
            var f = new LayeredForm { Bounds = scr.Bounds };
            f.Show();
            using var bmp = new Bitmap(scr.Bounds.Width, scr.Bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp)) g.Clear(color);
            f.SetBitmap(bmp, 0);
            forms.Add(f);
        }
        var start = DateTime.Now;
        var timer = new System.Windows.Forms.Timer { Interval = 30 };
        timer.Tick += (_, _) =>
        {
            double t = (DateTime.Now - start).TotalSeconds;
            const double each = 0.45;
            if (t >= times * each) { timer.Stop(); timer.Dispose(); foreach (var f in forms) f.Close(); return; }
            double p = t % each / each;
            double a = p < 0.3 ? p / 0.3 : 1 - (p - 0.3) / 0.7;
            foreach (var f in forms) f.SetAlpha((byte)(150 * a));
        };
        timer.Start();
    }

    static readonly List<Form> dims = new();

    /// <summary>Darkens all screens with a message until clicked (or for some seconds).</summary>
    public static void Dim(string text, int percent, double seconds)
    {
        foreach (var d in dims.ToList()) d.Close();
        dims.Clear();
        double target = Math.Clamp(percent, 10, 95) / 100.0;
        foreach (var scr in Screen.AllScreens)
        {
            var f = new DimForm(scr.Primary ? text : "") { Bounds = scr.Bounds };
            f.FormClosed += (_, _) => { foreach (var d in dims.ToList()) if (d != f && !d.IsDisposed) d.Close(); dims.Clear(); };
            dims.Add(f);
            f.Show();
            f.FadeTo(target);
        }
        if (seconds > 0)
        {
            var t = new System.Windows.Forms.Timer { Interval = (int)(seconds * 1000) };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); foreach (var d in dims.ToList()) d.Close(); dims.Clear(); };
            t.Start();
        }
    }

    sealed class DimForm : Form
    {
        readonly string text;
        public DimForm(string text)
        {
            this.text = text;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = true; BackColor = Color.Black; Opacity = 0; Cursor = Cursors.Hand; DoubleBuffered = true;
            Click += (_, _) => Close();
            KeyPreview = true; KeyDown += (_, _) => Close();
        }
        public void FadeTo(double target)
        {
            var t = new System.Windows.Forms.Timer { Interval = 30 };
            t.Tick += (_, _) => { if (IsDisposed) { t.Dispose(); return; } Opacity = Math.Min(target, Opacity + 0.05); if (Opacity >= target) { t.Stop(); t.Dispose(); } };
            t.Start();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (text.Length == 0) return;
            var g = e.Graphics; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var big = new Font("Segoe UI Light", 40f);
            using var small = new Font("Segoe UI", 13f);
            var r = ClientRectangle;
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, big, Brushes.White, new RectangleF(r.X, r.Y - 30, r.Width, r.Height), sf);
            using var gray = new SolidBrush(Color.FromArgb(200, 200, 200));
            g.DrawString("Click anywhere to continue", small, gray, new RectangleF(r.X, r.Y + 60, r.Width, r.Height), sf);
        }
    }

    static Form? banner;

    /// <summary>Big text across the top of the main screen.</summary>
    public static void Banner(string text, Color color, double seconds)
    {
        banner?.Close();
        var area = Screen.PrimaryScreen!.WorkingArea;
        var f = new BannerForm(text, color) { Bounds = new Rectangle(area.Left, area.Top, area.Width, 96) };
        banner = f;
        f.FormClosed += (_, _) => { if (banner == f) banner = null; };
        f.Show();
        var t = new System.Windows.Forms.Timer { Interval = (int)(Math.Max(1, seconds) * 1000) };
        t.Tick += (_, _) => { t.Stop(); t.Dispose(); if (!f.IsDisposed) f.Close(); };
        t.Start();
    }

    sealed class BannerForm : Form
    {
        readonly string text; readonly Color color;
        public BannerForm(string text, Color color)
        {
            this.text = text; this.color = color;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = true; BackColor = color; Opacity = 0.96; Cursor = Cursors.Hand; DoubleBuffered = true;
            Click += (_, _) => Close();
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE; return cp; } }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Segoe UI Semibold", 28f);
            using var brush = new SolidBrush(IsLight(color) ? Color.FromArgb(20, 20, 20) : Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            g.DrawString(text, font, brush, ClientRectangle, sf);
        }
    }

    public static void CloseAll()
    {
        ClearEdges();
        foreach (var d in dims.ToList()) d.Close();
        dims.Clear();
        banner?.Close();
        Cards.CloseAll();
    }
}

/// <summary>Little pop-up cards in a corner of the screen. They stack, and close when clicked.</summary>
public static class Cards
{
    static readonly List<CardForm> open = new();

    public static void Show(string title, string text, Color color, double seconds, string where, string link)
    {
        var f = new CardForm(title, text, color, link);
        f.Tag = where;
        f.FormClosed += (_, _) => { open.Remove(f); Layout(); };
        open.Add(f);
        while (open.Count > 6) open[0].Close();
        Layout();
        f.Show();
        f.FadeIn();
        if (seconds > 0) f.CloseAfter(seconds);
    }

    public static void CloseAll() { foreach (var f in open.ToList()) f.Close(); }

    static void Layout()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        int gap = 10;
        foreach (var group in open.GroupBy(f => f.Tag as string ?? ""))
        {
            int y = group.Key == "Bottom right" ? area.Bottom - gap : area.Top + gap;
            if (group.Key == "Middle") y = area.Top + area.Height / 3;
            foreach (var f in group.Reverse())
            {
                int x = group.Key == "Middle" ? area.Left + (area.Width - f.Width) / 2 : area.Right - f.Width - gap;
                if (group.Key == "Bottom right") { y -= f.Height; f.Location = new Point(x, y); y -= gap; }
                else { f.Location = new Point(x, y); y += f.Height + gap; }
            }
        }
    }

    sealed class CardForm : Form
    {
        readonly string title, text, link; readonly Color color;
        bool hover; DateTime closeAt = DateTime.MaxValue;
        readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };

        public CardForm(string title, string text, Color color, string link)
        {
            this.title = title; this.text = text; this.color = color; this.link = link;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            TopMost = true; BackColor = Theme.Card; DoubleBuffered = true; Opacity = 0; Cursor = Cursors.Hand;
            int w = (int)(340 * DeviceDpi / 96.0);
            using var g = CreateGraphics();
            int textH = text.Length == 0 ? 0 : (int)g.MeasureString(text, Theme.Body, w - S(40)).Height;
            Size = new Size(w, S(20) + (int)g.MeasureString(title, Theme.Title, w - S(40)).Height + (textH > 0 ? textH + S(6) : 0) + S(16));
            Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, Width + 1, Height + 1, S(14), S(14)));
            MouseEnter += (_, _) => hover = true;
            MouseLeave += (_, _) => hover = false;
            Click += (_, _) =>
            {
                if (link.Trim().Length > 0) Actions.OpenThing(link);
                Close();
            };
            timer.Tick += (_, _) =>
            {
                if (hover) closeAt = closeAt == DateTime.MaxValue ? closeAt : closeAt.AddMilliseconds(100);
                if (DateTime.Now >= closeAt) Close();
            };
            timer.Start();
        }

        int S(int px) => (int)(px * DeviceDpi / 96.0);

        [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE; return cp; } }

        public void FadeIn()
        {
            var t = new System.Windows.Forms.Timer { Interval = 20 };
            t.Tick += (_, _) => { if (IsDisposed) { t.Dispose(); return; } Opacity = Math.Min(0.97, Opacity + 0.12); if (Opacity >= 0.97) { t.Stop(); t.Dispose(); } };
            t.Start();
        }

        public void CloseAfter(double seconds) => closeAt = DateTime.Now.AddSeconds(seconds);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(color)) g.FillRectangle(b, 0, 0, S(6), Height);
            using (var p = new Pen(Theme.Line)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            int x = S(20), w = Width - S(40);
            var tsz = g.MeasureString(title, Theme.Title, w);
            using (var b = new SolidBrush(Theme.Text)) g.DrawString(title, Theme.Title, b, new RectangleF(x, S(12), w, tsz.Height));
            if (text.Length > 0)
                using (var b = new SolidBrush(Theme.Dim)) g.DrawString(text, Theme.Body, b, new RectangleF(x, S(14) + tsz.Height, w, Height));
            using (var b = new SolidBrush(Theme.Faint)) g.DrawString("×", Theme.Body, b, Width - S(22), S(6));
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
}

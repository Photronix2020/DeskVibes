namespace DeskVibes;

/// <summary>Colors and fonts for the dark DeskVibes look.</summary>
public static class Theme
{
    /// <summary>Set by --render-ui: windows open off-screen without taking the focus.</summary>
    public static bool Quiet;

    public static readonly Color Back = Color.FromArgb(24, 24, 28);
    public static readonly Color Panel = Color.FromArgb(32, 32, 38);
    public static readonly Color Card = Color.FromArgb(38, 38, 46);
    public static readonly Color Raised = Color.FromArgb(50, 50, 60);
    public static readonly Color Line = Color.FromArgb(62, 62, 74);
    public static readonly Color Text = Color.FromArgb(240, 240, 245);
    public static readonly Color Dim = Color.FromArgb(185, 185, 196);
    public static readonly Color Faint = Color.FromArgb(125, 125, 138);
    public static readonly Color Accent = Color.FromArgb(124, 92, 255);
    public static readonly Color AccentHot = Color.FromArgb(146, 120, 255);
    public static readonly Color Good = Color.FromArgb(48, 209, 88);
    public static readonly Color Bad = Color.FromArgb(255, 69, 58);
    public static readonly Color Warn = Color.FromArgb(255, 214, 10);

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Bold = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Title = new("Segoe UI Semibold", 11.5f);
    public static readonly Font Big = new("Segoe UI Light", 22f);
    public static readonly Font Huge = new("Segoe UI Light", 30f);
    public static readonly Font Head = new("Segoe UI Semibold", 15f);

    /// <summary>The app icon: a little window with a sparkle, drawn in code.</summary>
    public static Icon MakeIcon(int size = 32, bool paused = false)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float s = size / 32f;
            var main = paused ? Color.FromArgb(140, 140, 150) : Accent;
            using var path = Round(new RectangleF(2 * s, 5 * s, 24 * s, 22 * s), 5 * s);
            using (var b = new SolidBrush(main)) g.FillPath(b, path);
            using (var b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillRectangle(b, 2 * s, 10 * s, 24 * s, 1.6f * s);
            using (var b = new SolidBrush(Color.White))
            {
                g.FillEllipse(b, 5 * s, 6.5f * s, 2.2f * s, 2.2f * s);
                g.FillEllipse(b, 8.5f * s, 6.5f * s, 2.2f * s, 2.2f * s);
            }
            // sparkle
            var sp = paused ? Color.FromArgb(200, 200, 200) : Warn;
            using (var b = new SolidBrush(sp)) g.FillPolygon(b, Star(24 * s, 8 * s, 7.5f * s, 2f * s));
            using (var b = new SolidBrush(Color.White)) g.FillPolygon(b, Star(13 * s, 19 * s, 4.5f * s, 1.3f * s));
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    static PointF[] Star(float cx, float cy, float r, float inner)
    {
        var pts = new PointF[8];
        for (int i = 0; i < 8; i++)
        {
            double ang = Math.PI / 4 * i - Math.PI / 2;
            float rr = i % 2 == 0 ? r : inner;
            pts[i] = new PointF(cx + (float)Math.Cos(ang) * rr, cy + (float)Math.Sin(ang) * rr);
        }
        return pts;
    }

    public static System.Drawing.Drawing2D.GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Button Button(string text, bool primary = false)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, ForeColor = Text, Font = Bold,
            BackColor = primary ? Accent : Raised, Cursor = Cursors.Hand, Padding = new Padding(10, 3, 10, 3), Margin = new Padding(4),
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderColor = primary ? Accent : Line;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentHot : Line;
        return b;
    }

    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text, AutoSize = true, Font = font ?? Body, ForeColor = color ?? Text, BackColor = Color.Transparent, Margin = new Padding(3, 4, 3, 2), UseMnemonic = false,
    };

    public static void Style(Control c)
    {
        switch (c)
        {
            case TextBox t: t.BackColor = Raised; t.ForeColor = Text; t.BorderStyle = BorderStyle.FixedSingle; t.Font = Body; break;
            case ComboBox cb:
                cb.BackColor = Raised; cb.ForeColor = Text; cb.FlatStyle = FlatStyle.Flat; cb.Font = Body;
                cb.DrawMode = DrawMode.OwnerDrawFixed; cb.ItemHeight = 20;
                cb.DrawItem -= DrawCombo; cb.DrawItem += DrawCombo;
                break;
            case NumericUpDown n: n.BackColor = Raised; n.ForeColor = Text; n.BorderStyle = BorderStyle.FixedSingle; n.Font = Body; break;
            case CheckBox k: k.ForeColor = Text; k.Font = Body; k.BackColor = Color.Transparent; break;
            case RadioButton r: r.ForeColor = Text; r.Font = Body; r.BackColor = Color.Transparent; break;
        }
    }

    static void DrawCombo(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        bool hot = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var b = new SolidBrush(hot ? Accent : Raised)) e.Graphics.FillRectangle(b, e.Bounds);
        if (e.Index < 0) return;
        TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, e.Bounds, Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    /// <summary>Dark title bar on Windows 10/11.</summary>
    public static void DarkTitle(Form f)
    {
        try { int on = 1; DwmSetWindowAttribute(f.Handle, 20, ref on, 4); } catch { }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);
}

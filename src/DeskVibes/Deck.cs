using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace DeskVibes;

/// <summary>The button deck widget ("w.deck"): a Stream Deck style grid of buttons that open programs.
/// Button i is stored in the widget's answers as "b{i}" (what to open) and "b{i}.name" (its label).</summary>
public static class Deck
{
    public const int MaxCols = 16, MaxRows = 8;

    public static int Cols(Step s) => Math.Clamp((int)s.Num("cols", 8), 1, MaxCols);
    public static int Rows(Step s) => Math.Clamp((int)s.Num("rows", 2), 1, MaxRows);
    public static int Count(Step s) => Cols(s) * Rows(s);
    public static int ButtonSize(Step s) => Math.Clamp((int)s.Num("size", 64), 32, 160);
    public static bool AlwaysOpen(Step s) => s.Get("show") == "Always";
    public static bool Labels(Step s) => s.Get("labels", "true") != "false";

    public static string Target(Step s, int i) => s.Get($"b{i}");
    public static string Label(Step s, int i) => s.Get($"b{i}.name");

    public static void Assign(Step s, int i, string target)
    {
        target = target.Trim().Trim('"');
        if (target.Length == 0) { Clear(s, i); return; }
        s.P[$"b{i}"] = target;
        s.P[$"b{i}.name"] = NameFor(target);
    }

    public static void Clear(Step s, int i) { s.P.Remove($"b{i}"); s.P.Remove($"b{i}.name"); }

    /// <summary>What decides how the deck window is built: everything except the buttons themselves,
    /// so assigning a program doesn't rebuild the window.</summary>
    public static string Shape(Step s) =>
        string.Join("|", s.P.Where(kv => !System.Text.RegularExpressions.Regex.IsMatch(kv.Key, @"^b\d+(\.name)?$")).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));

    /// <summary>A friendly name: the shortcut's name, the program's description, or the file name.</summary>
    public static string NameFor(string target)
    {
        target = target.Trim().Trim('"');
        if (target.Contains("://")) return Uri.TryCreate(target, UriKind.Absolute, out var u) && u.Host.Length > 0 ? u.Host.Replace("www.", "") : target;
        var trimmed = target.TrimEnd('\\', '/');
        if (trimmed.Length <= 2 && trimmed.EndsWith(':')) return trimmed.ToUpperInvariant() + " drive";
        string name = Path.GetFileNameWithoutExtension(trimmed);
        if (Path.GetExtension(trimmed).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var d = File.Exists(trimmed) ? FileVersionInfo.GetVersionInfo(trimmed).FileDescription?.Trim() ?? "" : "";
                foreach (var brand in new[] { "Microsoft ", "Windows " })
                    if (d.StartsWith(brand, StringComparison.OrdinalIgnoreCase) && d.Length > brand.Length + 2) d = d[brand.Length..];
                // short enough to fit under an icon, otherwise the file name says it better
                if (d.Length is > 0 and <= 14) name = d;
            }
            catch { }
        }
        if (name.Length == 0) name = trimmed;
        return name.Length > 0 && char.IsLower(name[0]) ? char.ToUpperInvariant(name[0]) + name[1..] : name;
    }

    /// <summary>Opens what's on a button. Returns a problem in plain words, or null when it worked.</summary>
    public static string? Launch(string target)
    {
        target = target.Trim().Trim('"');
        if (target.Length == 0) return null;
        bool local = !target.Contains("://") && !target.EndsWith(':');
        if (local && !File.Exists(target) && !Directory.Exists(target)) return "It isn't there anymore. Right-click the button to pick it again.";
        try
        {
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            // games and older programs often expect to start in their own folder
            if (local && File.Exists(target) && Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                psi.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
            Process.Start(psi)?.Dispose();
            return null;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return null; } // "no" on the admin prompt
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>Where each button sits, for a deck with a header strip on top.</summary>
    public static (Size whole, Rectangle[] buttons) Layout(int cols, int rows, int size, int gap, int header)
    {
        var rects = new Rectangle[cols * rows];
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                rects[r * cols + c] = new Rectangle(gap + c * (size + gap), header + r * (size + gap), size, size);
        return (new Size(gap + cols * (size + gap), header + rows * (size + gap)), rects);
    }

    // ---------- icons ----------

    static readonly Dictionary<string, Bitmap?> icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The program's own icon (from the shell, so shortcuts and store apps look right), cached.</summary>
    public static Bitmap? Icon(string target, int px)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var key = px + "|" + target;
        if (icons.TryGetValue(key, out var cached)) return cached;
        Bitmap? bmp = null;
        try { bmp = ShellImage(target.Trim().Trim('"'), px); } catch { }
        if (bmp == null)
            try { using var ic = System.Drawing.Icon.ExtractAssociatedIcon(target); bmp = ic?.ToBitmap(); } catch { }
        icons[key] = bmp;
        return bmp;
    }

    public static void ForgetIcon(string target)
    {
        foreach (var k in icons.Keys.Where(k => k.EndsWith("|" + target, StringComparison.OrdinalIgnoreCase)).ToList()) icons.Remove(k);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr hbitmap); }

    [StructLayout(LayoutKind.Sequential)] struct NativeSize { public int cx, cy; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bindCtx, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    static Bitmap? ShellImage(string path, int px)
    {
        if (path.Contains("://") || (!File.Exists(path) && !Directory.Exists(path))) return null;
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
        try
        {
            const int BiggerSizeOk = 0x1, IconOnly = 0x4;
            if (factory.GetImage(new NativeSize { cx = px, cy = px }, BiggerSizeOk | IconOnly, out var hbm) != 0 || hbm == IntPtr.Zero) return null;
            try { return WithAlpha(hbm); }
            finally { Native.DeleteObject(hbm); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }

    /// <summary>Image.FromHbitmap drops the alpha channel; read the bits again as premultiplied ARGB.</summary>
    static Bitmap WithAlpha(IntPtr hbm)
    {
        using var plain = Image.FromHbitmap(hbm);
        var rect = new Rectangle(0, 0, plain.Width, plain.Height);
        var data = plain.LockBits(rect, ImageLockMode.ReadOnly, plain.PixelFormat);
        try
        {
            if (Image.GetPixelFormatSize(plain.PixelFormat) != 32) return new Bitmap(plain);
            bool anyAlpha = false;
            var row = new byte[data.Stride];
            for (int y = 0; y < data.Height && !anyAlpha; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                for (int x = 3; x < data.Width * 4; x += 4) if (row[x] != 0) { anyAlpha = true; break; }
            }
            if (!anyAlpha) return new Bitmap(plain);
            using var view = new Bitmap(data.Width, data.Height, data.Stride, PixelFormat.Format32bppPArgb, data.Scan0);
            var copy = new Bitmap(data.Width, data.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(copy)) { g.CompositingMode = CompositingMode.SourceCopy; g.DrawImage(view, 0, 0, data.Width, data.Height); }
            return copy;
        }
        finally { plain.UnlockBits(data); }
    }
}

/// <summary>The button deck on screen. Folded it's a small tab; point at it and the grid of buttons opens.
/// Click a button to open its program, right-click (or click an empty one) to choose what it opens, or drop a program on it.</summary>
public sealed class DeckForm : Form
{
    public Vibe Vibe { get; private set; }
    readonly Action<Vibe> saved;
    readonly Action<Vibe> edit;
    readonly System.Windows.Forms.Timer hover = new() { Interval = 100 };
    bool open, grewUp, busy;
    int hot = -1, pressed = -1, insideTicks;
    DateTime outsideSince = DateTime.MaxValue;
    Point anchor;
    Rectangle[] buttons = Array.Empty<Rectangle>();

    Step S => Vibe.Widget!;
    int Sc(int px) => (int)(px * DeviceDpi / 96.0);
    int Header => Sc(26);
    Size Folded => new(Sc(150), Sc(30));

    public DeckForm(Vibe v, Action<Vibe> saved, Action<Vibe> edit)
    {
        Vibe = v; this.saved = saved; this.edit = edit;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        MaximizeBox = false; MinimizeBox = false; DoubleBuffered = true; BackColor = Theme.Card; AllowDrop = true;
        Text = v.Name;
        hover.Tick += (_, _) => Watch();
        ApplyPlace();
        hover.Start();
    }

    public bool IsOpen => open;

    public void Update(Vibe v)
    {
        Vibe = v; Text = v.Name;
        ApplyPlace();
    }

    void ApplyPlace()
    {
        var p = Vibe.Place;
        var area = Screen.PrimaryScreen!.WorkingArea;
        if (p.X == -30000) anchor = new Point(p.X, p.Y); // off-screen rendering
        else if (p.X < 0 || p.Y < 0 || !Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(p.X, p.Y, Folded.Width, Folded.Height))))
            anchor = new Point(area.Right - Folded.Width - Sc(24), area.Top + Sc(24));
        else anchor = new Point(p.X, p.Y);
        Opacity = Math.Clamp(p.Opacity, 20, 100) / 100.0;
        TopMost = p.Layer == "top";
        SetOpen(Deck.AlwaysOpen(S) || open, true);
        if (IsHandleCreated && p.Layer == "desktop") SendBack();
    }

    /// <summary>Opens or folds the grid. It opens downwards, or upwards when there's no room below.</summary>
    public void SetOpen(bool on, bool force = false)
    {
        if (on == open && !force) return;
        open = on;
        if (!open)
        {
            buttons = Array.Empty<Rectangle>(); hot = -1;
            Bounds = new Rectangle(anchor, Folded);
        }
        else
        {
            var (size, rects) = Deck.Layout(Deck.Cols(S), Deck.Rows(S), Sc(Deck.ButtonSize(S)), Sc(8), Header);
            buttons = rects;
            var area = anchor.X == -30000 ? new Rectangle(-30000, -30000, 20000, 20000) : Screen.FromPoint(anchor).WorkingArea;
            int x = Math.Max(area.Left, Math.Min(anchor.X, area.Right - size.Width));
            int y = anchor.Y;
            grewUp = y + size.Height > area.Bottom;
            if (grewUp) y = Math.Max(area.Top, anchor.Y + Folded.Height - size.Height);
            Bounds = new Rectangle(x, y, size.Width, size.Height);
        }
        Invalidate();
    }

    /// <summary>Opens when the mouse rests on the tab, folds again a moment after it leaves.</summary>
    void Watch()
    {
        if (!Visible || Deck.AlwaysOpen(S)) return;
        var inside = Bounds;
        inside.Inflate(Sc(6), Sc(6));
        bool over = inside.Contains(Cursor.Position);
        if (!open)
        {
            insideTicks = over ? insideTicks + 1 : 0;
            if (insideTicks >= 2) { SetOpen(true); outsideSince = DateTime.MaxValue; }
            return;
        }
        if (over || busy || MouseButtons != MouseButtons.None) { outsideSince = DateTime.MaxValue; return; }
        if (outsideSince == DateTime.MaxValue) outsideSince = DateTime.Now;
        else if (DateTime.Now - outsideSince > TimeSpan.FromMilliseconds(600)) { SetOpen(false); insideTicks = 0; }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // never steals focus from the game or app you're in
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    void SendBack() => Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Vibe.Place.Layer == "desktop") SendBack();
    }

    // ---------- drawing ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using (var pen = new Pen(Theme.Line)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        // the tab / header: a little grid mark and the name
        int top = 0, h = open ? Header : Height;
        using (var mark = new SolidBrush(open ? Theme.Faint : Theme.AccentHot))
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 3; c++)
                    g.FillRectangle(mark, Sc(12) + c * Sc(6), top + h / 2 - Sc(5) + r * Sc(6), Sc(4), Sc(4));
        var title = open ? Vibe.Name.ToUpperInvariant() : Vibe.Name;
        using (var tb = new SolidBrush(open ? Theme.Faint : Theme.Text))
        {
            var sf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            g.DrawString(title, open ? Theme.Small : Theme.Body, tb, new RectangleF(Sc(36), top, Width - Sc(44), h), sf);
        }
        if (!open) return;

        bool labels = Deck.Labels(S);
        bool empty = Enumerable.Range(0, buttons.Length).All(i => Deck.Target(S, i).Length == 0);
        if (empty)
            using (var hb = new SolidBrush(Theme.Faint))
            {
                var hint = "click a button to pick a program";
                var sz = g.MeasureString(hint, Theme.Small);
                g.DrawString(hint, Theme.Small, hb, Width - sz.Width - Sc(10), (Header - sz.Height) / 2);
            }
        for (int i = 0; i < buttons.Length; i++)
        {
            var b = buttons[i];
            var target = Deck.Target(S, i);
            using var path = Theme.Round(new RectangleF(b.X + 0.5f, b.Y + 0.5f, b.Width - 1, b.Height - 1), Sc(10));
            var fill = i == pressed ? Theme.Accent : i == hot ? Theme.Line : Theme.Raised;
            using (var fb = new SolidBrush(fill)) g.FillPath(fb, path);
            if (i == hot) using (var hp = new Pen(Theme.AccentHot, Sc(2) / 2f)) g.DrawPath(hp, path);
            if (target.Length == 0)
            {
                using var plus = new Pen(i == hot ? Theme.Dim : Theme.Line, Sc(2));
                int cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2, arm = b.Width / 7;
                g.DrawLine(plus, cx - arm, cy, cx + arm, cy); g.DrawLine(plus, cx, cy - arm, cx, cy + arm);
                continue;
            }
            int iconPx = (int)(b.Width * (labels ? 0.52 : 0.66));
            var icon = Deck.Icon(target, iconPx <= 32 ? 32 : iconPx <= 48 ? 48 : iconPx <= 64 ? 64 : 128);
            int ix = b.X + (b.Width - iconPx) / 2, iy = labels ? b.Y + Sc(6) : b.Y + (b.Height - iconPx) / 2;
            if (icon != null) g.DrawImage(icon, new Rectangle(ix, iy, iconPx, iconPx));
            else using (var qb = new SolidBrush(Theme.Dim)) g.DrawString("?", Theme.Title, qb, ix + iconPx / 3, iy + iconPx / 4);
            if (labels)
            {
                using var lb = new SolidBrush(Theme.Text);
                using var font = new Font("Segoe UI", Math.Clamp(b.Width / (float)Sc(64) * 7.5f, 6.5f, 10f));
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                g.DrawString(Deck.Label(S, i), font, lb, new RectangleF(b.X + Sc(3), iy + iconPx, b.Width - Sc(6), b.Bottom - iy - iconPx), sf);
            }
        }
    }

    // ---------- mouse ----------

    int ButtonAt(Point p) => Array.FindIndex(buttons, r => r.Contains(p));

    protected override void WndProc(ref Message m)
    {
        if (Vibe == null) { base.WndProc(ref m); return; }
        if (m.Msg == Native.WM_NCHITTEST)
        {
            base.WndProc(ref m);
            var pt = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
            // drag the tab or the header to move it; the buttons are buttons
            if (!Vibe.Place.Locked && (!open || pt.Y < Header)) m.Result = (IntPtr)Native.HTCAPTION;
            return;
        }
        if (m.Msg == 0xA5) { ShowDeckMenu(Cursor.Position); return; } // WM_NCRBUTTONUP on the header
        if (m.Msg == Native.WM_WINDOWPOSCHANGING && Vibe.Place.Layer == "desktop" && IsHandleCreated)
        {
            var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(m.LParam);
            pos.hwndInsertAfter = Native.HWND_BOTTOM;
            pos.flags &= ~Native.SWP_NOZORDER;
            Marshal.StructureToPtr(pos, m.LParam, false);
        }
        base.WndProc(ref m);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = ButtonAt(e.Location);
        if (h != hot) { hot = h; Invalidate(); }
        Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hot >= 0) { hot = -1; Invalidate(); } }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { pressed = ButtonAt(e.Location); Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int i = ButtonAt(e.Location);
        if (e.Button == MouseButtons.Right)
        {
            if (i >= 0) ShowButtonMenu(i, Cursor.Position); else ShowDeckMenu(Cursor.Position);
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        bool same = i >= 0 && i == pressed;
        if (same && Deck.Target(S, i).Length == 0) { pressed = -1; Invalidate(); Choose(i); return; }
        if (same) Press(i);
        var t = new System.Windows.Forms.Timer { Interval = 140 };
        t.Tick += (_, _) => { t.Dispose(); pressed = -1; Invalidate(); };
        t.Start();
    }

    void Press(int i)
    {
        var target = Deck.Target(S, i);
        var problem = Deck.Launch(target);
        if (problem != null) Cards.Show("Couldn't open " + Deck.Label(S, i), problem, Theme.Bad, 8, "Bottom right", "");
        else if (!Deck.AlwaysOpen(S)) outsideSince = DateTime.Now.AddMilliseconds(-300); // fold soon after use
    }

    // ---------- choosing what a button opens ----------

    void Choose(int i)
    {
        busy = true;
        try
        {
            using var dlg = new OpenFileDialog
            {
                Title = $"Pick a program for button {i + 1}",
                Filter = "Programs and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd;*.appref-ms|Anything|*.*",
                DereferenceLinks = false, // keep the shortcut: it has the right icon and settings
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            };
            var current = Deck.Target(S, i);
            if (current.Length > 0 && File.Exists(current)) { dlg.InitialDirectory = Path.GetDirectoryName(current); dlg.FileName = Path.GetFileName(current); }
            if (dlg.ShowDialog() == DialogResult.OK) Set(i, dlg.FileName);
        }
        finally { busy = false; outsideSince = DateTime.MaxValue; }
    }

    void Set(int i, string target)
    {
        Deck.ForgetIcon(target);
        Deck.Assign(S, i, target);
        saved(Vibe);
        Invalidate();
    }

    void ShowButtonMenu(int i, Point at)
    {
        var menu = Menu();
        var target = Deck.Target(S, i);
        if (target.Length > 0)
        {
            menu.Items.Add(new ToolStripMenuItem(Deck.Label(S, i)) { Enabled = false });
            Add(menu, "Open it", () => Press(i));
            Add(menu, "Choose a different program…", () => Choose(i));
            Add(menu, "Clear this button", () => { Deck.Clear(S, i); saved(Vibe); Invalidate(); });
        }
        else Add(menu, "Choose a program…", () => Choose(i));
        menu.Items.Add(new ToolStripSeparator());
        DeckItems(menu);
        Show(menu, at);
    }

    void ShowDeckMenu(Point at)
    {
        var menu = Menu();
        DeckItems(menu);
        Show(menu, at);
    }

    void DeckItems(ContextMenuStrip menu)
    {
        var p = Vibe.Place;
        Add(menu, "Keep the buttons open", () => { S.P["show"] = Deck.AlwaysOpen(S) ? "When I point at it" : "Always"; saved(Vibe); ApplyPlace(); }, Deck.AlwaysOpen(S));
        Add(menu, "Keep on top", () => { p.Layer = p.Layer == "top" ? "normal" : "top"; saved(Vibe); ApplyPlace(); }, p.Layer == "top");
        Add(menu, "Lock in place", () => { p.Locked = !p.Locked; saved(Vibe); }, p.Locked);
        Add(menu, "Change size and layout…", () => edit(Vibe));
        Add(menu, "Hide it", () => { Vibe.On = false; saved(Vibe); });
    }

    static ContextMenuStrip Menu() => new() { Renderer = new DarkMenu(), ShowImageMargin = false };

    static void Add(ContextMenuStrip menu, string text, Action act, bool check = false)
    {
        var item = new ToolStripMenuItem(text) { Checked = check };
        item.Click += (_, _) => act();
        menu.Items.Add(item);
    }

    void Show(ContextMenuStrip menu, Point at)
    {
        busy = true;
        menu.Closed += (_, _) => { busy = false; outsideSince = DateTime.MaxValue; BeginInvoke(menu.Dispose); };
        menu.Show(at);
    }

    // ---------- drag a program onto a button ----------

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        int i = open ? ButtonAt(PointToClient(new Point(e.X, e.Y))) : -1;
        e.Effect = i >= 0 && e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Link : DragDropEffects.None;
        if (i != hot) { hot = i; Invalidate(); }
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        int i = ButtonAt(PointToClient(new Point(e.X, e.Y)));
        if (i >= 0 && e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0) Set(i, files[0]);
    }

    // ---------- remembering where it is ----------

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        // after a drag, the tab's spot follows the window
        anchor = new Point(Left, open && grewUp ? Bottom - Folded.Height : Top);
        var p = Vibe.Place;
        if (p.X == anchor.X && p.Y == anchor.Y) return;
        p.X = anchor.X; p.Y = anchor.Y;
        saved(Vibe);
    }

    protected override void Dispose(bool disposing) { if (disposing) hover.Dispose(); base.Dispose(disposing); }
}

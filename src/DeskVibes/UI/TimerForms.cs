using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Media;

namespace DeskVibes;

/// <summary>"New timer or alarm": one click for the usual timer lengths, or set your own; an alarm is a time and how often.</summary>
public sealed class TimerDialog : Form
{
    readonly FlowLayoutPanel timerPanel, alarmPanel;
    readonly Button timerTab, alarmTab, go;
    readonly TextBox label;
    readonly NumericUpDown hours, minutes, seconds, alarmHour;
    readonly ComboBox alarmMin, ampm, repeat;
    readonly bool clock24;
    bool alarmMode;

    public TimerDialog(string mode, bool clock24)
    {
        this.clock24 = clock24;
        Text = "Timers and alarms";
        Icon = Theme.MakeIcon();
        BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; MinimizeBox = false;
        ClientSize = new Size(470, 380);

        var body = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(20, 14, 20, 8), BackColor = Theme.Back };
        var tabs = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        timerTab = Theme.Button("⏱  Timer"); alarmTab = Theme.Button("⏰  Alarm");
        timerTab.Click += (_, _) => Mode(false); alarmTab.Click += (_, _) => Mode(true);
        tabs.Controls.Add(timerTab); tabs.Controls.Add(alarmTab);
        body.Controls.Add(tabs);

        // ---- timer ----
        timerPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        timerPanel.Controls.Add(Theme.Label("Start one now", Theme.Title));
        var chips = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(440, 0), Margin = new Padding(0, 2, 0, 8) };
        foreach (var m in Timers.QuickMinutes)
        {
            var b = Theme.Button(m == 60 ? "1 hour" : $"{m} min");
            b.Click += (_, _) => { Start(TimeSpan.FromMinutes(m)); };
            chips.Controls.Add(b);
        }
        timerPanel.Controls.Add(chips);
        timerPanel.Controls.Add(Theme.Label("Or set your own", Theme.Title));
        var own = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 2, 0, 4) };
        hours = Num(0, 99, 0); minutes = Num(0, 59, 10); seconds = Num(0, 59, 0);
        own.Controls.Add(hours); own.Controls.Add(Unit("hours"));
        own.Controls.Add(minutes); own.Controls.Add(Unit("minutes"));
        own.Controls.Add(seconds); own.Controls.Add(Unit("seconds"));
        timerPanel.Controls.Add(own);
        body.Controls.Add(timerPanel);

        // ---- alarm ----
        alarmPanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0), Visible = false };
        alarmPanel.Controls.Add(Theme.Label("Ring at", Theme.Title));
        var at = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 2, 0, 8) };
        var next = DateTime.Now.AddHours(1);
        alarmHour = clock24 ? Num(0, 23, next.Hour) : Num(1, 12, next.Hour % 12 == 0 ? 12 : next.Hour % 12);
        alarmMin = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 62, Margin = new Padding(3, 3, 3, 3), MaxDropDownItems = 12 }; Theme.Style(alarmMin);
        for (int i = 0; i < 60; i++) alarmMin.Items.Add($"{i:00}");
        alarmMin.SelectedIndex = 0;
        ampm = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Margin = new Padding(6, 3, 3, 3) }; Theme.Style(ampm);
        ampm.Items.AddRange(new object[] { "AM", "PM" }); ampm.SelectedIndex = next.Hour >= 12 ? 1 : 0;
        at.Controls.Add(alarmHour); at.Controls.Add(Unit(":")); at.Controls.Add(alarmMin);
        if (!clock24) at.Controls.Add(ampm);
        alarmPanel.Controls.Add(at);
        alarmPanel.Controls.Add(Theme.Label("Repeat", Theme.Title));
        repeat = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Margin = new Padding(3, 2, 3, 8) }; Theme.Style(repeat);
        repeat.Items.AddRange(Timers.Repeats); repeat.SelectedIndex = 0;
        alarmPanel.Controls.Add(repeat);
        body.Controls.Add(alarmPanel);

        body.Controls.Add(Theme.Label("Name (optional)", Theme.Small, Theme.Dim));
        label = new TextBox { Width = 300, PlaceholderText = "e.g. Pizza, Call mom, Stretch" }; Theme.Style(label);
        body.Controls.Add(label);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(14, 8, 14, 8), BackColor = Theme.Panel };
        go = Theme.Button("Start timer", true);
        go.Click += (_, _) =>
        {
            if (alarmMode) SetAlarm();
            else Start(new TimeSpan((int)hours.Value, (int)minutes.Value, (int)seconds.Value));
        };
        var cancel = Theme.Button("Cancel"); cancel.Click += (_, _) => Close();
        bottom.Controls.Add(go); bottom.Controls.Add(cancel);
        Controls.Add(body); Controls.Add(bottom);
        AcceptButton = go; CancelButton = cancel;
        Mode(mode == "alarm");
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.DarkTitle(this); }
    protected override bool ShowWithoutActivation => Theme.Quiet;

    static NumericUpDown Num(int min, int max, int value)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 58, Margin = new Padding(3, 3, 3, 3), TextAlign = HorizontalAlignment.Center };
        Theme.Style(n);
        return n;
    }

    static Label Unit(string t) { var l = Theme.Label(t, Theme.Small, Theme.Dim); l.Margin = new Padding(0, 8, 8, 0); return l; }

    void Mode(bool alarm)
    {
        alarmMode = alarm;
        timerPanel.Visible = !alarm; alarmPanel.Visible = alarm;
        foreach (var (b, on) in new[] { (timerTab, !alarm), (alarmTab, alarm) })
        {
            b.BackColor = on ? Theme.Accent : Theme.Raised;
            b.FlatAppearance.BorderColor = on ? Theme.Accent : Theme.Line;
        }
        go.Text = alarm ? "Set alarm" : "Start timer";
    }

    void Start(TimeSpan length)
    {
        if (length.TotalSeconds < 1) { minutes.Focus(); return; }
        var t = Timers.Timer(length, label.Text, DateTime.Now);
        Timers.Add(t);
        TimerMenus.Confirm(t);
        Close();
    }

    void SetAlarm()
    {
        int h = (int)alarmHour.Value;
        if (!clock24) h = h % 12 + (ampm.SelectedIndex == 1 ? 12 : 0);
        var a = Timers.Alarm(new TimeSpan(h, alarmMin.SelectedIndex, 0), repeat.SelectedItem as string ?? "Once", label.Text, DateTime.Now);
        Timers.Add(a);
        TimerMenus.Confirm(a);
        Close();
    }
}

/// <summary>The pop-up when a timer or alarm goes off: rings and lights the edges until you press Stop (or Snooze).</summary>
public sealed class RingForm : Form
{
    static readonly List<RingForm> ringing = new();
    readonly TimerItem item;
    readonly System.Windows.Forms.Timer bell = new() { Interval = 4000 };
    readonly DateTime started = DateTime.Now;
    SoundPlayer? player;

    public static void Ring(TimerItem item)
    {
        var f = new RingForm(item);
        ringing.Add(f);
        f.FormClosed += (_, _) => { ringing.Remove(f); if (ringing.Count == 0) Overlays.ClearEdges(); Place(); };
        Place();
        f.Show();
    }

    static void Place()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        int y = area.Top + area.Height / 6;
        foreach (var f in ringing) { f.Location = new Point(area.Left + (area.Width - f.Width) / 2, y); y += f.Height + 12; }
    }

    int S(int px) => (int)(px * DeviceDpi / 96.0);

    public RingForm(TimerItem item)
    {
        this.item = item;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        TopMost = true; BackColor = Theme.Card; DoubleBuffered = true; Font = Theme.Body;
        Size = new Size(S(420), S(150));
        Text = Timers.Name(item);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = S(48), Padding = new Padding(S(10), S(6), S(10), S(6)), BackColor = Color.Transparent };
        var stop = Theme.Button("Stop", true); stop.Click += (_, _) => Close();
        var snooze = Theme.Button("Snooze 5 min"); snooze.Click += (_, _) => { Timers.Add(Timers.Snooze(item, 5, DateTime.Now)); Close(); };
        buttons.Controls.Add(stop); buttons.Controls.Add(snooze);
        if (!item.IsAlarm)
        {
            var again = Theme.Button("Again"); again.Click += (_, _) => { Timers.Add(Timers.Timer(TimeSpan.FromSeconds(item.Seconds), item.Label, DateTime.Now)); Close(); };
            buttons.Controls.Add(again);
        }
        Controls.Add(buttons);

        bell.Tick += (_, _) =>
        {
            // ring for 3 minutes at most, then just wait quietly
            if (DateTime.Now - started > TimeSpan.FromMinutes(3)) { bell.Stop(); return; }
            Chime();
        };
        if (!Theme.Quiet)
        {
            Overlays.Edges(Color.FromArgb(255, 149, 0), "Pulse", 26, 0);
            Chime();
            bell.Start();
        }
    }

    void Chime()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
            if (File.Exists(path)) { player ??= new SoundPlayer(path); player.Play(); }
            else SystemSounds.Exclamation.Play();
        }
        catch { }
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE; return cp; } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var orange = Color.FromArgb(255, 149, 0);
        using (var b = new SolidBrush(orange)) g.FillRectangle(b, 0, 0, S(6), Height);
        using (var p = new Pen(orange)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        int x = S(22), w = Width - S(40);
        var head = item.IsAlarm ? "⏰  Alarm · " + Timers.Clock(Timers.TimeOf(item)) : "⏱  Time's up!";
        using (var b = new SolidBrush(orange)) g.DrawString(head, Theme.Bold, b, x, S(14));
        var big = item.Label.Length > 0 ? item.Label : item.IsAlarm ? "Your alarm" : Timers.Length(item.Seconds) + " timer";
        using (var big2 = new Font("Segoe UI Semibold", 17f))
        using (var b = new SolidBrush(Theme.Text))
            g.DrawString(big, big2, b, new RectangleF(x, S(36), w, S(36)), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
        if (item.Label.Length > 0)
            using (var b = new SolidBrush(Theme.Dim))
                g.DrawString(item.IsAlarm ? Timers.RepeatText(item) : Timers.Length(item.Seconds) + " timer", Theme.Small, b, x, S(74));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { bell.Dispose(); player?.Stop(); player?.Dispose(); }
        base.Dispose(disposing);
    }
}

/// <summary>The same timer menus in the tray and on the Timers widget.</summary>
public static class TimerMenus
{
    /// <summary>Quick timers, "Set an alarm…" and "Other timer…".</summary>
    public static void AddNew(ToolStripItemCollection items)
    {
        foreach (var m in new[] { 1, 5, 10, 15, 25, 30, 60 })
        {
            var mm = m;
            var i = new ToolStripMenuItem(m == 60 ? "1 hour timer" : $"{m} minute timer");
            i.Click += (_, _) =>
            {
                var t = Timers.Timer(TimeSpan.FromMinutes(mm), "", DateTime.Now);
                Timers.Add(t);
                Confirm(t);
            };
            items.Add(i);
        }
        items.Add(new ToolStripMenuItem("Other timer…", null, (_, _) => Timers.ShowDialog?.Invoke("timer")));
        items.Add(new ToolStripMenuItem("Set an alarm…", null, (_, _) => Timers.ShowDialog?.Invoke("alarm")));
    }

    /// <summary>Cancel / add time for a timer; on-off / delete for an alarm.</summary>
    public static void AddFor(ToolStripItemCollection items, TimerItem t)
    {
        if (t.IsAlarm)
        {
            items.Add(new ToolStripMenuItem(t.On ? "Turn off" : "Turn on", null, (_, _) => { Timers.SetOn(t, !t.On, DateTime.Now); Timers.Touch(); }));
            items.Add(new ToolStripMenuItem("Delete this alarm", null, (_, _) => Timers.Remove(t.Id)));
        }
        else
        {
            items.Add(new ToolStripMenuItem("Add 1 minute", null, (_, _) => { t.Due = t.Due.AddMinutes(1); t.Seconds += 60; Timers.Touch(); }));
            items.Add(new ToolStripMenuItem("Add 5 minutes", null, (_, _) => { t.Due = t.Due.AddMinutes(5); t.Seconds += 300; Timers.Touch(); }));
            items.Add(new ToolStripMenuItem("Cancel this timer", null, (_, _) => Timers.Remove(t.Id)));
        }
    }

    /// <summary>A short card saying it's set, so you know it worked.</summary>
    public static void Confirm(TimerItem t)
    {
        var now = DateTime.Now;
        if (t.IsAlarm)
        {
            var rep = Timers.RepeatText(t);
            Cards.Show("Alarm set: " + Timers.Left(t, now) + (rep.Length > 0 ? ", " + rep : ""), t.Label, Theme.Accent, 5, "Bottom right", "");
        }
        else Cards.Show("Timer started: " + Timers.Length(t.Seconds), (t.Label.Length > 0 ? t.Label + " · " : "") + "rings at " + Timers.Clock(t.Due.TimeOfDay), Theme.Accent, 5, "Bottom right", "");
    }

    /// <summary>"Pizza — 4:59" / "7:30 AM weekdays".</summary>
    public static string Line(TimerItem t, DateTime now)
    {
        var rep = t.IsAlarm ? Timers.RepeatText(t) : "";
        return $"{Timers.Name(t)} — {Timers.Left(t, now)}{(rep.Length > 0 ? ", " + rep : "")}";
    }

    public static void Popup(Action<ToolStripItemCollection> fill)
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenu(), ShowImageMargin = false };
        fill(menu.Items);
        menu.Closed += (_, _) => menu.BeginInvoke(menu.Dispose);
        menu.Show(Cursor.Position);
    }
}

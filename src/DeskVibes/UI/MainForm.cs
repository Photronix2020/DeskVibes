using Microsoft.Win32;

namespace DeskVibes;

/// <summary>The DeskVibes window: your vibes, ideas to start from, settings and the log.</summary>
public sealed class MainForm : Form
{
    readonly TrayApp app;
    readonly Panel content;
    readonly Dictionary<string, Control> pages = new();
    readonly Dictionary<string, Button> nav = new();
    FlowLayoutPanel? list;
    TextBox? logBox;
    string current = "";

    public MainForm(TrayApp app)
    {
        this.app = app;
        Text = "DeskVibes";
        Icon = Theme.MakeIcon();
        BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(900, 640);
        MinimumSize = new Size(720, 480);

        var side = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 180, FlowDirection = FlowDirection.TopDown, BackColor = Theme.Panel, Padding = new Padding(10, 16, 10, 10), WrapContents = false };
        var logo = new PictureBox { Image = Theme.MakeIcon(48).ToBitmap(), Size = new Size(48, 48), SizeMode = PictureBoxSizeMode.Zoom, Margin = new Padding(8, 0, 0, 2) };
        side.Controls.Add(logo);
        side.Controls.Add(Theme.Label("DeskVibes", Theme.Head));
        var tag = Theme.Label("Little helpers for Windows", Theme.Small, Theme.Faint); tag.Margin = new Padding(4, 0, 0, 18);
        side.Controls.Add(tag);
        foreach (var (id, label) in new[] { ("vibes", "✦  My vibes"), ("ideas", "💡  Ideas"), ("settings", "⚙  Settings"), ("log", "☰  What happened") })
        {
            var b = new Button
            {
                Text = label, Width = 158, Height = 36, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Text,
                BackColor = Theme.Panel, Font = Theme.Bold, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 0, 4), Padding = new Padding(8, 0, 0, 0),
            };
            b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Theme.Raised;
            b.Click += (_, _) => Show(id);
            nav[id] = b; side.Controls.Add(b);
        }

        content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
        Controls.Add(content);
        Controls.Add(side);

        app.VibesChanged += () => { if (!IsDisposed && current == "vibes") FillList(); };
        app.Engine.Logged += line => { if (!IsDisposed && logBox != null) BeginInvoke(() => logBox.AppendText(line + Environment.NewLine)); };
        Load += (_, _) => Theme.DarkTitle(this);
        Show("vibes");
    }

    public void Show(string id)
    {
        current = id;
        foreach (var (k, b) in nav) b.BackColor = k == id ? Theme.Raised : Theme.Panel;
        if (!pages.TryGetValue(id, out var page))
        {
            page = id switch { "ideas" => IdeasPage(), "settings" => SettingsPage(), "log" => LogPage(), _ => VibesPage() };
            page.Dock = DockStyle.Fill;
            pages[id] = page;
            content.Controls.Add(page);
        }
        if (id == "vibes") FillList();
        foreach (var (k, pg) in pages) pg.Visible = k == id;
        page.BringToFront();
    }

    static FlowLayoutPanel Page() => new()
    {
        FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(26, 20, 26, 20), BackColor = Theme.Back,
    };

    int Inner => content.ClientSize.Width - 80;

    protected override bool ShowWithoutActivation => Theme.Quiet;

    // ---------- my vibes ----------

    Control VibesPage()
    {
        var p = Page();
        p.Controls.Add(Theme.Label("What should your PC do for you?", Theme.Head));
        var sub = Theme.Label("Say it in your own words. I'll set it up and you can check it before saving.", null, Theme.Dim);
        p.Controls.Add(sub);

        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 2) };
        var ask = new TextBox { Width = 560, PlaceholderText = "e.g. when I get an email from my boss, turn the edges of the screen red", Font = new Font("Segoe UI", 11f) };
        Theme.Style(ask); ask.Font = new Font("Segoe UI", 11f);
        var make = Theme.Button("✨ Make it", true);
        async void Go()
        {
            if (ask.Text.Trim().Length == 0 || !make.Enabled) return;
            var key = Secret.Unprotect(app.Store.Settings.AnthropicKeyProtected);
            Describe.Result r;
            if (key.Length == 0) r = Describe.Parse(ask.Text);
            else
            {
                var label = make.Text;
                make.Enabled = false; ask.ReadOnly = true; make.Text = "Thinking…";
                try { r = await AiDescribe.ParseAsync(ask.Text, key); }
                finally { make.Enabled = true; ask.ReadOnly = false; make.Text = label; }
            }
            if (app.Edit(r.Vibe, true, this, string.Join("\n", r.Understood))) ask.Clear();
        }
        make.Click += (_, _) => Go();
        ask.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Go(); } };
        row.Controls.Add(ask); row.Controls.Add(make);
        p.Controls.Add(row);

        var examples = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(0, 0, 0, 10) };
        examples.Controls.Add(Theme.Label("Try:", Theme.Small, Theme.Faint));
        foreach (var ex in new[]
        {
            "if Bitcoin drops below 60000 flash the edges orange",
            "remind me to stretch after 45 minutes of work",
            "when Stormrage realm comes back up say it out loud",
            "when it's going to rain in Boston pop up a card",
            "ctrl+alt+b hides everything",
        })
        {
            var l = new LinkLabel { Text = ex, AutoSize = true, LinkColor = Theme.AccentHot, ActiveLinkColor = Theme.Text, Font = Theme.Small, Margin = new Padding(3, 4, 8, 0) };
            l.LinkClicked += (_, _) => { ask.Text = ex; ask.Focus(); };
            examples.Controls.Add(l);
        }
        p.Controls.Add(examples);

        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 4, 0, 8) };
        var nr = Theme.Button("+ New vibe"); nr.Click += (_, _) => app.Edit(new Vibe { Name = "", When = new Step("mail.new"), Do = { new Step("show.edges") } }, true, this);
        var nw = Theme.Button("+ New widget"); nw.Click += (_, _) => app.Edit(new Vibe { Kind = "widget", Name = "Clock", Widget = new Step("w.clock") }, true, this);
        var paste = Theme.Button("Paste a share code"); paste.Click += (_, _) => PasteCode();
        buttons.Controls.Add(nr); buttons.Controls.Add(nw); buttons.Controls.Add(paste);
        p.Controls.Add(buttons);

        list = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        p.Controls.Add(list);
        p.Resize += (_, _) => { ask.Width = Math.Max(300, Inner - make.Width - 20); foreach (Control c in list.Controls) c.Width = Inner; };
        return p;
    }

    void FillList()
    {
        if (list == null) return;
        list.SuspendLayout();
        foreach (Control c in list.Controls) c.Dispose();
        list.Controls.Clear();
        var vibes = app.Store.Vibes;
        if (vibes.Count == 0)
            list.Controls.Add(Theme.Label("No vibes yet. Describe one above, or pick something from Ideas.", null, Theme.Faint));
        foreach (var group in new[] { ("Rules", vibes.Where(v => !v.IsWidget)), ("Widgets", vibes.Where(v => v.IsWidget)) })
        {
            if (!group.Item2.Any()) continue;
            var h = Theme.Label(group.Item1.ToUpperInvariant(), Theme.Small, Theme.Faint); h.Margin = new Padding(2, 12, 0, 4);
            list.Controls.Add(h);
            foreach (var v in group.Item2) list.Controls.Add(Row(v));
        }
        list.ResumeLayout();
    }

    Control Row(Vibe v)
    {
        var row = new Panel { Width = Inner, Height = 62, BackColor = Theme.Card, Margin = new Padding(0, 0, 0, 6) };
        var toggle = new Toggle { On = v.On, Location = new Point(14, 20) };
        toggle.Changed += () => { v.On = toggle.On; app.Save(v); };
        var name = Theme.Label(v.Name, Theme.Bold, v.On ? Theme.Text : Theme.Faint); name.Location = new Point(70, 10); name.AutoSize = false;
        var sum = Theme.Label(Catalog.Summary(v), Theme.Small, Theme.Dim); sum.Location = new Point(70, 33); sum.AutoSize = false; sum.AutoEllipsis = true;
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.Card };
        var edit = Theme.Button("Change"); edit.Click += (_, _) => app.Edit(v, false, this);
        buttons.Controls.Add(edit);
        if (!v.IsWidget) { var tr = Theme.Button("▶ Try"); tr.Click += (_, _) => app.Engine.Test(v); buttons.Controls.Add(tr); }
        var more = Theme.Button("⋯");
        more.Click += (_, _) =>
        {
            var m = new ContextMenuStrip { Renderer = new DarkMenu(), ShowImageMargin = false };
            m.Items.Add("Make a copy", null, (_, _) => { var c = v.Clone(); c.Id = Vibe.NewId(); c.Name += " (copy)"; c.Place.X += 30; c.Place.Y += 30; app.Save(c); });
            m.Items.Add("Copy share code", null, (_, _) =>
            {
                Actions.SelfCopyUntil = DateTime.Now.AddSeconds(1);
                Clipboard.SetText(Share.Encode(v));
                Cards.Show("Share code copied", "Paste it to a friend. They use \"Paste a share code\" in DeskVibes.", Theme.Accent, 6, "Bottom right", "");
            });
            if (v.IsWidget && v.Place.ClickThrough) m.Items.Add("Make it clickable again", null, (_, _) => { v.Place.ClickThrough = false; app.Save(v); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Delete", null, (_, _) =>
            {
                if (MessageBox.Show(this, $"Delete \"{v.Name}\"?", "DeskVibes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) app.Delete(v);
            });
            m.Show(more, new Point(0, more.Height));
        };
        buttons.Controls.Add(more);
        row.Controls.AddRange(new Control[] { toggle, name, sum, buttons });
        void Lay()
        {
            buttons.Location = new Point(row.Width - buttons.PreferredSize.Width - 10, (row.Height - buttons.PreferredSize.Height) / 2);
            name.Width = sum.Width = Math.Max(100, buttons.Left - 80);
            name.Height = 22; sum.Height = 20;
        }
        row.Resize += (_, _) => Lay();
        Lay();
        return row;
    }

    void PasteCode()
    {
        string text = "";
        try { text = Clipboard.GetText(); } catch { }
        var v = Share.Decode(text);
        if (v == null)
        {
            MessageBox.Show(this, "Copy a DeskVibes share code first (it starts with DV1:), then press this again.", "DeskVibes", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        app.Edit(v, true, this, "From a share code. Check it, then save.");
    }

    // ---------- ideas ----------

    Control IdeasPage()
    {
        var p = Page();
        p.Controls.Add(Theme.Label("Ideas", Theme.Head));
        p.Controls.Add(Theme.Label("Pick one to start from. You can change everything before saving.", null, Theme.Dim));
        foreach (var group in Gallery.Ideas.GroupBy(i => i.Category))
        {
            var h = Theme.Label(group.Key.ToUpperInvariant(), Theme.Small, Theme.Faint); h.Margin = new Padding(2, 16, 0, 4);
            p.Controls.Add(h);
            foreach (var idea in group)
            {
                var sample = idea.Make();
                var row = new Panel { Width = 700, Height = 54, BackColor = Theme.Card, Margin = new Padding(0, 0, 0, 5) };
                var t = Theme.Label(sample.Name, Theme.Bold); t.Location = new Point(14, 7);
                var b = Theme.Label(idea.Blurb, Theme.Small, Theme.Dim); b.Location = new Point(14, 29); b.AutoSize = false; b.AutoEllipsis = true; b.Size = new Size(560, 20);
                var add = Theme.Button("Use this", true);
                add.Click += (_, _) => app.Edit(idea.Make(), true, this);
                row.Controls.AddRange(new Control[] { t, b, add });
                row.Resize += (_, _) => { add.Location = new Point(row.Width - add.Width - 12, 12); b.Width = add.Left - 24; };
                add.Location = new Point(row.Width - add.Width - 12, 12);
                p.Controls.Add(row);
            }
        }
        p.Resize += (_, _) => { foreach (Control c in p.Controls) if (c is Panel) c.Width = Inner; };
        return p;
    }

    // ---------- settings ----------

    Control SettingsPage()
    {
        var s = app.Store.Settings;
        var p = Page();
        p.Controls.Add(Theme.Label("Settings", Theme.Head));

        TextBox Text(string label, string value, Action<string> set, int width = 300, string help = "")
        {
            p.Controls.Add(Lbl(label));
            var t = new TextBox { Text = value, Width = width }; Theme.Style(t);
            t.TextChanged += (_, _) => { set(t.Text.Trim()); app.SaveSettings(); };
            p.Controls.Add(t);
            if (help.Length > 0) p.Controls.Add(Help(help));
            return t;
        }
        CheckBox Check(string label, bool value, Action<bool> set)
        {
            var c = new CheckBox { Text = label, Checked = value, AutoSize = true, Margin = new Padding(3, 8, 3, 2) }; Theme.Style(c);
            c.CheckedChanged += (_, _) => { set(c.Checked); app.SaveSettings(); };
            p.Controls.Add(c);
            return c;
        }

        Head("Where you are");
        Text("Town (for weather)", s.Location, v => s.Location = v, 300, "Like Boston, MA or London, UK");
        var units = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 }; Theme.Style(units);
        units.Items.AddRange(new object[] { "°F", "°C" }); units.SelectedIndex = s.Units == "C" ? 1 : 0;
        units.SelectedIndexChanged += (_, _) => { s.Units = units.SelectedIndex == 1 ? "C" : "F"; app.SaveSettings(); };
        p.Controls.Add(Lbl("Temperature")); p.Controls.Add(units);
        Check("24-hour clock", s.Clock24, v => s.Clock24 = v);
        Text("Work hours start", s.WorkFrom, v => s.WorkFrom = v, 100);
        Text("Work hours end", s.WorkTo, v => s.WorkTo = v, 100);

        Head("Email");
        p.Controls.Add(Help("DeskVibes only reads who sent new emails and their subjects. It never marks them read or sends anything.\n" +
            "Gmail: turn on 2-Step Verification, then make an \"app password\" at myaccount.google.com/apppasswords and use it here.\n" +
            "Outlook.com: server outlook.office365.com. Yahoo: imap.mail.yahoo.com (also needs an app password)."));
        Text("Email server (IMAP)", s.ImapServer, v => { s.ImapServer = v; app.Mail.Reset(); }, 300, "e.g. imap.gmail.com");
        Text("Port", s.ImapPort.ToString(), v => { if (int.TryParse(v, out var n)) s.ImapPort = n; }, 80);
        Text("Email address", s.ImapUser, v => { s.ImapUser = v; app.Mail.Reset(); });
        p.Controls.Add(Lbl("Password or app password"));
        var pw = new TextBox { Width = 300, UseSystemPasswordChar = true, PlaceholderText = s.ImapPasswordProtected.Length > 0 ? "(saved — type to change)" : "" }; Theme.Style(pw);
        pw.TextChanged += (_, _) => { s.ImapPasswordProtected = Secret.Protect(pw.Text); app.SaveSettings(); app.Mail.Reset(); };
        p.Controls.Add(pw);
        p.Controls.Add(Help("Saved encrypted, so only your Windows account can read it."));
        var testRow = new FlowLayoutPanel { AutoSize = true };
        var test = Theme.Button("Test email");
        var result = Theme.Label("", Theme.Small, Theme.Dim); result.Margin = new Padding(6, 10, 0, 0);
        test.Click += async (_, _) =>
        {
            result.Text = "Checking…";
            result.Text = await MailHub.TestImap(s.ImapServer, s.ImapPort, s.ImapUser, Secret.Unprotect(s.ImapPasswordProtected));
        };
        testRow.Controls.Add(test); testRow.Controls.Add(result);
        p.Controls.Add(testRow);
        Text("Check every (minutes)", s.MailEveryMin.ToString(), v => { if (int.TryParse(v, out var n)) s.MailEveryMin = Math.Max(1, n); }, 80);
        Check("Also use classic Outlook for Windows (email and meetings, while Outlook is open)", s.UseOutlook, v => { s.UseOutlook = v; app.Mail.Reset(); });

        Head("World of Warcraft realms");
        p.Controls.Add(Help("Make a free key at develop.battle.net/access/clients (any name, no redirect needed), then paste the Client ID and Secret here."));
        Text("Blizzard Client ID", s.BlizzardId, v => { s.BlizzardId = v; app.Feeds.ForgetToken(); });
        p.Controls.Add(Lbl("Blizzard Client Secret"));
        var sec = new TextBox { Width = 300, UseSystemPasswordChar = true, PlaceholderText = s.BlizzardSecretProtected.Length > 0 ? "(saved — type to change)" : "" }; Theme.Style(sec);
        sec.TextChanged += (_, _) => { s.BlizzardSecretProtected = Secret.Protect(sec.Text.Trim()); app.Feeds.ForgetToken(); app.SaveSettings(); };
        p.Controls.Add(sec);

        Head("Smarter \"Describe it\" (optional)");
        p.Controls.Add(Help("Paste your own Anthropic API key and the Describe box uses Claude AI, so it understands much freer wording. " +
            "Make a key at console.anthropic.com (Settings → API keys). Each sentence costs a few cents on your Anthropic account.\n" +
            "Only the sentence you type is sent. Leave this empty to use the built-in matcher, which works offline."));
        p.Controls.Add(Lbl("Anthropic API key"));
        var ai = new TextBox { Width = 300, UseSystemPasswordChar = true, PlaceholderText = s.AnthropicKeyProtected.Length > 0 ? "(saved — type to change)" : "" }; Theme.Style(ai);
        ai.TextChanged += (_, _) => { s.AnthropicKeyProtected = Secret.Protect(ai.Text.Trim()); app.SaveSettings(); };
        p.Controls.Add(ai);
        p.Controls.Add(Help("Saved encrypted, so only your Windows account can read it."));
        var aiRow = new FlowLayoutPanel { AutoSize = true };
        var aiTest = Theme.Button("Test key");
        var aiResult = Theme.Label("", Theme.Small, Theme.Dim); aiResult.Margin = new Padding(6, 10, 0, 0);
        aiTest.Click += async (_, _) =>
        {
            aiResult.Text = "Checking…";
            aiResult.Text = await AiDescribe.TestKey(Secret.Unprotect(s.AnthropicKeyProtected));
        };
        var aiClear = Theme.Button("Forget key");
        aiClear.Click += (_, _) => { ai.Clear(); s.AnthropicKeyProtected = ""; ai.PlaceholderText = ""; app.SaveSettings(); aiResult.Text = "Forgotten. Using the built-in matcher."; };
        aiRow.Controls.Add(aiTest); aiRow.Controls.Add(aiClear); aiRow.Controls.Add(aiResult);
        p.Controls.Add(aiRow);

        Head("Peace and quiet");
        Check("Quiet hours (no pop-ups, edges or sounds)", s.QuietHours, v => s.QuietHours = v);
        Text("From", s.QuietFrom, v => s.QuietFrom = v, 100);
        Text("To", s.QuietTo, v => s.QuietTo = v, 100);

        Head("Starting up");
        Check("Start DeskVibes when I sign in to Windows", s.StartWithWindows, v => { s.StartWithWindows = v; SetStartup(v); });
        p.Controls.Add(Help("Your vibes and settings are saved in " + app.Store.Dir));
        return p;

        void Head(string t) { var l = Theme.Label(t, Theme.Title); l.Margin = new Padding(0, 20, 0, 2); p.Controls.Add(l); }
        static Label Lbl(string t) { var l = Theme.Label(t, Theme.Small, Theme.Dim); l.Margin = new Padding(3, 8, 3, 1); return l; }
        static Label Help(string t) { var l = Theme.Label(t, Theme.Small, Theme.Faint); l.MaximumSize = new Size(640, 0); return l; }
    }

    public static void SetStartup(bool on)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (key == null) return;
        if (on) key.SetValue("DeskVibes", $"\"{Environment.ProcessPath}\" --tray");
        else key.DeleteValue("DeskVibes", false);
    }

    // ---------- log ----------

    Control LogPage()
    {
        var panel = new Panel { Padding = new Padding(26, 20, 26, 20), BackColor = Theme.Back };
        logBox = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Panel, ForeColor = Theme.Dim, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.5f) };
        logBox.Text = string.Join(Environment.NewLine, app.Engine.Log) + Environment.NewLine;
        var head = Theme.Label("What happened", Theme.Head); head.Dock = DockStyle.Top;
        var sub = Theme.Label("Every time a vibe fires (or would have, but was held back) it shows up here.", null, Theme.Dim); sub.Dock = DockStyle.Top;
        panel.Controls.Add(logBox); panel.Controls.Add(sub); panel.Controls.Add(head);
        return panel;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // closing the window keeps DeskVibes running in the tray
        if (e.CloseReason == CloseReason.UserClosing && !app.Quitting)
        {
            e.Cancel = true;
            Hide();
            app.HintTray();
            return;
        }
        base.OnFormClosing(e);
    }
}

/// <summary>An on/off switch.</summary>
public sealed class Toggle : Control
{
    bool on;
    public event Action? Changed;
    public bool On { get => on; set { on = value; Invalidate(); } }
    public Toggle()
    {
        Size = new Size(42, 22); Cursor = Cursors.Hand; DoubleBuffered = true;
        SetStyle(ControlStyles.SupportsTransparentBackColor, true); BackColor = Color.Transparent;
    }
    protected override void OnClick(EventArgs e) { on = !on; Invalidate(); Changed?.Invoke(); base.OnClick(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = Theme.Round(r, r.Height / 2);
        using (var b = new SolidBrush(on ? Theme.Accent : Theme.Raised)) g.FillPath(b, path);
        float d = Height - 6;
        using (var b = new SolidBrush(on ? Color.White : Theme.Faint)) g.FillEllipse(b, on ? Width - d - 4 : 3, 3, d, d);
    }
}

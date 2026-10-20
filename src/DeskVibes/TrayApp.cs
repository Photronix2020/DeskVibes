namespace DeskVibes;

/// <summary>The running app: tray icon, the once-a-second loop, widgets, and the main window.</summary>
public sealed class TrayApp : ApplicationContext
{
    public Store Store { get; }
    public Engine Engine { get; }
    public Feeds Feeds { get; }
    public MailHub Mail { get; }
    public bool Quitting { get; private set; }
    public event Action? VibesChanged;

    readonly RealWorld world = new();
    readonly NotifyIcon tray;
    readonly Watchers watchers;
    readonly System.Windows.Forms.Timer tick = new() { Interval = 1000 };
    readonly Dictionary<string, (Form form, string sig)> widgets = new();
    readonly SynchronizationContext ui;
    readonly Icon icon = Theme.MakeIcon(), pausedIcon = Theme.MakeIcon(32, true);
    MainForm? main;
    bool hinted;

    public TrayApp(Store store, bool showWindow)
    {
        Store = store;
        ui = new WindowsFormsSynchronizationContext();
        Feeds = new Feeds(() => Store.Settings);
        Mail = new MailHub(() => Store.Settings);
        Engine = new Engine(world, Feeds, Mail, () => Store.Settings, () => Store.Vibes) { RunAction = Actions.Run };
        Actions.Engine = Engine;
        watchers = new Watchers((type, ctx) => Engine.Push(type, ctx));
        Mail.NewMail += m => ui.Post(_ => Engine.Push("mail.new", Ctx.Of(("from", m.From), ("subject", m.Subject), ("info", Mime.ShortName(m.From)))), null);

        if (Store.FirstRun) { foreach (var v in Gallery.Starter()) Store.Vibes.Add(v); Store.Save(); }

        tray = new NotifyIcon { Icon = icon, Text = "DeskVibes", Visible = true, ContextMenuStrip = new ContextMenuStrip { Renderer = new DarkMenu(), ShowImageMargin = false } };
        tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
        UpdateTrayIcon();

        tick.Tick += (_, _) => OnTick();
        tick.Start();
        world.Refresh();
        Sync();
        Engine.Note("DeskVibes started");
        if (showWindow || Store.FirstRun) ShowMain();
    }

    /// <summary>Runs something on the UI thread.</summary>
    public void Post(Action a) => ui.Post(_ => a(), null);

    void OnTick()
    {
        try { world.Refresh(); } catch (Exception ex) { Engine.Note("Couldn't read the PC state: " + ex.Message); }
        Engine.Tick();
        foreach (var (form, _) in widgets.Values) if (form.Visible && form is WidgetForm) form.Invalidate();
    }

    /// <summary>Brings widgets and hotkeys in line with the saved vibes.</summary>
    public void Sync()
    {
        var failed = watchers.SetHotkeys(Store.Vibes.Where(v => v.On && !v.IsWidget && v.When.Type == "pc.hotkey").Select(v => v.When.Get("keys")));
        foreach (var f in failed) Engine.Note($"The hotkey {f} is taken by another app (or isn't a key I know)");

        var want = Store.Vibes.Where(v => v.On && v.IsWidget && v.Widget != null).ToDictionary(v => v.Id);
        foreach (var id in widgets.Keys.Where(k => !want.ContainsKey(k)).ToList())
        {
            widgets[id].form.Close();
            widgets.Remove(id);
        }
        foreach (var v in want.Values)
        {
            bool deck = v.Widget!.Type == "w.deck";
            // a deck's buttons change often; only its shape needs a new window
            var sig = deck ? "deck|" + Deck.Shape(v.Widget) : Json.Write(v.Widget) + v.Place.ClickThrough;
            if (widgets.TryGetValue(v.Id, out var w))
            {
                if (w.sig == sig)
                {
                    if (w.form is WidgetForm wf) wf.Update(v); else if (w.form is DeckForm df) df.Update(v);
                    continue;
                }
                w.form.Close(); widgets.Remove(v.Id);
            }
            if (v.Place.X < 0 || v.Place.Y < 0) FindSpot(v);
            Form form = deck ? new DeckForm(v, SaveQuietly, x => Edit(x, false, null))
                             : new WidgetForm(v, new WidgetContent(v, Engine, () => Store.Settings), SaveQuietly, x => Edit(x, false, null));
            widgets[v.Id] = (form, sig);
            form.Show();
        }
        VibesChanged?.Invoke();
    }

    /// <summary>New widgets go down the right side of the main screen, under the ones already there.</summary>
    void FindSpot(Vibe v)
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        int w = v.Place.W > 0 ? v.Place.W : 260;
        int x = area.Right - w - 24, y = area.Top + 24;
        foreach (var (form, _) in widgets.Values)
            if (form.Right > area.Right - 420 && form.Top < area.Bottom - 200 && area.Contains(form.Location)) y = Math.Max(y, form.Bottom + 12);
        if (y > area.Bottom - 150) y = area.Top + 24;
        v.Place.X = x; v.Place.Y = y;
        Store.Save();
    }

    /// <summary>Saves a vibe and applies it.</summary>
    public void Save(Vibe v)
    {
        Store.Upsert(v);
        Sync();
    }

    /// <summary>Widgets save their position often; no need to rebuild everything for that.</summary>
    void SaveQuietly(Vibe v)
    {
        var known = Store.Get(v.Id);
        if (known == null) return;
        bool changed = known.On != v.On || known.Place.ClickThrough != v.Place.ClickThrough;
        Store.Upsert(v);
        if (changed) Sync();
    }

    public void Delete(Vibe v) { Store.Remove(v.Id); Sync(); }

    public void SaveSettings() { Store.Save(); UpdateTrayIcon(); }

    /// <summary>Opens the builder; returns true if it was saved.</summary>
    public bool Edit(Vibe v, bool isNew, IWin32Window? owner, string notes = "")
    {
        using var f = new BuilderForm(v, Engine, isNew, notes);
        if ((owner == null ? f.ShowDialog() : f.ShowDialog(owner)) != DialogResult.OK) return false;
        var r = f.Result;
        r.On = isNew || v.On;
        Save(r);
        Engine.Note($"Saved \"{r.Name}\"");
        return true;
    }

    public void ShowMain(string page = "vibes")
    {
        if (main == null || main.IsDisposed) main = new MainForm(this);
        main.Show(page);
        main.Show();
        if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
        main.Activate();
    }

    public void HintTray()
    {
        if (hinted) return;
        hinted = true;
        Cards.Show("DeskVibes is still running", "It lives in the tray (by the clock). Right-click the icon for options, or Quit.", Theme.Accent, 7, "Bottom right", "");
    }

    void UpdateTrayIcon()
    {
        tray.Icon = Store.Settings.Paused ? pausedIcon : icon;
        tray.Text = Store.Settings.Paused ? "DeskVibes (paused)" : "DeskVibes";
    }

    void BuildMenu()
    {
        var m = tray.ContextMenuStrip!;
        m.Items.Clear();
        m.Items.Add("Open DeskVibes", null, (_, _) => ShowMain());
        m.Items.Add("Ideas…", null, (_, _) => ShowMain("ideas"));
        m.Items.Add(new ToolStripSeparator());
        if (Overlays.EdgesShowing) m.Items.Add("Clear the alerts", null, (_, _) => Overlays.CloseAll());
        var pause = new ToolStripMenuItem("Pause everything") { Checked = Store.Settings.Paused };
        pause.Click += (_, _) => { Store.Settings.Paused = !Store.Settings.Paused; SaveSettings(); Engine.Note(Store.Settings.Paused ? "Paused" : "Running again"); };
        m.Items.Add(pause);
        var ws = Store.Vibes.Where(v => v.IsWidget).ToList();
        if (ws.Count > 0)
        {
            var sub = new ToolStripMenuItem("Widgets");
            foreach (var v in ws)
            {
                var i = new ToolStripMenuItem(v.Name) { Checked = v.On };
                i.Click += (_, _) => { v.On = !v.On; Save(v); };
                sub.DropDownItems.Add(i);
                if (v.On && v.Place.ClickThrough)
                {
                    var c = new ToolStripMenuItem("   make " + v.Name + " clickable again");
                    c.Click += (_, _) => { v.Place.ClickThrough = false; Save(v); };
                    sub.DropDownItems.Add(c);
                }
            }
            m.Items.Add(sub);
        }
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Quit", null, (_, _) => Quit());
    }

    public void Quit()
    {
        Quitting = true;
        tick.Stop();
        Store.Save();
        Overlays.CloseAll();
        foreach (var (form, _) in widgets.Values) form.Close();
        widgets.Clear();
        tray.Visible = false;
        tray.Dispose();
        watchers.Dispose();
        Mail.Dispose();
        main?.Close();
        ExitThread();
    }
}

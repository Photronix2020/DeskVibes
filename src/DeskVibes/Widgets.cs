using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace DeskVibes;

/// <summary>One line of a widget.</summary>
public sealed class WLine
{
    public string Text = "";
    public string Right = "";
    public Color? Color;
    public Color? RightColor;
    public Color? Dot;
    public float Size = 1f;
    public bool Bold;
    public bool Light;
    public double? Bar;
    public Color? BarColor;
    public Action? Click;

    public WLine() { }
    public WLine(string text, Color? color = null, float size = 1f) { Text = text; Color = color; Size = size; }
}

/// <summary>What a widget shows. Each widget vibe gets its own instance (timers and history live here).</summary>
public sealed class WidgetContent
{
    readonly Vibe vibe;
    readonly Engine engine;
    readonly Func<Settings> settings;
    Step S => vibe.Widget!;

    public WidgetContent(Vibe vibe, Engine engine, Func<Settings> settings)
    {
        this.vibe = vibe; this.engine = engine; this.settings = settings;
    }

    public string Title => vibe.Name;

    // focus timer state
    string mode = "ready"; DateTime endsAt; TimeSpan left;

    public List<WLine> Lines()
    {
        var w = engine.World; var feeds = engine.Feeds; var now = w.Now;
        var L = new List<WLine>();
        var s = settings();
        switch (S.Type)
        {
            case "w.clock":
            {
                string fmt = (s.Clock24 ? "HH:mm" : "h:mm") + (S.Bool("seconds") ? ":ss" : "");
                L.Add(new WLine(now.ToString(fmt, CultureInfo.CurrentCulture), Theme.Text, 2.6f) { Light = true, Right = s.Clock24 ? "" : now.ToString("tt") });
                if (S.Bool("date")) L.Add(new WLine(now.ToString("dddd, MMMM d", CultureInfo.CurrentCulture), Theme.Dim));
                var zone = S.Get("zone").Trim();
                if (zone.Length > 0)
                {
                    var tz = FindZone(zone);
                    if (tz == null) L.Add(new WLine($"Don't know the time zone \"{zone}\"", Theme.Faint));
                    else
                    {
                        var t = TimeZoneInfo.ConvertTime(now, tz);
                        var day = t.Date == now.Date ? "" : t.Date > now.Date ? " (tomorrow)" : " (yesterday)";
                        L.Add(new WLine(zone, Theme.Dim) { Right = t.ToString(s.Clock24 ? "HH:mm" : "h:mm tt") + day, RightColor = Theme.Text });
                    }
                }
                break;
            }
            case "w.weather":
            {
                var wx = feeds.Weather(S.Get("place"));
                var place = S.Get("place").Trim().Length > 0 ? S.Get("place") : s.Location;
                if (place.Trim().Length == 0) { L.Add(new WLine("Set your town in Settings", Theme.Dim)); break; }
                if (wx == null)
                {
                    var p = feeds.Problem("weather:" + place.Trim().ToLowerInvariant() + ":" + (s.Units == "C" ? "C" : "F"));
                    L.Add(new WLine(p ?? "Getting the weather…", Theme.Faint)); break;
                }
                L.Add(new WLine($"{wx.Icon}  {Math.Round(wx.Temp)}°{wx.Units}", Theme.Text, 2.3f) { Light = true });
                L.Add(new WLine($"{wx.Text} · high {Math.Round(wx.High)}° low {Math.Round(wx.Low)}°", Theme.Dim));
                var rain = wx.Hours.Take(12).FirstOrDefault(h => h.RainChance >= 50 && Feeds.IsWet(h.Code));
                L.Add(rain == null ? new WLine("No rain in the next 12 hours", Theme.Faint)
                    : new WLine($"{(Feeds.IsSnow(rain.Code) ? "Snow" : "Rain")} around {rain.Time:h tt} ({rain.RainChance}%)", Theme.Warn));
                L.Add(new WLine(wx.Place, Theme.Faint) { Right = $"wind {Math.Round(wx.Wind)} {(wx.Units == "F" ? "mph" : "km/h")}", RightColor = Theme.Faint });
                break;
            }
            case "w.tickers":
            {
                foreach (var sym in Split(S.Get("symbols")))
                {
                    var q = feeds.Quote(sym);
                    if (q == null) { L.Add(new WLine(sym.ToUpperInvariant(), Theme.Dim) { Right = feeds.Problem("quote:" + sym.ToUpperInvariant()) ?? "…", RightColor = Theme.Faint }); continue; }
                    var up = q.ChangePct >= 0;
                    L.Add(new WLine(q.Symbol, Theme.Text) { Bold = true,
                        Right = $"{Price(q.Price)}   {(up ? "▲" : "▼")} {Math.Abs(q.ChangePct):0.00}%", RightColor = up ? Theme.Good : Theme.Bad });
                }
                if (L.Count == 0) L.Add(new WLine("Add some symbols", Theme.Faint));
                break;
            }
            case "w.system":
            {
                L.Add(new WLine("CPU", Theme.Dim) { Right = $"{w.Cpu:0}%", Bar = w.Cpu / 100 });
                L.Add(new WLine("Memory", Theme.Dim) { Right = $"{w.Memory:0}%", Bar = w.Memory / 100 });
                if (w.Battery != null) L.Add(new WLine("Battery" + (w.Plugged == true ? " (charging)" : ""), Theme.Dim) { Right = $"{w.Battery}%", Bar = w.Battery / 100.0 });
                var free = w.FreeGb("C");
                if (free >= 0) L.Add(new WLine("Disk C free", Theme.Dim) { Right = $"{free:0} GB", RightColor = free < 10 ? Theme.Bad : null });
                var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
                L.Add(new WLine("Up for", Theme.Dim) { Right = up.TotalDays >= 1 ? $"{(int)up.TotalDays}d {up.Hours}h" : $"{up.Hours}h {up.Minutes}m" });
                L.Add(new WLine("Internet", Theme.Dim) { Right = w.Online ? "online" : "offline", RightColor = w.Online ? Theme.Good : Theme.Bad });
                break;
            }
            case "w.countdown":
            {
                var target = NextTime(S.Get("target"), now);
                L.Add(new WLine(S.Get("title"), Theme.Dim));
                if (target == null) { L.Add(new WLine("Can't read the date", Theme.Faint)); break; }
                var left = target.Value - now;
                if (left <= TimeSpan.Zero) { L.Add(new WLine("It's here! 🎉", Theme.Good, 2f)); break; }
                string big = left.TotalDays >= 1 ? $"{(int)left.TotalDays}d {left.Hours}h {left.Minutes}m"
                    : left.TotalHours >= 1 ? $"{(int)left.TotalHours}h {left.Minutes}m {left.Seconds}s" : $"{left.Minutes}m {left.Seconds:00}s";
                L.Add(new WLine(big, Theme.Text, 2.1f) { Light = true });
                L.Add(new WLine(target.Value.ToString("dddd, MMM d · h:mm tt", CultureInfo.CurrentCulture), Theme.Faint));
                break;
            }
            case "w.focus":
                FocusLines(L, now);
                break;
            case "w.status":
            {
                foreach (var raw in S.Get("items").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0))
                {
                    if (raw.StartsWith("wow:", StringComparison.OrdinalIgnoreCase))
                    {
                        var p = raw.Split(':');
                        string region = p.Length > 2 ? p[1] : "us", realm = p.Length > 2 ? p[2] : p.ElementAtOrDefault(1) ?? "", game = p.Length > 3 ? p[3] : "Retail";
                        var r = feeds.Realm(region, game, realm);
                        var prob = feeds.Problem($"realm:{region.ToLowerInvariant()}:{Feeds.NamespaceFor(game, region.ToLowerInvariant())}:{Feeds.Slug(realm)}");
                        L.Add(new WLine(realm + (game.Equals("retail", StringComparison.OrdinalIgnoreCase) ? "" : $" ({game})"), Theme.Text)
                        {
                            Dot = r == null ? Theme.Faint : r.Up ? Theme.Good : Theme.Bad,
                            Right = r == null ? (prob == null ? "…" : prob.Contains("Blizzard") ? "needs a key" : prob) : r.Up ? (r.Population.Length > 0 ? r.Population : "up") : "down",
                            RightColor = r == null ? Theme.Faint : null,
                        });
                    }
                    else
                    {
                        var site = feeds.Site(raw);
                        var name = raw.Replace("https://", "").Replace("http://", "").TrimEnd('/');
                        L.Add(new WLine(name, Theme.Text)
                        {
                            Dot = site == null ? Theme.Faint : site.Up ? Theme.Good : Theme.Bad,
                            Right = site?.Status ?? "…", RightColor = site == null || site.Up ? Theme.Faint : Theme.Bad,
                        });
                    }
                }
                if (L.Count == 0) L.Add(new WLine("Add websites or realms", Theme.Faint));
                break;
            }
            case "w.clipboard":
            {
                var items = Watchers.ClipHistory.Take((int)Math.Clamp(S.Num("count", 8), 1, 30)).ToList();
                if (items.Count == 0) L.Add(new WLine("Copy something and it shows up here", Theme.Faint));
                foreach (var t in items)
                {
                    var one = t.Replace("\r", " ").Replace("\n", " ").Trim();
                    if (one.Length > 60) one = one[..58] + "…";
                    var copy = t;
                    L.Add(new WLine(one, Theme.Text) { Click = () => { Actions.SelfCopyUntil = DateTime.Now.AddSeconds(1); Clipboard.SetText(copy); } });
                }
                break;
            }
            case "w.screentime":
            {
                var u = w.UsageToday.OrderByDescending(kv => kv.Value).ToList();
                var total = u.Sum(kv => kv.Value);
                L.Add(new WLine(Hm(total), Theme.Text, 2f) { Light = true, Right = "today", RightColor = Theme.Faint });
                double top = u.Count > 0 ? u[0].Value : 1;
                foreach (var kv in u.Take((int)Math.Clamp(S.Num("count", 5), 1, 15)))
                    L.Add(new WLine(kv.Key, Theme.Dim) { Right = Hm(kv.Value), Bar = kv.Value / top, BarColor = Theme.Accent });
                break;
            }
            case "w.mail":
            {
                var m = engine.Mail;
                if (!m.Configured) { L.Add(new WLine("Set up email in Settings", Theme.Dim)); break; }
                L.Add(new WLine($"{m.Unread}", m.Unread > 0 ? Theme.Text : Theme.Dim, 2.2f) { Light = true, Right = "unread", RightColor = Theme.Faint });
                if (m.Problem != null) L.Add(new WLine(m.Problem, Theme.Bad));
                foreach (var it in m.Recent.Take(5))
                    L.Add(new WLine(Mime.ShortName(it.From), Theme.Text) { Bold = true, Right = Cut(it.Subject, 34), RightColor = Theme.Dim });
                break;
            }
            case "w.meetings":
            {
                if (!s.UseOutlook) { L.Add(new WLine("Turn on Outlook in Settings", Theme.Dim)); break; }
                var list = engine.Mail.Meetings;
                if (list.Count == 0) L.Add(new WLine("Nothing on the calendar today", Theme.Faint));
                bool nextMarked = false;
                foreach (var m in list)
                {
                    bool past = m.End < now, next = !past && !nextMarked;
                    if (next) nextMarked = true;
                    L.Add(new WLine(m.Subject, past ? Theme.Faint : Theme.Text) { Bold = next, Right = m.Start.ToString("h:mm tt"), RightColor = next ? Theme.Warn : Theme.Faint });
                }
                break;
            }
            case "w.news":
            {
                var items = feeds.News(S.Get("url"));
                if (items == null) { L.Add(new WLine(feeds.Problem("news:" + Feeds.NormalizeUrl(S.Get("url"))) ?? "Getting headlines…", Theme.Faint)); break; }
                foreach (var it in items.Take((int)Math.Clamp(S.Num("count", 5), 1, 20)))
                {
                    var link = it.Link;
                    L.Add(new WLine("• " + Cut(it.Title, 90), Theme.Text) { Click = () => Actions.OpenThing(link) });
                }
                break;
            }
            case "w.text":
                L.Add(new WLine(new Ctx().Fill(S.Get("text"), now), Theme.Text, (float)Math.Clamp(S.Num("size", 16) / 10.0, 0.7, 6)));
                break;
            case "w.note":
            case "w.deck": // drawn by DeckForm
                break;
            default:
                L.Add(new WLine("Unknown widget " + S.Type, Theme.Bad));
                break;
        }
        return L;
    }

    void FocusLines(List<WLine> L, DateTime now)
    {
        var work = TimeSpan.FromMinutes(Math.Clamp(S.Num("work", 25), 1, 240));
        var rest = TimeSpan.FromMinutes(Math.Clamp(S.Num("rest", 5), 1, 120));
        if (mode is "work" or "rest" && now >= endsAt)
        {
            if (mode == "work")
            {
                Overlays.Edges(S.Color("color", Theme.Good), "Solid", 22, 5);
                Actions.PlaySound("Chime", "", 1);
                Cards.Show(vibe.Name, $"Nice work! Take {rest.TotalMinutes:0} minutes.", S.Color("color", Theme.Good), 10, "Bottom right", "");
                mode = "rest"; endsAt = now + rest;
            }
            else
            {
                Actions.PlaySound("Ding", "", 1);
                Cards.Show(vibe.Name, "Break's over. Click the timer when you're ready.", Theme.Accent, 10, "Bottom right", "");
                mode = "ready";
            }
        }
        TimeSpan show = mode switch { "work" or "rest" => endsAt - now, "paused" => left, _ => work };
        if (show < TimeSpan.Zero) show = TimeSpan.Zero;
        var color = mode == "rest" ? Theme.Good : mode == "work" ? Theme.Text : Theme.Dim;
        Action toggle = () =>
        {
            var n = DateTime.Now;
            switch (mode)
            {
                case "ready": mode = "work"; endsAt = n + work; break;
                case "work": case "rest": left = endsAt - n; mode = "paused" + mode; break;
                default: mode = mode.Replace("paused", ""); if (mode is not ("work" or "rest")) mode = "work"; endsAt = n + left; break;
            }
        };
        if (mode.StartsWith("paused")) show = left;
        L.Add(new WLine($"{(int)show.TotalMinutes:00}:{show.Seconds:00}", color, 2.6f) { Light = true, Click = toggle });
        string hint = mode switch
        {
            "work" => "Focusing · click to pause", "rest" => "Break time · click to pause",
            "ready" => "Click to start focusing", _ => "Paused · click to go on",
        };
        L.Add(new WLine(hint, Theme.Faint) { Click = toggle });
        if (mode != "ready")
            L.Add(new WLine("Reset", Theme.Faint) { Click = () => mode = "ready" });
    }

    static IEnumerable<string> Split(string s) => s.Split(new[] { ',', ' ', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    static string Cut(string s, int n) => s.Length > n ? s[..(n - 1)] + "…" : s;
    static string Hm(double secs) { var t = TimeSpan.FromSeconds(secs); return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m"; }
    static string Price(double p) => p >= 1000 ? p.ToString("#,0.00", CultureInfo.CurrentCulture) : p >= 1 ? p.ToString("0.00", CultureInfo.CurrentCulture) : p.ToString("0.####", CultureInfo.CurrentCulture);

    static readonly Dictionary<string, string> zones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pacific"] = "Pacific Standard Time", ["la"] = "Pacific Standard Time", ["los angeles"] = "Pacific Standard Time", ["seattle"] = "Pacific Standard Time",
        ["mountain"] = "Mountain Standard Time", ["denver"] = "Mountain Standard Time", ["arizona"] = "US Mountain Standard Time",
        ["central"] = "Central Standard Time", ["chicago"] = "Central Standard Time", ["texas"] = "Central Standard Time",
        ["eastern"] = "Eastern Standard Time", ["new york"] = "Eastern Standard Time", ["nyc"] = "Eastern Standard Time", ["boston"] = "Eastern Standard Time",
        ["hawaii"] = "Hawaiian Standard Time", ["alaska"] = "Alaskan Standard Time",
        ["london"] = "GMT Standard Time", ["uk"] = "GMT Standard Time", ["dublin"] = "GMT Standard Time",
        ["paris"] = "Romance Standard Time", ["berlin"] = "W. Europe Standard Time", ["amsterdam"] = "W. Europe Standard Time", ["rome"] = "W. Europe Standard Time",
        ["madrid"] = "Romance Standard Time", ["athens"] = "GTB Standard Time", ["moscow"] = "Russian Standard Time",
        ["dubai"] = "Arabian Standard Time", ["india"] = "India Standard Time", ["mumbai"] = "India Standard Time",
        ["china"] = "China Standard Time", ["beijing"] = "China Standard Time", ["shanghai"] = "China Standard Time", ["hong kong"] = "China Standard Time",
        ["singapore"] = "Singapore Standard Time", ["tokyo"] = "Tokyo Standard Time", ["japan"] = "Tokyo Standard Time", ["seoul"] = "Korea Standard Time",
        ["sydney"] = "AUS Eastern Standard Time", ["melbourne"] = "AUS Eastern Standard Time", ["auckland"] = "New Zealand Standard Time",
        ["sao paulo"] = "E. South America Standard Time", ["brazil"] = "E. South America Standard Time", ["mexico city"] = "Central Standard Time (Mexico)",
        ["utc"] = "UTC", ["gmt"] = "UTC",
    };

    public static TimeZoneInfo? FindZone(string name)
    {
        name = name.Trim();
        try { if (zones.TryGetValue(name, out var id)) return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        try { return TimeZoneInfo.FindSystemTimeZoneById(name); } catch { }
        return TimeZoneInfo.GetSystemTimeZones().FirstOrDefault(z => z.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase)
            || z.StandardName.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>"2026-12-25 08:00", "Dec 25", "Friday 17:00" (the next Friday) or "17:00" (today or tomorrow).</summary>
    public static DateTime? NextTime(string text, DateTime now)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return null;
        var parts = text.Split(' ', 2, StringSplitOptions.TrimEntries);
        if (Enum.TryParse<DayOfWeek>(parts[0], true, out var dow) && !int.TryParse(parts[0], out _))
        {
            var t = parts.Length > 1 && Engine.TryTime(parts[1], out var tt) ? tt : TimeSpan.Zero;
            int add = ((int)dow - (int)now.DayOfWeek + 7) % 7;
            var d = now.Date.AddDays(add) + t;
            if (d <= now && add == 0) d = d.AddDays(7);
            return d;
        }
        if (Engine.TryTime(text, out var only))
        {
            var d = now.Date + only;
            return d > now ? d : d.AddDays(1);
        }
        string[] fmts = { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd", "M/d/yyyy h:mm tt", "M/d/yyyy H:mm", "M/d/yyyy", "MMM d yyyy", "MMM d", "MMMM d", "MMM d h:mm tt", "MM-dd" };
        if (DateTime.TryParseExact(text, fmts, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var x))
        {
            if (!text.Contains(x.Year.ToString()) && x < now) x = x.AddYears(1);
            return x;
        }
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out x)) return x;
        return null;
    }
}

/// <summary>A widget on the desktop: drag it to move, drag the corner to resize, right-click for options.</summary>
public sealed class WidgetForm : Form
{
    public Vibe Vibe { get; private set; }
    readonly WidgetContent content;
    readonly Action<Vibe> saved;
    readonly Action<Vibe> edit;
    TextBox? note;
    List<(RectangleF rect, Action click)> hits = new();
    readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 800 };

    public WidgetForm(Vibe v, WidgetContent content, Action<Vibe> saved, Action<Vibe> edit)
    {
        Vibe = v; this.content = content; this.saved = saved; this.edit = edit;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        MaximizeBox = false; MinimizeBox = false; DoubleBuffered = true; BackColor = Theme.Card;
        Text = v.Name;
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); saved(Vibe); };
        ApplyPlace();
        if (v.Widget?.Type == "w.note") MakeNote();
    }

    int S(int px) => (int)(px * DeviceDpi / 96.0);

    void MakeNote()
    {
        var paper = Vibe.Widget!.Color("color", Theme.Warn);
        BackColor = paper;
        note = new TextBox
        {
            Multiline = true, BorderStyle = BorderStyle.None, BackColor = paper, ForeColor = Overlays.IsLight(paper) ? Color.FromArgb(30, 30, 30) : Color.White,
            Font = new Font("Segoe Print", 11f), Text = Vibe.Place.Note, ScrollBars = ScrollBars.None, AcceptsReturn = true,
        };
        note.TextChanged += (_, _) => { Vibe.Place.Note = note.Text; saveTimer.Stop(); saveTimer.Start(); };
        Controls.Add(note);
        LayoutNote();
    }

    void LayoutNote()
    {
        if (note == null) return;
        note.Bounds = new Rectangle(S(12), S(30), Width - S(24), Height - S(42));
    }

    public void Update(Vibe v)
    {
        Vibe = v;
        Text = v.Name;
        ApplyPlace();
        if (note != null)
        {
            var paper = v.Widget!.Color("color", Theme.Warn);
            BackColor = note.BackColor = paper;
            note.ForeColor = Overlays.IsLight(paper) ? Color.FromArgb(30, 30, 30) : Color.White;
        }
        Invalidate();
    }

    void ApplyPlace()
    {
        var p = Vibe.Place;
        var area = Screen.PrimaryScreen!.WorkingArea;
        int w = Math.Max(S(160), p.W > 0 ? p.W : S(260));
        if (p.X < 0 || p.Y < 0 || !Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(p.X, p.Y, w, S(60)))))
        {
            p.X = area.Right - w - S(24); if (p.Y < 0 || p.Y > area.Bottom - S(60)) p.Y = area.Top + S(24);
        }
        Bounds = new Rectangle(p.X, p.Y, w, Vibe.Widget?.Type == "w.note" ? Math.Max(S(120), p.H) : Height < S(60) ? S(140) : Height);
        Opacity = Math.Clamp(p.Opacity, 20, 100) / 100.0;
        TopMost = p.Layer == "top";
        if (IsHandleCreated) { ApplyStyles(); if (p.Layer == "desktop") SendBack(); }
        LayoutNote();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
            if (Vibe?.Place.ClickThrough == true) cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT;
            return cp;
        }
    }

    void ApplyStyles()
    {
        int ex = Native.GetWindowLong(Handle, Native.GWL_EXSTYLE);
        ex = Vibe.Place.ClickThrough ? ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED : ex & ~Native.WS_EX_TRANSPARENT;
        Native.SetWindowLong(Handle, Native.GWL_EXSTYLE, ex);
        Opacity = Math.Clamp(Vibe.Place.Opacity, 20, 100) / 100.0;
    }

    void SendBack() => Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplyStyles();
        if (Vibe.Place.Layer == "desktop") SendBack();
    }

    protected override bool ShowWithoutActivation => true;

    public void Refresh(bool force = false)
    {
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        bool isNote = note != null;
        var ink = isNote ? (Overlays.IsLight(BackColor) ? Color.FromArgb(90, 0, 0, 0) : Color.FromArgb(150, 255, 255, 255)) : Theme.Faint;
        using (var p = new Pen(isNote ? Color.FromArgb(40, 0, 0, 0) : Theme.Line)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        int pad = S(14), x = pad, w = Width - pad * 2;
        using (var tb = new SolidBrush(ink)) g.DrawString(content.Title.ToUpperInvariant(), Theme.Small, tb, x, S(9));
        // resize grip
        using (var gp = new Pen(ink)) for (int i = 1; i <= 2; i++) g.DrawLine(gp, Width - S(4 * i + 2), Height - S(3), Width - S(3), Height - S(4 * i + 2));
        if (isNote) return;

        List<WLine> lines;
        try { lines = content.Lines(); }
        catch (Exception ex) { lines = new List<WLine> { new(ex.Message, Theme.Bad) }; }
        float y = S(30);
        var newHits = new List<(RectangleF, Action)>();
        foreach (var l in lines)
        {
            using var font = new Font(l.Light ? "Segoe UI Light" : l.Bold ? "Segoe UI Semibold" : "Segoe UI", 9.75f * l.Size);
            float tx = x;
            var h = g.MeasureString("Ag", font).Height;
            if (l.Dot is Color dot)
            {
                using var db = new SolidBrush(dot);
                g.FillEllipse(db, tx, y + h / 2 - S(4), S(8), S(8));
                tx += S(14);
            }
            float rightW = 0;
            if (l.Right.Length > 0)
            {
                using var rf = l.Size > 1.5f ? new Font("Segoe UI", 9.75f) : new Font(font, FontStyle.Regular);
                var rs = g.MeasureString(l.Right, rf);
                rightW = rs.Width + S(6);
                using var rb = new SolidBrush(l.RightColor ?? Theme.Dim);
                g.DrawString(l.Right, rf, rb, x + w - rs.Width, y + (l.Size > 1.5f ? h - rs.Height - S(6) : 0));
            }
            using (var b = new SolidBrush(l.Color ?? Theme.Text))
            {
                var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = l.Size > 1.5f ? 0 : StringFormatFlags.NoWrap };
                var rect = new RectangleF(tx, y, x + w - tx - rightW, l.Size > 1.5f ? h * 1.0f : h);
                if (l.Size <= 1.5f || l.Bar != null) g.DrawString(l.Text, font, b, rect, sf);
                else
                {
                    var need = g.MeasureString(l.Text, font, (int)rect.Width);
                    rect.Height = Math.Max(h, need.Height);
                    g.DrawString(l.Text, font, b, rect, sf);
                    h = rect.Height;
                }
            }
            if (l.Click != null) newHits.Add((new RectangleF(x, y, w, h), l.Click));
            y += h;
            if (l.Bar is double bar)
            {
                using var back = new SolidBrush(Theme.Raised);
                g.FillRectangle(back, x, y, w, S(4));
                var c = l.BarColor ?? (bar > 0.9 ? Theme.Bad : bar > 0.75 ? Theme.Warn : Theme.Accent);
                using var fb = new SolidBrush(c);
                g.FillRectangle(fb, x, y, (float)(w * Math.Clamp(bar, 0, 1)), S(4));
                y += S(8);
            }
            y += S(2);
        }
        hits = newHits;
        int want = (int)y + S(12);
        if (Math.Abs(Height - want) > 1) BeginInvoke(() => { Height = want; });
    }

    protected override void WndProc(ref Message m)
    {
        if (Vibe == null) { base.WndProc(ref m); return; }
        if (m.Msg == Native.WM_NCHITTEST)
        {
            base.WndProc(ref m);
            var pt = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
            if (Vibe.Place.Locked) return;
            if (pt.X >= Width - S(14) && pt.Y >= Height - S(14)) { m.Result = (IntPtr)Native.HTBOTTOMRIGHT; return; }
            if (hits.Any(h => h.rect.Contains(pt))) return;
            if (note != null && note.Bounds.Contains(pt)) return;
            m.Result = (IntPtr)Native.HTCAPTION;
            return;
        }
        if (m.Msg == 0xA5) // WM_NCRBUTTONUP
        {
            ShowMenu(Cursor.Position);
            return;
        }
        if (m.Msg == Native.WM_WINDOWPOSCHANGING && Vibe.Place.Layer == "desktop" && IsHandleCreated)
        {
            var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<Native.WINDOWPOS>(m.LParam);
            pos.hwndInsertAfter = Native.HWND_BOTTOM;
            pos.flags &= ~Native.SWP_NOZORDER;
            System.Runtime.InteropServices.Marshal.StructureToPtr(pos, m.LParam, false);
        }
        base.WndProc(ref m);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right) { ShowMenu(Cursor.Position); return; }
        foreach (var (rect, click) in hits)
            if (rect.Contains(e.Location)) { try { click(); } catch { } Invalidate(); break; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = hits.Any(h => h.rect.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); Remember(); }
    protected override void OnMove(EventArgs e) { base.OnMove(e); if (Visible) { Remember(); } }
    protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutNote(); if (Visible) Remember(); Invalidate(); }

    void Remember()
    {
        var p = Vibe.Place;
        if (p.X == Left && p.Y == Top && p.W == Width && p.H == Height) return;
        p.X = Left; p.Y = Top; p.W = Width; p.H = Height;
        saveTimer.Stop(); saveTimer.Start();
    }

    void ShowMenu(Point at)
    {
        var menu = new ContextMenuStrip { Renderer = new DarkMenu(), ShowImageMargin = false };
        var p = Vibe.Place;
        void Item(string text, bool check, Action act) { var i = new ToolStripMenuItem(text) { Checked = check }; i.Click += (_, _) => { act(); ApplyPlace(); saved(Vibe); }; menu.Items.Add(i); }
        Item("Keep on top", p.Layer == "top", () => p.Layer = p.Layer == "top" ? "normal" : "top");
        Item("Stay on the desktop (behind windows)", p.Layer == "desktop", () => p.Layer = p.Layer == "desktop" ? "normal" : "desktop");
        Item("Lock in place", p.Locked, () => p.Locked = !p.Locked);
        Item("Let clicks go through", p.ClickThrough, () =>
        {
            p.ClickThrough = true;
            Cards.Show("Clicks go through " + Vibe.Name, "To change it back, open DeskVibes and press Edit on this widget, or use the tray menu.", Theme.Accent, 8, "Bottom right", "");
        });
        var see = new ToolStripMenuItem("See-through");
        foreach (var o in new[] { 100, 90, 75, 60, 40 })
        {
            var i = new ToolStripMenuItem(o == 100 ? "Solid" : $"{o}%") { Checked = p.Opacity == o };
            i.Click += (_, _) => { p.Opacity = o; ApplyPlace(); saved(Vibe); };
            see.DropDownItems.Add(i);
        }
        menu.Items.Add(see);
        menu.Items.Add(new ToolStripSeparator());
        var ed = new ToolStripMenuItem("Change this widget…"); ed.Click += (_, _) => edit(Vibe); menu.Items.Add(ed);
        var hide = new ToolStripMenuItem("Hide it"); hide.Click += (_, _) => { Vibe.On = false; saved(Vibe); }; menu.Items.Add(hide);
        menu.Show(at);
    }

    protected override void Dispose(bool disposing) { if (disposing) saveTimer.Dispose(); base.Dispose(disposing); }
}

/// <summary>Dark menus for the tray and widgets.</summary>
public sealed class DarkMenu : ToolStripProfessionalRenderer
{
    public DarkMenu() : base(new DarkColors()) { RoundedEdges = false; }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Faint; base.OnRenderItemText(e); }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.Dim; base.OnRenderArrow(e); }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        using var b = new SolidBrush(Theme.Accent);
        e.Graphics.FillEllipse(b, r.X + r.Width / 2 - 4, r.Y + r.Height / 2 - 4, 8, 8);
    }

    sealed class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Panel;
        public override Color MenuItemSelected => Theme.Raised;
        public override Color MenuItemSelectedGradientBegin => Theme.Raised;
        public override Color MenuItemSelectedGradientEnd => Theme.Raised;
        public override Color MenuItemBorder => Theme.Line;
        public override Color MenuBorder => Theme.Line;
        public override Color SeparatorDark => Theme.Line;
        public override Color SeparatorLight => Theme.Line;
        public override Color ImageMarginGradientBegin => Theme.Panel;
        public override Color ImageMarginGradientMiddle => Theme.Panel;
        public override Color ImageMarginGradientEnd => Theme.Panel;
        public override Color CheckBackground => Theme.Panel;
        public override Color CheckSelectedBackground => Theme.Raised;
        public override Color CheckPressedBackground => Theme.Raised;
    }
}

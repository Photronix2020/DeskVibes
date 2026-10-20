using System.Globalization;

namespace DeskVibes;

/// <summary>Runs the rule vibes: checks their triggers every second (and on pushed events) and fires their actions.</summary>
public sealed class Engine
{
    public IWorld World { get; }
    public IFeeds Feeds { get; }
    public IMail Mail { get; }
    readonly Func<Settings> settings;
    readonly Func<IEnumerable<Vibe>> vibes;
    /// <summary>Runs one action. Set by the app (overlays, sounds…); tests record calls instead.</summary>
    public Action<Vibe, Step, Ctx> RunAction { get; set; } = (_, _, _) => { };
    public event Action<string>? Logged;

    readonly Dictionary<string, State> states = new();
    readonly List<string> log = new();
    public IReadOnlyList<string> Log { get { lock (log) return log.ToList(); } }
    public DateTime Started { get; } = DateTime.Now;

    public Engine(IWorld world, IFeeds feeds, IMail mail, Func<Settings> settings, Func<IEnumerable<Vibe>> vibes)
    {
        World = world; Feeds = feeds; Mail = mail; this.settings = settings; this.vibes = vibes;
    }

    /// <summary>Per-vibe memory: last result of a condition, what's been seen, when it last fired.</summary>
    sealed class State
    {
        public string Sig = "";
        public bool? Last;
        public DateTime LastFire = DateTime.MinValue;
        public DateTime Since = DateTime.MinValue;
        public HashSet<string>? Seen;
        public double Peak;
        public string Mark = "";
    }

    State StateFor(Vibe v)
    {
        var sig = Json.Write(v.When);
        if (!states.TryGetValue(v.Id, out var s) || s.Sig != sig)
            states[v.Id] = s = new State { Sig = sig };
        return s;
    }

    public void Note(string text)
    {
        var line = $"{World.Now:HH:mm:ss}  {text}";
        lock (log) { log.Add(line); if (log.Count > 300) log.RemoveAt(0); }
        Logged?.Invoke(line);
    }

    IEnumerable<Vibe> Rules => vibes().Where(v => v.On && !v.IsWidget).ToList();

    /// <summary>Called once a second.</summary>
    public void Tick()
    {
        var now = World.Now;
        foreach (var v in Rules)
        {
            Ctx? hit;
            try { hit = Check(v, StateFor(v), now); }
            catch (Exception ex) { Note($"\"{v.Name}\" had a problem: {ex.Message}"); continue; }
            if (hit != null) Fire(v, hit);
        }
        foreach (var id in states.Keys.Where(k => !vibes().Any(v => v.Id == k)).ToList()) states.Remove(id);
    }

    /// <summary>An event from Windows (hotkey, clipboard, lock, new mail…).</summary>
    public void Push(string type, Ctx ctx)
    {
        foreach (var v in Rules)
        {
            if (v.When.Type != type) continue;
            if (!Matches(v.When, ctx)) continue;
            Fire(v, new Ctx(ctx));
        }
    }

    static bool Has(string text, string part) =>
        part.Trim().Length == 0 || (text ?? "").Contains(part.Trim(), StringComparison.OrdinalIgnoreCase);

    static bool Matches(Step w, Ctx c) => w.Type switch
    {
        "mail.new" => Has(c.GetValueOrDefault("from", ""), w.Get("from")) && Has(c.GetValueOrDefault("subject", ""), w.Get("subject")),
        "pc.clipboard" => Has(c.GetValueOrDefault("text", ""), w.Get("contains")),
        "pc.hotkey" => string.Equals(Hotkey(w.Get("keys")), c.GetValueOrDefault("keys", ""), StringComparison.OrdinalIgnoreCase),
        _ => true,
    };

    /// <summary>Normal form of a hotkey, so "alt+ctrl+k" matches "Ctrl+Alt+K".</summary>
    public static string Hotkey(string keys) =>
        Native.ParseHotkey(keys, out var m, out var vk) ? $"{m}:{vk}" : keys.Trim().ToLowerInvariant();

    /// <summary>True when "Only if" conditions and quiet hours allow firing now.</summary>
    public bool Allowed(Vibe v, DateTime now, out string why)
    {
        why = "";
        var s = settings();
        if (s.Paused) { why = "paused"; return false; }
        bool weekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        bool work = !weekend && InRange(now, s.WorkFrom, s.WorkTo);
        foreach (var o in v.Only)
        {
            bool ok = o switch
            {
                "weekdays" => !weekend,
                "weekends" => weekend,
                "work" => work,
                "notwork" => !work,
                "here" => World.IdleSeconds < 120,
                "nogame" => !World.Fullscreen,
                "battery" => World.Plugged == false,
                "plugged" => World.Plugged != false,
                _ => true,
            };
            if (!ok) { why = Catalog.Find(o)?.Label.ToLowerInvariant() ?? o; return false; }
        }
        if (s.QuietHours && InRange(now, s.QuietFrom, s.QuietTo) && !v.Do.All(a => a.Type.StartsWith("do."))) { why = "quiet hours"; return false; }
        return true;
    }

    public static bool InRange(DateTime now, string from, string to)
    {
        if (!TryTime(from, out var a) || !TryTime(to, out var b)) return false;
        var t = now.TimeOfDay;
        return a <= b ? t >= a && t < b : t >= a || t < b;
    }

    public static bool TryTime(string s, out TimeSpan t)
    {
        s = (s ?? "").Trim().ToLowerInvariant().Replace(".", "");
        string[] fmts = { "H:mm", "HH:mm", "h:mm tt", "h:mmtt", "h tt", "htt", "%H" };
        if (DateTime.TryParseExact(s, fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) { t = d.TimeOfDay; return true; }
        t = default; return false;
    }

    void Fire(Vibe v, Ctx ctx)
    {
        var now = World.Now;
        var st = StateFor(v);
        if (v.CooldownSec > 0 && (now - st.LastFire).TotalSeconds < v.CooldownSec) return;
        if (!Allowed(v, now, out var why)) { Note($"\"{v.Name}\" would fire, but not now ({why})"); return; }
        st.LastFire = now;
        ctx["vibe"] = v.Name;
        Note($"\"{v.Name}\" fired" + (ctx.TryGetValue("info", out var info) ? $" ({info})" : ""));
        foreach (var a in v.Do)
        {
            try { RunAction(v, a, ctx); }
            catch (Exception ex) { Note($"\"{v.Name}\": {Catalog.Find(a.Type)?.Label ?? a.Type} failed: {ex.Message}"); }
        }
    }

    /// <summary>Runs a vibe's actions now, for the Test button.</summary>
    public void Test(Vibe v)
    {
        var ctx = Sample(v.When);
        ctx["vibe"] = v.Name;
        foreach (var a in v.Do)
        {
            try { RunAction(v, a, ctx); }
            catch (Exception ex) { Note($"Test: {Catalog.Find(a.Type)?.Label ?? a.Type} failed: {ex.Message}"); }
        }
    }

    /// <summary>Made-up values for {placeholders} when testing.</summary>
    public static Ctx Sample(Step w) => w.Type switch
    {
        "mail.new" => Ctx.Of(("from", w.Get("from").Length > 0 ? w.Get("from") : "Jane Doe"), ("subject", w.Get("subject").Length > 0 ? w.Get("subject") : "Lunch on Friday?")),
        "mail.unread" => Ctx.Of(("count", w.Get("count"))),
        "cal.soon" => Ctx.Of(("subject", "Weekly sync"), ("start", DateTime.Now.AddMinutes(5).ToString("t")), ("minutes", w.Get("minutes"))),
        "web.weather" => Ctx.Of(("temp", "31°"), ("weather", "Light snow"), ("place", w.Get("place").Length > 0 ? w.Get("place") : "Home"), ("rain", "snow at 3 PM")),
        "web.stock" => Ctx.Of(("symbol", w.Get("symbol").ToUpperInvariant()), ("price", "123.45"), ("change", "+3.2%")),
        "web.down" or "web.up" => Ctx.Of(("url", w.Get("url")), ("status", w.Type == "web.up" ? "up" : "not answering")),
        "web.wow" => Ctx.Of(("realm", w.Get("realm")), ("status", w.Get("when") == "Goes down" ? "down" : "up")),
        "web.rss" => Ctx.Of(("title", "Example headline"), ("link", "https://example.com")),
        "pc.idle" or "pc.back" => Ctx.Of(("away", "15 minutes")),
        "pc.sitting" => Ctx.Of(("minutes", w.Get("minutes"))),
        "pc.battery" or "pc.plug" or "pc.unplug" => Ctx.Of(("battery", "18%")),
        "pc.usb.in" or "pc.usb.out" => Ctx.Of(("drive", "E:")),
        "pc.cpu" => Ctx.Of(("cpu", "97%"), ("app", "chrome")),
        "pc.memory" => Ctx.Of(("memory", "93%")),
        "pc.disk" => Ctx.Of(("free", "8.2 GB"), ("drive", w.Get("drive"))),
        "pc.clipboard" => Ctx.Of(("text", "copied text")),
        "app.start" or "app.stop" or "app.focus" or "app.usedfor" or "app.fullscreen" =>
            Ctx.Of(("app", w.Get("app").Length > 0 ? w.Get("app") : "game"), ("title", "Window title"), ("minutes", w.Get("minutes"))),
        _ => new Ctx(),
    };

    /// <summary>Fires once each time the condition turns true (including right away if it's already true).</summary>
    static Ctx? Edge(State st, bool now, Func<Ctx> ctx)
    {
        bool was = st.Last ?? false;
        st.Last = now;
        return now && !was ? ctx() : null;
    }

    /// <summary>Like <see cref="Edge"/> but the first check only learns the current state.</summary>
    static Ctx? Change(State st, bool now, Func<Ctx> ctx)
    {
        bool? was = st.Last;
        st.Last = now;
        return was != null && now && was == false ? ctx() : null;
    }

    static string Mins(double seconds)
    {
        var m = (int)Math.Round(seconds / 60);
        return m >= 120 ? $"{m / 60} hours {m % 60} minutes" : m >= 60 ? $"1 hour {m - 60} minutes" : $"{m} minute{(m == 1 ? "" : "s")}";
    }

    Ctx? Check(Vibe v, State st, DateTime now)
    {
        var w = v.When;
        switch (w.Type)
        {
            case "time.every":
            {
                var mins = Math.Max(1, w.Num("minutes", 60));
                if (st.Since == DateTime.MinValue) { st.Since = now; return null; }
                if ((now - st.Since).TotalMinutes < mins) return null;
                st.Since = now;
                return new Ctx();
            }
            case "time.at":
            {
                if (!TryTime(w.Get("time"), out var t)) return null;
                bool weekend = now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                var days = w.Get("days");
                if ((days == "Weekdays" && weekend) || (days == "Weekends" && !weekend)) return null;
                var mark = now.ToString("yyyyMMdd");
                if (now.TimeOfDay >= t && now.TimeOfDay < t + TimeSpan.FromMinutes(2) && st.Mark != mark) { st.Mark = mark; return new Ctx(); }
                return null;
            }
            case "time.date":
            {
                if (!DateTime.TryParseExact(w.Get("date").Trim(), new[] { "MM-dd", "M-d", "MM/dd", "M/d" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return null;
                if (now.Month != d.Month || now.Day != d.Day) return null;
                if (!TryTime(w.Get("time"), out var t)) t = TimeSpan.FromHours(9);
                var mark = now.ToString("yyyyMMdd");
                if (now.TimeOfDay >= t && st.Mark != mark) { st.Mark = mark; return new Ctx(); }
                return null;
            }
            case "time.startup":
                if (st.Mark == "done") return null;
                st.Mark = "done";
                return (now - Started).TotalMinutes < 2 ? new Ctx() : null;

            case "pc.idle":
                return Edge(st, World.IdleSeconds >= w.Num("minutes", 10) * 60, () => Ctx.Of(("away", Mins(World.IdleSeconds)), ("info", "away")));
            case "pc.back":
            {
                // remember the longest idle stretch; fire when input comes back after it
                if (World.IdleSeconds > st.Peak) { st.Peak = World.IdleSeconds; return null; }
                var away = st.Peak;
                if (World.IdleSeconds < 5 && away > 0) { st.Peak = 0; if (away >= w.Num("minutes", 15) * 60) return Ctx.Of(("away", Mins(away))); }
                return null;
            }
            case "pc.sitting":
                return Edge(st, World.SittingSeconds >= w.Num("minutes", 50) * 60, () => Ctx.Of(("minutes", ((int)(World.SittingSeconds / 60)).ToString())));
            case "pc.battery":
                return World.Battery == null ? null :
                    Edge(st, World.Battery < w.Num("below", 20) && World.Plugged == false, () => Ctx.Of(("battery", World.Battery + "%")));
            case "pc.unplug":
                return World.Plugged == null ? null : Change(st, World.Plugged == false, () => Ctx.Of(("battery", World.Battery + "%")));
            case "pc.plug":
                return World.Plugged == null ? null : Change(st, World.Plugged == true, () => Ctx.Of(("battery", World.Battery + "%")));
            case "pc.offline":
                return Change(st, !World.Online, () => new Ctx());
            case "pc.online":
                return Change(st, World.Online, () => new Ctx());
            case "pc.usb.in":
            case "pc.usb.out":
            {
                var cur = new HashSet<string>(World.RemovableDrives, StringComparer.OrdinalIgnoreCase);
                var old = st.Seen; st.Seen = cur;
                if (old == null) return null;
                var diff = w.Type == "pc.usb.in" ? cur.Except(old).FirstOrDefault() : old.Except(cur).FirstOrDefault();
                return diff == null ? null : Ctx.Of(("drive", diff));
            }
            case "pc.cpu":
            {
                bool high = World.Cpu >= w.Num("above", 90);
                if (!high) { st.Since = DateTime.MinValue; st.Last = false; return null; }
                if (st.Since == DateTime.MinValue) st.Since = now;
                return Edge(st, (now - st.Since).TotalSeconds >= w.Num("seconds", 30),
                    () => Ctx.Of(("cpu", Math.Round(World.Cpu) + "%"), ("app", World.BusiestApp)));
            }
            case "pc.memory":
                return Edge(st, World.Memory >= w.Num("above", 90), () => Ctx.Of(("memory", Math.Round(World.Memory) + "%")));
            case "pc.disk":
            {
                var free = World.FreeGb(w.Get("drive", "C"));
                if (free < 0) return null;
                return Edge(st, free < w.Num("below", 10), () => Ctx.Of(("free", $"{free:0.0} GB"), ("drive", w.Get("drive", "C").TrimEnd(':') + ":")));
            }

            case "app.start":
            case "app.stop":
            {
                var name = w.Get("app").Trim();
                if (name.Length == 0) return null;
                var match = World.Processes.FirstOrDefault(p => p.Contains(name, StringComparison.OrdinalIgnoreCase));
                bool running = match != null;
                if (match != null) st.Mark = match;
                return Change(st, w.Type == "app.start" ? running : !running, () => Ctx.Of(("app", st.Mark.Length > 0 ? st.Mark : name)));
            }
            case "app.focus":
            {
                var text = w.Get("text");
                bool on = text.Trim().Length > 0 && (Has(World.App, text) || Has(World.Title, text));
                return Edge(st, on, () => Ctx.Of(("app", World.App), ("title", World.Title)));
            }
            case "app.fullscreen":
                return Edge(st, World.Fullscreen, () => Ctx.Of(("app", World.App), ("title", World.Title)));
            case "app.usedfor":
            {
                var name = w.Get("app").Trim();
                if (name.Length == 0) return null;
                double secs = World.UsageToday.Where(kv => kv.Key.Contains(name, StringComparison.OrdinalIgnoreCase)).Sum(kv => kv.Value);
                var mark = now.ToString("yyyyMMdd");
                if (secs >= w.Num("minutes", 120) * 60 && st.Mark != mark) { st.Mark = mark; return Ctx.Of(("app", name), ("minutes", ((int)(secs / 60)).ToString())); }
                return null;
            }

            case "mail.unread":
                return Mail.Configured ? Edge(st, Mail.Unread >= w.Num("count", 10), () => Ctx.Of(("count", Mail.Unread.ToString()))) : null;
            case "cal.soon":
            {
                st.Seen ??= new HashSet<string>();
                foreach (var m in Mail.Meetings)
                {
                    var mins = (m.Start - now).TotalMinutes;
                    if (mins < 0 || mins > w.Num("minutes", 5) || !Has(m.Subject, w.Get("subject"))) continue;
                    if (st.Seen.Add(m.Subject + m.Start.Ticks))
                        return Ctx.Of(("subject", m.Subject), ("start", m.Start.ToString("t")), ("minutes", Math.Ceiling(mins).ToString()), ("where", m.Where));
                }
                return null;
            }

            case "web.weather":
            {
                var wx = Feeds.Weather(w.Get("place"));
                if (wx == null) return null;
                Ctx Make(string rain = "") => Ctx.Of(("temp", $"{Math.Round(wx.Temp)}°"), ("weather", wx.Text), ("place", wx.Place), ("rain", rain));
                switch (w.Get("when"))
                {
                    case "It gets hotter than": return Edge(st, wx.Temp > w.Num("value", 90), () => Make());
                    case "It gets colder than": return Edge(st, wx.Temp < w.Num("value", 32), () => Make());
                    case "It gets windy": return Edge(st, wx.Wind > w.Num("value", 25), () => Make());
                    default:
                    {
                        var ahead = wx.Hours.Take((int)Math.Clamp(w.Num("hours", 3), 1, 24)).FirstOrDefault(h => h.RainChance >= 50 && DeskVibes.Feeds.IsWet(h.Code));
                        bool wetNow = DeskVibes.Feeds.IsWet(wx.Code);
                        return Edge(st, ahead != null && !wetNow, () =>
                            Make($"{(DeskVibes.Feeds.IsSnow(ahead!.Code) ? "snow" : "rain")} around {ahead.Time:h tt} ({ahead.RainChance}%)"));
                    }
                }
            }
            case "web.stock":
            {
                var q = Feeds.Quote(w.Get("symbol"));
                if (q == null) return null;
                double val = w.Num("value", 3);
                bool on = w.Get("when") switch
                {
                    "Goes above" => q.Price > val,
                    "Goes below" => q.Price < val,
                    _ => Math.Abs(q.ChangePct) >= val,
                };
                // a big move counts once a day
                if (w.Get("when") == "Moves more than % today")
                {
                    var mark = now.ToString("yyyyMMdd");
                    if (!on || st.Mark == mark) return null;
                    st.Mark = mark;
                    return QuoteCtx(q);
                }
                return Edge(st, on, () => QuoteCtx(q));
            }
            case "web.down":
            case "web.up":
            {
                var site = Feeds.Site(w.Get("url"));
                if (site == null) return null;
                var c = Ctx.Of(("url", site.Url), ("status", site.Status));
                return w.Type == "web.down" ? Edge(st, !site.Up, () => c) : Change(st, site.Up, () => c);
            }
            case "web.wow":
            {
                var r = Feeds.Realm(w.Get("region", "us"), w.Get("game"), w.Get("realm"));
                if (r == null) return null;
                var c = Ctx.Of(("realm", r.Name), ("status", r.Status));
                return w.Get("when") == "Goes down" ? Edge(st, !r.Up, () => c) : Change(st, r.Up, () => c);
            }
            case "web.rss":
            {
                var items = Feeds.News(w.Get("url"));
                if (items == null) return null;
                bool first = st.Seen == null;
                st.Seen ??= new HashSet<string>();
                NewsItem? hit = null;
                foreach (var it in items)
                    if (st.Seen.Add(it.Id) && !first && hit == null && Has(it.Title, w.Get("contains"))) hit = it;
                return hit == null ? null : Ctx.Of(("title", hit.Title), ("link", hit.Link));
            }
        }
        return null;
    }

    static Ctx QuoteCtx(QuoteInfo q) => Ctx.Of(("symbol", q.Symbol), ("price", q.Price.ToString(q.Price >= 100 ? "0.00" : "0.####", CultureInfo.InvariantCulture)),
        ("change", (q.ChangePct >= 0 ? "+" : "") + q.ChangePct.ToString("0.0", CultureInfo.InvariantCulture) + "%"));
}

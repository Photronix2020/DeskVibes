using DeskVibes;

// A tiny test runner (no packages needed): dotnet run --project tests\DeskVibes.Tests
int passed = 0, failed = 0;
void Check(bool ok, string what)
{
    if (ok) passed++;
    else { failed++; Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("FAIL  " + what); Console.ResetColor(); }
}
void Eq<T>(T actual, T expected, string what) => Check(Equals(actual, expected), $"{what}: expected [{expected}] got [{actual}]");
void Test(string name, Action body)
{
    try { body(); }
    catch (Exception ex) { failed++; Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($"FAIL  {name} threw {ex}"); Console.ResetColor(); }
}

// ---------- basics ----------

Test("placeholders", () =>
{
    var c = Ctx.Of(("from", "Jane"), ("subject", "Hi"));
    Eq(c.Fill("{from}: {subject} {nope}", new DateTime(2026, 1, 5, 9, 30, 0)), "Jane: Hi {nope}", "fill");
    Eq(c.Fill("{day}", new DateTime(2026, 1, 5)), "Monday", "day");
    Eq(c.Fill("no braces", DateTime.Now), "no braces", "plain");
    Eq(c.Fill("{unclosed", DateTime.Now), "{unclosed", "unclosed");
});

Test("colors", () =>
{
    Eq(Colors.Parse("red", Color.Black).ToArgb(), Colors.Parse("#FF3B30", Color.Black).ToArgb(), "named red");
    Eq(Colors.Parse("#00ff00", Color.Black).G, (byte)255, "hex");
    Eq(Colors.Parse("garbage!", Color.Black).ToArgb(), Color.Black.ToArgb(), "fallback");
    Eq(Colors.NameOf("#FFD60A"), "yellow", "name of");
});

Test("share codes", () =>
{
    var v = new Vibe { Name = "Boss", When = new Step("mail.new", ("from", "boss")), Do = { new Step("show.edges", ("color", "#FF3B30")) } };
    var code = Share.Encode(v);
    Check(code.StartsWith("DV1:"), "prefix");
    var back = Share.Decode("  here you go: " + code + "\n");
    Check(back != null, "decodes with junk around it");
    Eq(back!.When.Get("from"), "boss", "keeps trigger");
    Check(back.Id != v.Id, "gets a new id");
    Check(Share.Decode("DV1:notbase64!!") == null, "rejects garbage");
    var bad = new Vibe { When = new Step("evil.trigger") };
    Check(Share.Decode(Share.Encode(bad)) == null, "rejects unknown types");
});

Test("store", () =>
{
    var dir = Path.Combine(Path.GetTempPath(), "dv-test-" + Guid.NewGuid().ToString("N")[..6]);
    var s = new Store(dir); s.Load();
    Check(s.FirstRun, "first run");
    s.Vibes.Add(new Vibe { Name = "A" }); s.Settings.Location = "Boston"; s.Save();
    var t = new Store(dir); t.Load();
    Check(!t.FirstRun, "not first run");
    Eq(t.Vibes.Count, 1, "vibes");
    Eq(t.Settings.Location, "Boston", "settings");
    File.WriteAllText(Path.Combine(dir, "vibes.json"), "{broken");
    var u = new Store(dir); u.Load();
    Check(File.Exists(Path.Combine(dir, "vibes.json.broken")), "keeps a broken file aside");
    Directory.Delete(dir, true);
});

Test("secrets", () =>
{
    var p = Secret.Protect("hunter2");
    Check(p.Length > 0 && !p.Contains("hunter2"), "encrypted");
    Eq(Secret.Unprotect(p), "hunter2", "round trip");
    Eq(Secret.Unprotect("bm9wZQ=="), "", "bad data");
});

Test("times", () =>
{
    Check(Engine.TryTime("17:30", out var a) && a == new TimeSpan(17, 30, 0), "17:30");
    Check(Engine.TryTime("5:30 pm", out var b) && b == new TimeSpan(17, 30, 0), "5:30 pm");
    Check(Engine.TryTime("9am", out var c) && c == new TimeSpan(9, 0, 0), "9am");
    Check(Engine.InRange(new DateTime(2026, 1, 1, 23, 0, 0), "22:00", "07:00"), "overnight late");
    Check(Engine.InRange(new DateTime(2026, 1, 1, 6, 0, 0), "22:00", "07:00"), "overnight early");
    Check(!Engine.InRange(new DateTime(2026, 1, 1, 12, 0, 0), "22:00", "07:00"), "overnight noon");
    var mon = new DateTime(2026, 10, 19, 10, 0, 0); // a Monday
    Eq(WidgetContent.NextTime("Friday 17:00", mon), new DateTime(2026, 10, 23, 17, 0, 0), "next friday");
    Eq(WidgetContent.NextTime("9:00", mon), new DateTime(2026, 10, 20, 9, 0, 0), "tomorrow 9");
    Eq(WidgetContent.NextTime("2026-12-25 08:00", mon), new DateTime(2026, 12, 25, 8, 0, 0), "christmas");
    Eq(WidgetContent.NextTime("Dec 25", mon), new DateTime(2026, 12, 25), "dec 25");
    Check(WidgetContent.FindZone("London") != null, "zone london");
    Check(WidgetContent.FindZone("Tokyo") != null, "zone tokyo");
});

Test("hotkeys", () =>
{
    Check(Native.ParseHotkey("Ctrl+Alt+K", out var m, out var vk) && m == 3 && vk == 'K', "ctrl alt k");
    Check(Native.ParseHotkey("Win+Shift+F9", out m, out vk) && m == 12 && vk == 0x78, "win shift f9");
    Check(!Native.ParseHotkey("Ctrl+Banana", out _, out _), "bad key");
    Eq(Engine.Hotkey("alt+ctrl+k"), Engine.Hotkey("Ctrl+Alt+K"), "same hotkey any order");
});

// ---------- catalog & gallery ----------

Test("catalog", () =>
{
    foreach (var d in Catalog.Triggers.Concat(Catalog.Actions).Concat(Catalog.Widgets))
        foreach (var f in d.Fields.Where(f => f.Kind == FieldKind.Choice))
            Check(f.Choices!.Contains(f.Default), $"{d.Id}.{f.Key} default is one of its choices");
    foreach (var t in Catalog.Triggers)
        Check(Engine.Sample(new Step(t.Id)) != null, $"sample for {t.Id}");
    foreach (var idea in Gallery.Ideas)
    {
        var v = idea.Make();
        if (v.IsWidget) Check(Catalog.Find(v.Widget!.Type) != null, $"idea widget {v.Name}");
        else
        {
            Check(Catalog.Find(v.When.Type) != null, $"idea trigger {v.Name}");
            foreach (var a in v.Do) Check(Catalog.Find(a.Type) != null, $"idea action {v.Name} {a.Type}");
            foreach (var o in v.Only) Check(Catalog.Find(o) != null, $"idea condition {v.Name} {o}");
        }
        Check(Catalog.Summary(v).Length > 0, "summary " + v.Name);
    }
    Check(Gallery.Starter().Count >= 3, "starter vibes");
    var deck = Gallery.Starter().Where(v => v.Widget?.Type == "w.deck").ToList();
    Eq(deck.Count, 1, "new installs get the button deck");
    Check(deck[0].On, "and it's switched on");
    Eq(Deck.Cols(deck[0].Widget!) * 10 + Deck.Rows(deck[0].Widget!), 82, "8 x 2");
});

// ---------- describe it ----------

Step? Act(Vibe v, string type) => v.Do.FirstOrDefault(a => a.Type == type);

Test("describe: boss email red edges", () =>
{
    var v = Describe.Parse("when I get an email from my boss turn the edges of the screen red").Vibe;
    Eq(v.When.Type, "mail.new", "trigger");
    Eq(v.When.Get("from"), "boss", "from");
    Eq(Act(v, "show.edges")?.Get("color"), "#FF3B30", "red edges");
});
Test("describe: user's own words", () =>
{
    var v = Describe.Parse("I got an email, turn the edges of the screen red or yellow").Vibe;
    Eq(v.When.Type, "mail.new", "trigger");
    Eq(v.When.Get("from"), "", "no from");
    Eq(Act(v, "show.edges")?.Get("color"), "#FF3B30", "first color wins");
});
Test("describe: bitcoin", () =>
{
    var v = Describe.Parse("if Bitcoin drops below 60000 flash the edges orange").Vibe;
    Eq(v.When.Type, "web.stock", "trigger");
    Eq(v.When.Get("symbol"), "BTC-USD", "symbol");
    Eq(v.When.Get("when"), "Goes below", "below");
    Eq(v.When.Get("value"), "60000", "price");
    Eq(Act(v, "show.edges")?.Get("style"), "Blink", "flash = blink");
    Eq(Act(v, "show.edges")?.Get("color"), "#FF9500", "orange");
});
Test("describe: stretch", () =>
{
    var v = Describe.Parse("remind me to stretch after 45 minutes of work").Vibe;
    Eq(v.When.Type, "pc.sitting", "trigger");
    Eq(v.When.Get("minutes"), "45", "minutes");
    Check(Act(v, "show.card") != null, "card");
});
Test("describe: realm", () =>
{
    var v = Describe.Parse("when Stormrage realm comes back up say it out loud").Vibe;
    Eq(v.When.Type, "web.wow", "trigger");
    Eq(v.When.Get("realm"), "Stormrage", "realm");
    Eq(v.When.Get("when"), "Comes back up", "up");
    Check(Act(v, "sound.speak") != null, "speak");
});
Test("describe: rain", () =>
{
    var v = Describe.Parse("when it's going to rain in Boston pop up a card").Vibe;
    Eq(v.When.Type, "web.weather", "trigger");
    Eq(v.When.Get("place"), "Boston", "place");
    Check(Act(v, "show.card") != null, "card");
});
Test("describe: hotkey", () =>
{
    var v = Describe.Parse("ctrl+alt+b hides everything").Vibe;
    Eq(v.When.Type, "pc.hotkey", "trigger");
    Eq(Engine.Hotkey(v.When.Get("keys")), Engine.Hotkey("Ctrl+Alt+B"), "keys");
    Check(Act(v, "do.desktop") != null, "show desktop");
});
Test("describe: app opens", () =>
{
    var v = Describe.Parse("when I open spotify mute the sound").Vibe;
    Eq(v.When.Type, "app.start", "trigger");
    Eq(v.When.Get("app"), "spotify", "app");
    Check(Act(v, "do.mute") != null, "mute");
    Check(Act(v, "do.open") == null, "doesn't also open spotify");
});
Test("describe: time + banner text", () =>
{
    var v = Describe.Parse("at 5pm on weekdays show a banner saying go home").Vibe;
    Eq(v.When.Type, "time.at", "trigger");
    Eq(v.When.Get("time"), "17:00", "time");
    Eq(Act(v, "show.banner")?.Get("text"), "go home", "banner text");
});
Test("describe: battery", () =>
{
    var v = Describe.Parse("when my battery is below 15% turn the edges orange").Vibe;
    Eq(v.When.Type, "pc.battery", "trigger");
    Eq(v.When.Get("below"), "15", "percent");
    Eq(Act(v, "show.edges")?.Get("color"), "#FF9500", "orange");
});
Test("describe: internet", () =>
{
    var v = Describe.Parse("when the internet goes down flash the screen red").Vibe;
    Eq(v.When.Type, "pc.offline", "trigger");
    Eq(Act(v, "show.flash")?.Get("color"), "#FF3B30", "flash red");
});
Test("describe: stock percent", () =>
{
    var v = Describe.Parse("if AAPL moves 5% tell me").Vibe;
    Eq(v.When.Type, "web.stock", "trigger");
    Eq(v.When.Get("symbol"), "AAPL", "symbol");
    Eq(v.When.Get("value"), "5", "percent");
});
Test("describe: website", () =>
{
    var v = Describe.Parse("when example.com is down show a red banner").Vibe;
    Eq(v.When.Type, "web.down", "trigger");
    Eq(v.When.Get("url"), "https://example.com", "url");
    Eq(Act(v, "show.banner")?.Get("color"), "#FF3B30", "red banner");
});
Test("describe: nothing to do → card", () =>
{
    var r = Describe.Parse("every 30 minutes");
    Eq(r.Vibe.When.Type, "time.every", "trigger");
    Eq(r.Vibe.When.Get("minutes"), "30", "minutes");
    Check(Act(r.Vibe, "show.card") != null, "default card");
});
Test("describe: gibberish", () =>
{
    var r = Describe.Parse("asdf qwerty");
    Check(r.GuessedTrigger, "says it guessed");
    Check(r.Vibe.Do.Count > 0, "still has an action");
});

// ---------- button deck ----------

Test("deck: defaults and limits", () =>
{
    var s = new Step("w.deck");
    Eq(Deck.Cols(s), 8, "8 columns");
    Eq(Deck.Rows(s), 2, "2 rows");
    Eq(Deck.Count(s), 16, "16 buttons");
    Check(!Deck.AlwaysOpen(s), "opens on hover by default");
    Check(Deck.Labels(s), "names on by default");
    s.P["cols"] = "100"; s.P["rows"] = "0"; s.P["size"] = "5";
    Eq(Deck.Cols(s), Deck.MaxCols, "columns capped");
    Eq(Deck.Rows(s), 1, "at least one row");
    Eq(Deck.ButtonSize(s), 32, "smallest button");
    s.P["show"] = "Always";
    Check(Deck.AlwaysOpen(s), "always open");
    var d = Catalog.Find("w.deck")!;
    Eq(d.Fields.First(f => f.Key == "cols").Default, "8", "catalog default columns");
    Eq(d.Fields.First(f => f.Key == "rows").Default, "2", "catalog default rows");
    Check(Gallery.Ideas.Any(i => i.Make().Widget?.Type == "w.deck"), "in the Ideas tab");
});

Test("deck: layout", () =>
{
    var (whole, b) = Deck.Layout(8, 2, 64, 8, 26);
    Eq(b.Length, 16, "16 rectangles");
    Eq(b[0], new Rectangle(8, 26, 64, 64), "first button");
    Eq(b[7], new Rectangle(8 + 7 * 72, 26, 64, 64), "end of first row");
    Eq(b[8], new Rectangle(8, 26 + 72, 64, 64), "second row starts under the first");
    Eq(whole, new Size(8 + 8 * 72, 26 + 2 * 72), "window size");
    Check(b.All(r => new Rectangle(Point.Empty, whole).Contains(r)), "all buttons inside");
    Check(!b.Where((r, i) => b.Where((q, j) => j != i).Any(q => q.IntersectsWith(r))).Any(), "no overlaps");
});

Test("deck: assigning buttons", () =>
{
    var s = new Step("w.deck");
    var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
    Deck.Assign(s, 3, "\"" + notepad + "\"");
    Eq(Deck.Target(s, 3), notepad, "quotes trimmed");
    Eq(Deck.Label(s, 3), "Notepad", "name from the program");
    Deck.Assign(s, 0, @"C:\Users\Me\Desktop\World of Warcraft.lnk");
    Eq(Deck.Label(s, 0), "World of Warcraft", "shortcut name");
    Eq(Deck.NameFor("https://www.youtube.com/watch?v=1"), "youtube.com", "website name");
    Eq(Deck.NameFor(@"D:\"), "D: drive", "drive");
    Eq(Deck.NameFor(@"C:\tools\myapp.exe"), "Myapp", "missing exe → file name");
    var shape = Deck.Shape(s);
    Deck.Assign(s, 5, notepad);
    Eq(Deck.Shape(s), shape, "buttons don't change the window's shape");
    s.P["cols"] = "4";
    Check(Deck.Shape(s) != shape, "columns do");
    Deck.Clear(s, 3);
    Eq(Deck.Target(s, 3), "", "cleared");
    Check(!s.P.ContainsKey("b3.name"), "label cleared too");
    Deck.Assign(s, 5, "  ");
    Eq(Deck.Target(s, 5), "", "blank clears");
    var v = new Vibe { Kind = "widget", Widget = s };
    Eq(Share.Decode(Share.Encode(v))!.Widget!.Get("b0"), @"C:\Users\Me\Desktop\World of Warcraft.lnk", "buttons survive a share code");
    Check(Json.Clone(v).Widget!.Get("b0.name") == "World of Warcraft", "and saving");
});

Test("deck: launching something that's gone", () =>
{
    var msg = Deck.Launch(@"C:\definitely\not\here\game.exe");
    Check(msg != null && msg.Contains("isn't there"), "plain message, nothing started");
    Eq(Deck.Launch(""), null, "empty button does nothing");
});

Test("deck: icons", () =>
{
    var notepad = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
    var icon = Deck.Icon(notepad, 48);
    Check(icon != null && icon.Width >= 32, "real icon from the shell");
    Check(ReferenceEquals(icon, Deck.Icon(notepad, 48)), "cached");
    Check(Deck.Icon(@"C:\nope\missing.exe", 48) == null, "missing file → no icon");
});

Test("deck: added once to older setups", () =>
{
    var old = new List<Vibe> { new() { Name = "Mine" } };
    var st = new Settings();
    Check(Gallery.AddDeckOnce(old, st), "added on update");
    Eq(old.Count(v => v.Widget?.Type == "w.deck"), 1, "one deck");
    Check(st.GotDeck, "remembered");
    old.RemoveAll(v => v.Widget?.Type == "w.deck");
    Check(!Gallery.AddDeckOnce(old, st), "not again");
    Eq(old.Count(v => v.Widget?.Type == "w.deck"), 0, "deleting it keeps it gone");
    var has = new List<Vibe> { Gallery.DeckWidget() };
    Gallery.AddDeckOnce(has, new Settings());
    Eq(has.Count, 1, "no second deck if one is there");
});

// ---------- timers & alarms ----------

var mon2pm = new DateTime(2026, 10, 26, 14, 0, 0); // a Monday

Test("timers: make, count down, ring", () =>
{
    var t = Timers.Timer(TimeSpan.FromMinutes(10), " Pizza ", mon2pm);
    Eq(t.Label, "Pizza", "label trimmed");
    Eq(t.Due, mon2pm.AddMinutes(10), "due");
    Eq(Timers.Left(t, mon2pm), "10:00", "full");
    Eq(Timers.Left(t, mon2pm.AddSeconds(1.2)), "9:59", "counts down, rounding up");
    Eq(Timers.Left(Timers.Timer(TimeSpan.FromMinutes(90), "", mon2pm), mon2pm), "1:30:00", "hours");
    Eq(Timers.Length(5400), "1 hour 30 minutes", "length words");
    Eq(Timers.Length(60), "1 minute", "singular");
    Eq(Timers.Name(Timers.Timer(TimeSpan.FromMinutes(5), "", mon2pm)), "5 minutes timer", "name without label");
    var list = new List<TimerItem> { t };
    Eq(Timers.TakeDue(list, mon2pm.AddMinutes(9)).Count, 0, "not yet");
    var rang = Timers.TakeDue(list, mon2pm.AddMinutes(10));
    Eq(rang.Count, 1, "rings on time");
    Eq(list.Count, 0, "timer goes away after ringing");
    var snooze = Timers.Snooze(t, 5, mon2pm.AddMinutes(10));
    Eq(snooze.Due, mon2pm.AddMinutes(15), "snooze 5");
    Eq(snooze.Label, "Pizza", "snooze keeps the name");
    Check(Timers.Timer(TimeSpan.Zero, "", mon2pm).Seconds >= 1, "at least a second");
});

Test("timers: alarms", () =>
{
    var a = Timers.Alarm(new TimeSpan(7, 30, 0), "Weekdays", "Wake up", mon2pm);
    Eq(a.Time, "07:30", "time");
    Eq(a.Due, new DateTime(2026, 10, 27, 7, 30, 0), "next weekday morning");
    Check(Timers.Left(a, mon2pm).StartsWith("Tomorrow"), "says tomorrow");
    var fri = new DateTime(2026, 10, 30, 9, 0, 0);
    Eq(Timers.NextAlarm(new TimeSpan(7, 30, 0), "Weekdays", fri), new DateTime(2026, 11, 2, 7, 30, 0), "weekdays skip the weekend");
    Eq(Timers.NextAlarm(new TimeSpan(10, 0, 0), "Weekends", mon2pm), new DateTime(2026, 10, 31, 10, 0, 0), "weekends wait for Saturday");
    Eq(Timers.NextAlarm(new TimeSpan(15, 0, 0), "Once", mon2pm), mon2pm.AddHours(1), "later today");
    Eq(Timers.NextAlarm(new TimeSpan(14, 0, 0), "Once", mon2pm), mon2pm.AddDays(1), "exactly now → tomorrow");

    var list = new List<TimerItem> { a, Timers.Alarm(new TimeSpan(7, 25, 0), "Once", "", mon2pm) };
    var rang = Timers.TakeDue(list, new DateTime(2026, 10, 27, 7, 30, 5));
    Eq(rang.Count, 2, "both ring (the one-time one 5 minutes late)");
    Eq(list.Count, 2, "alarms stay in the list");
    Eq(a.Due, new DateTime(2026, 10, 28, 7, 30, 0), "repeating alarm moves to the next day");
    Check(!list[1].On, "one-time alarm switches itself off");
    Eq(Timers.Left(list[1], mon2pm), "off", "shows off");
    Timers.SetOn(list[1], true, mon2pm);
    Check(list[1].On && list[1].Due == new DateTime(2026, 10, 27, 7, 25, 0), "switching on picks the next time");

    var missed = new List<TimerItem>();
    var late = new List<TimerItem> { Timers.Timer(TimeSpan.FromMinutes(1), "Tea", mon2pm) };
    Eq(Timers.TakeDue(late, mon2pm.AddHours(2), missed).Count, 0, "very late: doesn't ring");
    Eq(missed.Count, 1, "reported as missed");
    Eq(late.Count, 0, "and cleared");

    var sorted = Timers.Sorted(new[] { a, Timers.Timer(TimeSpan.FromMinutes(3), "b", mon2pm), Timers.Timer(TimeSpan.FromMinutes(1), "c", mon2pm) }).Select(x => x.Label).ToList();
    Eq(string.Join(",", sorted), "c,b,Wake up", "timers first, soonest first");
});

Test("timers: understood from a sentence", () =>
{
    TimerItem Q(string s) => Timers.Quick(s, mon2pm)!;
    var t = Q("set a 10 minute timer for the pizza");
    Check(!t.IsAlarm, "timer");
    Eq(t.Seconds, 600, "10 minutes");
    Eq(t.Label, "Pizza", "label");
    t = Q("timer 25 minutes"); Eq(t.Seconds, 1500, "timer N minutes"); Eq(t.Label, "", "no label");
    t = Q("timer 10 min pizza"); Eq(t.Label, "Pizza", "label after the time");
    t = Q("remind me in 20 minutes to check the oven"); Eq(t.Seconds, 1200, "remind in"); Eq(t.Label, "Check the oven", "remind label");
    t = Q("remind me to stretch in 45 minutes"); Eq(t.Seconds, 2700, "remind ... in"); Eq(t.Label, "Stretch", "label before the time");
    t = Q("start a timer for 1 hour 30 minutes called laundry"); Eq(t.Seconds, 5400, "hours and minutes"); Eq(t.Label, "Laundry", "called");
    t = Q("timer for half an hour"); Eq(t.Seconds, 1800, "half an hour");
    t = Q("90 second timer"); Eq(t.Seconds, 90, "seconds");
    t = Q("set an alarm in 20 minutes"); Check(!t.IsAlarm && t.Seconds == 1200, "alarm in N minutes is a timer");

    var a = Q("wake me up at 7 on weekdays");
    Check(a.IsAlarm, "alarm");
    Eq(a.Time, "07:00", "wake → morning");
    Eq(a.Days, "Weekdays", "weekdays");
    Eq(a.Label, "", "no label");
    a = Q("alarm 6:45pm call mom"); Eq(a.Time, "18:45", "pm"); Eq(a.Label, "Call mom", "label after time"); Eq(a.Days, "Once", "once");
    a = Q("set an alarm for 7:30 am every day"); Eq(a.Time, "07:30", "for 7:30 am"); Eq(a.Days, "Every day", "every day");
    a = Q("remind me at 3pm to call the dentist"); Check(a.IsAlarm, "remind at → alarm"); Eq(a.Time, "15:00", "3pm"); Eq(a.Label, "Call the dentist", "to ...");
    a = Q("alarm at 5"); Eq(a.Time, "17:00", "at 5 in the afternoon → 5 PM (the next one)");
    a = Q("alarm at noon for lunch"); Eq(a.Time, "12:00", "noon"); Eq(a.Label, "Lunch", "for lunch");
    a = Q("alarm at 10 tonight"); Eq(a.Time, "22:00", "tonight");

    foreach (var normal in new[] { "remind me to stretch after 45 minutes of work", "at 5pm on weekdays show a banner saying go home",
             "every 30 minutes", "when I get an email from my boss turn the edges of the screen red", "if AAPL moves 5% tell me", "ctrl+alt+b hides everything" })
        Check(Timers.Quick(normal, mon2pm) == null, "normal vibe stays a vibe: " + normal);
    Check(Timers.Quick("set an alarm", mon2pm) == null, "alarm without a time");
    Check(Timers.ParseLength("2h 5m 3s") == TimeSpan.FromSeconds(7503), "2h 5m 3s");
});

Test("timers: saved with the settings", () =>
{
    var s = new Settings();
    s.Timers.Add(Timers.Alarm(new TimeSpan(6, 0, 0), "Weekends", "Run", mon2pm));
    s.Timers.Add(Timers.Timer(TimeSpan.FromMinutes(3), "Tea", mon2pm));
    var back = Json.Read<Settings>(Json.Write(s))!;
    Eq(back.Timers.Count, 2, "both");
    Check(back.Timers[0].IsAlarm && back.Timers[0].Days == "Weekends" && back.Timers[0].Label == "Run", "alarm kept");
    Eq(back.Timers[1].Due, mon2pm.AddMinutes(3), "timer due kept");
    Check(Catalog.Find("w.timers") != null && Gallery.Ideas.Any(i => i.Make().Widget?.Type == "w.timers"), "timers widget in the catalog and Ideas");
});

// ---------- PC health ----------

Test("health: sums", () =>
{
    var engines = new[]
    {
        ("pid_10_luid_0x00000000_0x0000D1A4_phys_0_eng_0_engtype_3D", 30.0),
        ("pid_22_luid_0x00000000_0x0000D1A4_phys_0_eng_0_engtype_3D", 25.0),
        ("pid_22_luid_0x00000000_0x0000D1A4_phys_0_eng_3_engtype_VideoDecode", 40.0),
        ("pid_31_luid_0x00000000_0x0000AAAA_phys_0_eng_0_engtype_3D", 10.0),
    };
    Eq(Health.GpuPercent(engines), 55.0, "busiest engine, all apps added up");
    Eq(Health.GpuPercent(new[] { ("pid_1_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 80.0), ("pid_2_luid_0x0_0x1_phys_0_eng_0_engtype_3D", 70.0) }), 100.0, "capped at 100");
    Eq(Health.GpuPercent(Array.Empty<(string, double)>()), 0.0, "nothing");
    Eq(Health.Mbps(2_000_000, 1_000_000, 2), 4.0, "bytes → Mbps");
    Eq(Health.Mbps(5, 10, 1), 0.0, "counter reset");
    Eq(Health.Speed(12.34), "12.3 Mbps", "speed");
    Eq(Health.Speed(0.85), "850 kbps", "slow speed");
});

Test("health: widget lines", () =>
{
    var h = new HealthStats
    {
        Taken = mon2pm, Cpu = 42, CpuGhz = 4.1, MemUsedGb = 12.5, MemTotalGb = 32, MemPercent = 39, Gpu = 64, GpuMemUsedGb = 3.2, GpuMemTotalGb = 8,
        DiskBusy = 12, DiskReadMBs = 1.5, DiskWriteMBs = 0.2, Drives = { new("C:", 120, 476), new("D:", 5, 931) },
        DownMbps = 23.4, UpMbps = 1.2, PingMs = 18, Processes = 230, TopCpuApp = "chrome", TopCpuPercent = 12, TopMemApp = "chrome", TopMemGb = 2.1,
        Uptime = TimeSpan.FromHours(30), Battery = 80, Charging = false, BatteryLeft = TimeSpan.FromMinutes(150),
    };
    var lines = WidgetContent.HealthLines(h, new Step("w.system"));
    string R(string left) => lines.FirstOrDefault(l => l.Text == left)?.Right ?? "(missing)";
    Eq(R("CPU"), "42% · 4.1 GHz", "cpu");
    Eq(R("Memory"), "12.5 of 32 GB", "memory");
    Eq(R("Graphics"), "64% · 3.2 of 8 GB", "gpu");
    Check(R("Disks").StartsWith("12%"), "disk activity");
    Eq(R("Drive C:"), "120 GB free of 476", "drive C");
    Check(lines.First(l => l.Text == "Drive D:").RightColor == Theme.Bad, "nearly full drive is red");
    Eq(R("Network"), "↓ 23.4 Mbps  ↑ 1.2 Mbps", "network");
    Eq(R("Ping"), "18 ms", "ping");
    Eq(R("Battery"), "80% · 2h 30m left", "battery time left");
    Eq(R("Busiest app"), "chrome 12%", "busiest");
    Eq(R("Most memory"), "chrome 2.1 GB", "memory hog");
    Eq(R("Up for"), "1d 6h · 230 processes", "uptime");
    var off = new Step("w.system", ("gpu", "false"), ("disk", "false"), ("net", "false"), ("top", "false"));
    var few = WidgetContent.HealthLines(h, off).Select(l => l.Text).ToList();
    Check(!few.Contains("Graphics") && !few.Contains("Network") && !few.Contains("Drive C:") && !few.Contains("Busiest app"), "sections can be switched off");
    Check(few.Contains("CPU") && few.Contains("Memory"), "basics stay");
    h.Online = false;
    Eq(WidgetContent.HealthLines(h, new Step("w.system")).First(l => l.Text == "Internet").Right, "offline", "offline");
    Eq(WidgetContent.HealthLines(new HealthStats { Taken = DateTime.MinValue }, new Step("w.system"))[0].Text, "Measuring…", "before the first reading");
});

Test("health: real reading of this PC (no internet)", () =>
{
    using var s = new Health.Sampler();
    s.Sample(ping: false);
    Thread.Sleep(1100);
    var h = s.Sample(ping: false);
    Check(h.Cpu is >= 0 and <= 100, "cpu in range");
    Check(h.MemTotalGb > 1 && h.MemUsedGb > 0, "memory");
    Check(h.Drives.Any(d => d.Name.StartsWith("C")), "drive C listed");
    Check(h.Processes > 10, "processes counted");
    Check(h.TopMemApp.Length > 0, "an app uses the most memory");
    Check(h.DiskBusy is null or (>= 0 and <= 100), "disk busy in range");
    Check(h.Gpu is null or (>= 0 and <= 100), "gpu in range");
    Check(h.PingMs == null, "didn't ping");
    Console.WriteLine($"      this PC: cpu {h.Cpu:0}% {h.CpuGhz:0.0}GHz '{h.CpuName}', gpu {(h.Gpu?.ToString("0") ?? "n/a")}% '{h.GpuName}' {h.GpuMemTotalGb:0}GB, disk {(h.DiskBusy?.ToString("0") ?? "n/a")}%, top {h.TopCpuApp}/{h.TopMemApp}");
});

// ---------- AI describe (canned answers, no network) ----------

string ApiResponse(string answer, string stop = "end_turn") =>
    new System.Text.Json.Nodes.JsonObject
    {
        ["id"] = "msg_test", ["type"] = "message", ["role"] = "assistant", ["model"] = AiDescribe.Model, ["stop_reason"] = stop,
        ["content"] = new System.Text.Json.Nodes.JsonArray(
            new System.Text.Json.Nodes.JsonObject { ["type"] = "thinking", ["thinking"] = "", ["signature"] = "x" },
            new System.Text.Json.Nodes.JsonObject { ["type"] = "text", ["text"] = answer }),
    }.ToJsonString();

const string BossAnswer = """
    {"kind":"rule","name":"Boss email → red edges",
     "trigger":{"type":"mail.new","answers":[{"key":"from","value":"boss@work.com"},{"key":"bogus","value":"x"}]},
     "actions":[{"type":"show.edges","answers":[{"key":"color","value":"red"},{"key":"style","value":"blink"},{"key":"size","value":"lots"}]},
                {"type":"sound.speak","answers":[{"key":"text","value":"Your boss wrote: {subject}"}]}],
     "only":["work","nogame","made.up"],"cooldown_minutes":60,"widget":null,
     "notes":["I guessed your boss's address from what you wrote."]}
    """;

Test("ai: canned response → vibe", () =>
{
    var r = AiDescribe.FromResponse(ApiResponse(BossAnswer));
    Check(r != null, "parses");
    var v = r!.Vibe;
    Eq(v.Kind, "rule", "kind");
    Eq(v.Name, "Boss email → red edges", "name");
    Eq(v.When.Type, "mail.new", "trigger");
    Eq(v.When.Get("from"), "boss@work.com", "from");
    Check(!v.When.P.ContainsKey("bogus"), "drops unknown field keys");
    Eq(v.Do.Count, 2, "two actions");
    Eq(v.Do[0].Get("color"), "#FF3B30", "named color → palette hex");
    Eq(v.Do[0].Get("style"), "Blink", "choice matched case-insensitively");
    Check(!v.Do[0].P.ContainsKey("size"), "bad number dropped (default applies)");
    Eq(v.Do[1].Get("text"), "Your boss wrote: {subject}", "placeholder text kept");
    Eq(string.Join(",", v.Only), "work,nogame", "unknown condition dropped");
    Eq(v.CooldownSec, 3600, "cooldown");
    Check(!r.GuessedTrigger, "not guessed");
    Eq(r.Understood[0], "Understood with Claude AI.", "first note says AI");
    Check(r.Understood.Any(n => n.StartsWith("When: A new email arrives")), "when note");
    Check(r.Understood.Any(n => n.StartsWith("Then: Light up the screen edges")), "then note");
    Check(r.Understood.Contains("I guessed your boss's address from what you wrote."), "Claude's own note");
    Check(Share.Decode(Share.Encode(v)) != null, "result survives a share code round trip");
});

Test("ai: widget answer", () =>
{
    var r = AiDescribe.FromJson("""
        {"kind":"widget","name":"","trigger":null,"actions":[],"only":[],"cooldown_minutes":0,
         "widget":{"type":"w.focus","answers":[{"key":"work","value":"50"},{"key":"color","value":"#30d158"}]},"notes":[]}
        """);
    Check(r != null, "parses");
    Check(r!.Vibe.IsWidget, "is widget");
    Eq(r.Vibe.Widget!.Type, "w.focus", "type");
    Eq(r.Vibe.Widget.Get("work"), "50", "work minutes");
    Eq(r.Vibe.Widget.Get("color"), "#30D158", "hex normalized");
    Eq(r.Vibe.Name, "Focus timer", "empty name → catalog label");
});

Test("ai: values are checked against the catalog", () =>
{
    var r = AiDescribe.FromJson("""
        {"kind":"rule","name":"x","trigger":{"type":"time.at","answers":[{"key":"time","value":"7:05"},{"key":"days","value":"Mondays"}]},
         "actions":[],"only":[],"cooldown_minutes":0,"widget":null,"notes":["", "a", "b", "c", "d"]}
        """)!;
    Eq(r.Vibe.When.Get("time"), "07:05", "time padded");
    Check(!r.Vibe.When.P.ContainsKey("days"), "choice not in list dropped");
    Eq(r.Vibe.Do.Count, 1, "no actions → default card");
    Eq(r.Vibe.Do[0].Type, "show.card", "card");
    Check(!r.Understood.Contains("d"), "at most three of Claude's notes");
    var bad = AiDescribe.FromJson("""{"kind":"rule","name":"x","trigger":{"type":"time.at","answers":[{"key":"time","value":"25:99"}]},"actions":[],"only":[],"cooldown_minutes":0,"widget":null,"notes":[]}""")!;
    Check(!bad.Vibe.When.P.ContainsKey("time"), "impossible time dropped");
});

Test("ai: unknown types are refused", () =>
{
    Check(AiDescribe.FromJson("""{"kind":"rule","name":"x","trigger":{"type":"evil.trigger","answers":[]},"actions":[],"only":[],"cooldown_minutes":0,"widget":null,"notes":[]}""") == null, "unknown trigger");
    Check(AiDescribe.FromJson("""{"kind":"rule","name":"x","trigger":{"type":"pc.lock","answers":[]},"actions":[{"type":"do.format_disk","answers":[]}],"only":[],"cooldown_minutes":0,"widget":null,"notes":[]}""") == null, "unknown action");
    Check(AiDescribe.FromJson("""{"kind":"rule","name":"x","trigger":{"type":"pc.lock","answers":[]},"actions":[{"type":"w.clock","answers":[]}],"only":[],"cooldown_minutes":0,"widget":null,"notes":[]}""") == null, "widget id used as an action");
    Check(AiDescribe.FromJson("""{"kind":"widget","name":"x","trigger":null,"actions":[],"only":[],"cooldown_minutes":0,"widget":{"type":"w.bitcoin_miner","answers":[]},"notes":[]}""") == null, "unknown widget");
    Check(AiDescribe.FromJson("""{"kind":"rule","name":"x","trigger":null,"actions":[],"only":[],"cooldown_minutes":0,"widget":null,"notes":[]}""") == null, "rule without trigger");
    Check(AiDescribe.FromJson("not json at all") == null, "garbage");
    Check(AiDescribe.FromJson("[1,2]") == null, "not an object");
});

Test("ai: declined or cut-off responses", () =>
{
    Check(AiDescribe.FromResponse(ApiResponse(BossAnswer, "refusal")) == null, "refusal");
    Check(AiDescribe.FromResponse(ApiResponse(BossAnswer[..40], "max_tokens")) == null, "cut off");
    Check(AiDescribe.FromResponse("""{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}""") == null, "error body");
    Check(AiDescribe.FromResponse("<html>") == null, "not json");
});

Test("ai: request body", () =>
{
    var body = System.Text.Json.Nodes.JsonNode.Parse(AiDescribe.BuildRequest("  when my boss emails, go red  "))!;
    Eq((string)body["model"]!, "claude-opus-5-5", "model");
    Eq((string)body["messages"]![0]!["content"]!, "when my boss emails, go red", "sentence trimmed");
    Eq((string)body["output_config"]!["format"]!["type"]!, "json_schema", "structured output");
    Eq((string)body["fallbacks"]!, "default", "fallbacks");
    Check(body.AsObject().ContainsKey("thinking") == false, "no thinking override");
    Check(AiDescribe.BuildRequest("x", false).Contains("\"fallbacks\"") == false, "no fallbacks when asked");
    var sys = (string)body["system"]![0]!["text"]!;
    foreach (var d in Catalog.Triggers.Concat(Catalog.Actions).Concat(Catalog.Widgets).Concat(Catalog.Conditions))
        Check(sys.Contains($"\"id\":\"{d.Id}\""), "prompt lists " + d.Id);
    Check(sys.Contains("Moves more than % today"), "prompt lists choices");
    var schema = body["output_config"]!["format"]!["schema"]!;
    var triggerIds = schema["properties"]!["trigger"]!["anyOf"]![0]!["properties"]!["type"]!["enum"]!.AsArray().Select(n => (string)n!).ToList();
    Eq(triggerIds.Count, Catalog.Triggers.Length, "schema enum has every trigger");
    Check(!AiDescribe.BuildRequest("x").Contains("sk-ant"), "no key in the body");
});

Test("ai: no key → phrase matcher, no network", () =>
{
    var r = AiDescribe.ParseAsync("when I get an email from my boss turn the edges of the screen red", "").GetAwaiter().GetResult();
    Eq(r.Vibe.When.Type, "mail.new", "same as Describe.Parse");
    Check(!r.Understood.Contains("Understood with Claude AI."), "not marked as AI");
});

Test("settings: api key is stored encrypted", () =>
{
    var s = new Settings { AnthropicKeyProtected = Secret.Protect("sk-ant-test-123") };
    var json = Json.Write(s);
    Check(!json.Contains("sk-ant-test-123"), "not in plain text");
    Eq(Secret.Unprotect(Json.Read<Settings>(json)!.AnthropicKeyProtected), "sk-ant-test-123", "round trip");
});

// ---------- engine ----------

var world = new FakeWorld();
var feeds = new FakeFeeds();
var mail = new FakeMail();
var settings = new Settings();
var vibes = new List<Vibe>();
var ran = new List<(string vibe, string action, Ctx ctx)>();
var engine = new Engine(world, feeds, mail, () => settings, () => vibes) { RunAction = (v, a, c) => ran.Add((v.Name, a.Type, c)) };
void Reset(params Vibe[] vs) { vibes.Clear(); vibes.AddRange(vs); ran.Clear(); }
Vibe R(string name, Step when, params Step[] then) => new() { Name = name, When = when, Do = then.Length > 0 ? then.ToList() : new List<Step> { new("show.card") } };
void Ticks(int n) { for (int i = 0; i < n; i++) { engine.Tick(); world.Now = world.Now.AddSeconds(1); } }

Test("engine: email filter", () =>
{
    Reset(R("boss", new Step("mail.new", ("from", "boss@x.com")), new Step("show.edges")));
    engine.Push("mail.new", Ctx.Of(("from", "Jane <jane@x.com>"), ("subject", "hi")));
    Eq(ran.Count, 0, "other sender ignored");
    engine.Push("mail.new", Ctx.Of(("from", "The Boss <BOSS@x.com>"), ("subject", "now")));
    Eq(ran.Count, 1, "boss fires (any case)");
    Eq(ran[0].ctx["vibe"], "boss", "vibe name in ctx");
});

Test("engine: battery edge + cooldown", () =>
{
    var v = R("bat", new Step("pc.battery", ("below", "20")));
    Reset(v);
    world.Battery = 50; world.Plugged = false; Ticks(2);
    Eq(ran.Count, 0, "not yet");
    world.Battery = 15; Ticks(3);
    Eq(ran.Count, 1, "fires once while low");
    world.Battery = 30; Ticks(1); world.Battery = 10; Ticks(1);
    Eq(ran.Count, 2, "fires again after recovering");
    v.CooldownSec = 3600;
    world.Battery = 30; Ticks(1); world.Battery = 10; Ticks(1);
    Eq(ran.Count, 2, "cooldown holds it back");
    world.Plugged = true; world.Battery = 10;
});

Test("engine: only weekdays, quiet hours, paused", () =>
{
    var v = R("wk", new Step("pc.hotkey", ("keys", "Ctrl+Alt+K")));
    v.Only.Add("weekdays");
    Reset(v);
    world.Now = new DateTime(2026, 10, 18, 12, 0, 0); // Sunday
    engine.Push("pc.hotkey", Ctx.Of(("keys", Engine.Hotkey("Ctrl+Alt+K"))));
    Eq(ran.Count, 0, "weekend blocked");
    world.Now = new DateTime(2026, 10, 19, 12, 0, 0); // Monday
    engine.Push("pc.hotkey", Ctx.Of(("keys", Engine.Hotkey("ctrl+alt+k"))));
    Eq(ran.Count, 1, "weekday ok");
    engine.Push("pc.hotkey", Ctx.Of(("keys", Engine.Hotkey("Ctrl+Alt+J"))));
    Eq(ran.Count, 1, "other hotkey ignored");
    settings.QuietHours = true; settings.QuietFrom = "11:00"; settings.QuietTo = "13:00";
    engine.Push("pc.hotkey", Ctx.Of(("keys", Engine.Hotkey("Ctrl+Alt+K"))));
    Eq(ran.Count, 1, "quiet hours hold back pop-ups");
    v.Do = new List<Step> { new("do.lock") };
    engine.Push("pc.hotkey", Ctx.Of(("keys", Engine.Hotkey("Ctrl+Alt+K"))));
    Eq(ran.Count, 2, "quiet hours still allow 'do' actions");
    settings.QuietHours = false; settings.Paused = true;
    engine.Push("pc.hotkey", Ctx.Of(("keys", Engine.Hotkey("Ctrl+Alt+K"))));
    Eq(ran.Count, 2, "paused");
    settings.Paused = false;
});

Test("engine: app opens", () =>
{
    Reset(R("spot", new Step("app.start", ("app", "spotify"))));
    world.Procs = new() { "explorer", "Spotify" };
    Ticks(2);
    Eq(ran.Count, 0, "already running at start doesn't count");
    world.Procs = new() { "explorer" }; Ticks(1);
    world.Procs = new() { "explorer", "Spotify" }; Ticks(1);
    Eq(ran.Count, 1, "fires on launch");
    Eq(ran[0].ctx["app"], "Spotify", "app name");
});

Test("engine: time of day", () =>
{
    Reset(R("five", new Step("time.at", ("time", "17:00"), ("days", "Every day"))));
    world.Now = new DateTime(2026, 10, 19, 16, 59, 58); Ticks(5);
    Eq(ran.Count, 1, "fires at 17:00 once");
    world.Now = new DateTime(2026, 10, 19, 17, 0, 30); Ticks(5);
    Eq(ran.Count, 1, "not twice the same day");
    world.Now = new DateTime(2026, 10, 20, 17, 0, 10); Ticks(1);
    Eq(ran.Count, 2, "next day");
});

Test("engine: every N minutes", () =>
{
    Reset(R("every", new Step("time.every", ("minutes", "1"))));
    world.Now = new DateTime(2026, 10, 19, 9, 0, 0);
    Ticks(125);
    Eq(ran.Count, 2, "twice in ~2 minutes");
});

Test("engine: come back after away", () =>
{
    Reset(R("back", new Step("pc.back", ("minutes", "10"))));
    for (int i = 0; i < 5; i++) { world.Idle = 60 * i; Ticks(1); }
    world.Idle = 1; Ticks(1);
    Eq(ran.Count, 0, "4 minutes away is not enough");
    for (int i = 0; i < 15; i++) { world.Idle = 60 * i; Ticks(1); }
    world.Idle = 0; Ticks(1);
    Eq(ran.Count, 1, "fires after 14 minutes away");
    Check(ran[0].ctx["away"].Contains("14"), "says how long: " + ran[0].ctx["away"]);
    world.Idle = 0;
});

Test("engine: stock below", () =>
{
    Reset(R("btc", new Step("web.stock", ("symbol", "BTC-USD"), ("when", "Goes below"), ("value", "60000"))));
    Ticks(1);
    Eq(ran.Count, 0, "no data yet");
    feeds.Quotes["BTC-USD"] = new QuoteInfo("BTC-USD", "Bitcoin", 61000, -1, "USD"); Ticks(1);
    Eq(ran.Count, 0, "above");
    feeds.Quotes["BTC-USD"] = new QuoteInfo("BTC-USD", "Bitcoin", 59000, -4.2, "USD"); Ticks(3);
    Eq(ran.Count, 1, "below fires once");
    Eq(ran[0].ctx["change"], "-4.2%", "change text");
});

Test("engine: big move once a day", () =>
{
    Reset(R("aapl", new Step("web.stock", ("symbol", "AAPL"), ("when", "Moves more than % today"), ("value", "3"))));
    feeds.Quotes["AAPL"] = new QuoteInfo("AAPL", "Apple", 200, 3.5, "USD"); Ticks(3);
    feeds.Quotes["AAPL"] = new QuoteInfo("AAPL", "Apple", 200, 1, "USD"); Ticks(1);
    feeds.Quotes["AAPL"] = new QuoteInfo("AAPL", "Apple", 200, 4, "USD"); Ticks(1);
    Eq(ran.Count, 1, "only once per day");
});

Test("engine: weather rain coming", () =>
{
    Reset(R("rain", new Step("web.weather", ("when", "Rain or snow is coming"), ("hours", "3"))));
    var hours = new List<HourCast> { new(world.Now, 10, 2), new(world.Now.AddHours(1), 20, 3), new(world.Now.AddHours(2), 80, 63) };
    feeds.Wx = new WeatherInfo("Boston", 50, "F", 3, 5, 55, 40, hours); Ticks(2);
    Eq(ran.Count, 1, "rain coming fires");
    Check(ran[0].ctx["rain"].StartsWith("rain around"), "rain text: " + ran[0].ctx["rain"]);
    feeds.Wx = null;
});

Test("engine: website down/up", () =>
{
    Reset(R("down", new Step("web.down", ("url", "example.com"))), R("up", new Step("web.up", ("url", "example.com"))));
    feeds.Sites["https://example.com"] = new SiteInfo("https://example.com", true, "up"); Ticks(1);
    Eq(ran.Count, 0, "up at start: nothing");
    feeds.Sites["https://example.com"] = new SiteInfo("https://example.com", false, "not answering"); Ticks(2);
    Eq(ran.Count(r => r.vibe == "down"), 1, "down fires");
    feeds.Sites["https://example.com"] = new SiteInfo("https://example.com", true, "up"); Ticks(1);
    Eq(ran.Count(r => r.vibe == "up"), 1, "up fires");
});

Test("engine: realm comes back up", () =>
{
    Reset(R("realm", new Step("web.wow", ("realm", "Stormrage"), ("when", "Comes back up"))));
    feeds.RealmUp = false; Ticks(2);
    Eq(ran.Count, 0, "down");
    feeds.RealmUp = true; Ticks(2);
    Eq(ran.Count, 1, "back up");
});

Test("engine: news", () =>
{
    Reset(R("news", new Step("web.rss", ("url", "x"), ("contains", "election"))));
    feeds.Items = new List<NewsItem> { new("Old election story", "l1", "1") }; Ticks(1);
    Eq(ran.Count, 0, "first look only learns");
    feeds.Items = new List<NewsItem> { new("Cat video", "l2", "2"), new("Old election story", "l1", "1") }; Ticks(1);
    Eq(ran.Count, 0, "not about the election");
    feeds.Items = new List<NewsItem> { new("Election results are in", "l3", "3") }; Ticks(1);
    Eq(ran.Count, 1, "new matching story");
    Eq(ran[0].ctx["link"], "l3", "link");
});

Test("engine: usb", () =>
{
    Reset(R("usb", new Step("pc.usb.in")));
    world.Drives = new() { }; Ticks(1);
    world.Drives = new() { "E:" }; Ticks(1);
    Eq(ran.Count, 1, "plugged in");
    Eq(ran[0].ctx["drive"], "E:", "drive");
});

Test("engine: meeting soon", () =>
{
    Reset(R("meet", new Step("cal.soon", ("minutes", "5"))));
    mail.MeetingsList = new List<Meeting> { new("Standup", world.Now.AddMinutes(10), world.Now.AddMinutes(25), "") };
    Ticks(2);
    Eq(ran.Count, 0, "10 minutes away");
    world.Now = world.Now.AddMinutes(6); Ticks(3);
    Eq(ran.Count, 1, "fires once inside 5 minutes");
});

Test("engine: unread count", () =>
{
    Reset(R("lots", new Step("mail.unread", ("count", "10"))));
    mail.UnreadCount = 3; Ticks(1);
    mail.UnreadCount = 12; Ticks(2);
    Eq(ran.Count, 1, "fires when reaching 10");
});

Test("engine: test button", () =>
{
    Reset();
    var v = R("t", new Step("mail.new"), new Step("show.card", ("title", "{from}")));
    engine.Test(v);
    Eq(ran.Count, 1, "runs actions");
    Eq(ran[0].ctx["from"], "Jane Doe", "sample sender");
});

// ---------- mail parsing ----------

Test("mime", () =>
{
    Eq(Mime.Decode("=?UTF-8?B?SGVsbG8gV29ybGQ=?="), "Hello World", "base64 word");
    Eq(Mime.Decode("=?utf-8?Q?Caf=C3=A9_time?="), "Café time", "q word");
    Eq(Mime.DisplayName("\"Jane Doe\" <jane@x.com>"), "Jane Doe <jane@x.com>", "display name");
    Eq(Mime.ShortName("Jane Doe <jane@x.com>"), "Jane Doe", "short name");
    Eq(Mime.DisplayName("jane@x.com"), "jane@x.com", "bare address");
});

Test("imap fetch parse", () =>
{
    var lines = new List<string>
    {
        "* 5 FETCH (UID 101 BODY[HEADER.FIELDS (FROM SUBJECT)] {60}",
        "From: Boss <boss@x.com>", "Subject: =?UTF-8?B?UmVwb3J0IGR1ZQ==?=", "", ")",
        "* 6 FETCH (UID 102 BODY[HEADER.FIELDS (FROM SUBJECT)] {40}",
        "Subject: Lunch", "  on Friday", "From: jane@x.com", "", ")",
    };
    var r = Imap.ParseFetch(lines);
    Eq(r[101].from, "Boss <boss@x.com>", "from 1");
    Eq(r[101].subject, "Report due", "subject 1");
    Eq(r[102].subject, "Lunch on Friday", "folded subject");
});

Test("mail hub seeding", () =>
{
    var hub = new MailHub(() => new Settings());
    var got = new List<string>();
    hub.NewMail += m => got.Add(m.Id);
    hub.Take(new[] { new MailItem("a", "x", "1", DateTime.Now), new MailItem("b", "x", "2", DateTime.Now) });
    Eq(got.Count, 0, "first check only learns");
    hub.Take(new[] { new MailItem("a", "x", "1", DateTime.Now), new MailItem("c", "x", "3", DateTime.Now) });
    Eq(string.Join(",", got), "c", "only the new one");
    Eq(hub.Recent[0].Id, "c", "newest first");
    hub.Dispose();
});

Test("feeds parse", () =>
{
    var rss = "<rss><channel><item><title>A &amp; B</title><link>http://a</link><guid>1</guid></item><item><title>C</title><link>http://c</link></item></channel></rss>";
    var r = Feeds.ParseFeed(rss);
    Eq(r.Count, 2, "rss items");
    Eq(r[0].Title, "A & B", "decoded title");
    var atom = "<feed xmlns='http://www.w3.org/2005/Atom'><entry><title>X</title><link href='http://x'/><id>x1</id></entry></feed>";
    var a = Feeds.ParseFeed(atom);
    Eq(a[0].Link, "http://x", "atom link");
    Eq(Feeds.Slug("Area 52"), "area-52", "slug");
    Eq(Feeds.Slug("Kel'Thuzad"), "kelthuzad", "slug apostrophe");
    Eq(Feeds.NamespaceFor("Classic Era", "eu"), "dynamic-classic1x-eu", "namespace");
});

// ---------- live internet (only with: dotnet run --project tests\DeskVibes.Tests -- live) ----------

if (args.Contains("live"))
{
    var live = new Feeds(() => new Settings { Units = "F" });
    WeatherInfo? wx = null; QuoteInfo? q = null; IReadOnlyList<NewsItem>? news = null; SiteInfo? site = null;
    for (int i = 0; i < 40 && (wx == null || q == null || news == null || site == null); i++)
    {
        wx = live.Weather("Boston, MA"); q = live.Quote("AAPL"); news = live.News("https://feeds.bbci.co.uk/news/rss.xml"); site = live.Site("https://www.google.com");
        Thread.Sleep(500);
    }
    Check(wx != null, "live weather " + live.Problem("weather:boston, ma:F"));
    Check(q != null && q.Price > 0, "live quote " + live.Problem("quote:AAPL"));
    Check(news != null && news.Count > 0, "live news");
    Check(site != null && site.Up, "live site");
    if (wx != null) Console.WriteLine($"  weather: {wx.Place} {wx.Temp:0}°{wx.Units} {wx.Text}, {wx.Hours.Count} hours ahead");
    if (q != null) Console.WriteLine($"  quote: {q.Symbol} {q.Price} ({q.ChangePct:+0.00;-0.00}%)");
    if (news != null && news.Count > 0) Console.WriteLine($"  news: {news[0].Title}");
}

Console.WriteLine();
Console.ForegroundColor = failed == 0 ? ConsoleColor.Green : ConsoleColor.Red;
Console.WriteLine($"{passed} passed, {failed} failed");
Console.ResetColor();
return failed == 0 ? 0 : 1;

sealed class FakeWorld : IWorld
{
    public DateTime Now { get; set; } = new(2026, 10, 19, 10, 0, 0);
    public double Idle;
    public double IdleSeconds => Idle;
    public int? Battery { get; set; } = 80;
    public bool? Plugged { get; set; } = true;
    public double Cpu { get; set; }
    public double Memory { get; set; }
    public string App { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Fullscreen { get; set; }
    public HashSet<string> Procs = new();
    public IReadOnlyCollection<string> Processes => Procs;
    public bool Online { get; set; } = true;
    public List<string> Drives = new();
    public IReadOnlyCollection<string> RemovableDrives => Drives;
    public double FreeGb(string drive) => 100;
    public IReadOnlyDictionary<string, double> UsageToday { get; } = new Dictionary<string, double>();
    public double SittingSeconds { get; set; }
    public string BusiestApp => "chrome";
    public void Refresh() { }
}

sealed class FakeFeeds : IFeeds
{
    public WeatherInfo? Wx;
    public Dictionary<string, QuoteInfo> Quotes = new();
    public Dictionary<string, SiteInfo> Sites = new();
    public List<NewsItem>? Items;
    public bool? RealmUp;
    public WeatherInfo? Weather(string place) => Wx;
    public QuoteInfo? Quote(string symbol) => Quotes.GetValueOrDefault(symbol.ToUpperInvariant());
    public SiteInfo? Site(string url) => Sites.GetValueOrDefault(Feeds.NormalizeUrl(url));
    public IReadOnlyList<NewsItem>? News(string url) => Items;
    public RealmInfo? Realm(string region, string game, string realm) => RealmUp == null ? null : new RealmInfo(realm, RealmUp.Value, RealmUp.Value ? "up" : "down", "");
    public string? Problem(string key) => null;
}

sealed class FakeMail : IMail
{
    public int UnreadCount;
    public List<Meeting> MeetingsList = new();
    public int Unread => UnreadCount;
    public IReadOnlyList<MailItem> Recent => Array.Empty<MailItem>();
    public IReadOnlyList<Meeting> Meetings => MeetingsList;
    public string? Problem => null;
    public bool Configured => true;
}

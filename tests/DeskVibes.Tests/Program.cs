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

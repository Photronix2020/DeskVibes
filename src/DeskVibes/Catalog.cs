namespace DeskVibes;

public enum FieldKind { Text, Number, Color, Choice, Check, Time, File, Long }

public sealed record Field(string Key, string Label, FieldKind Kind, string Default = "", string[]? Choices = null, string Hint = "");

/// <summary>A trigger, action, widget or condition the builder can offer. <see cref="Gives"/> lists the {placeholders} it fills.</summary>
public sealed record Def(string Id, string Group, string Label, string Help, Field[] Fields, string[]? Gives = null)
{
    public string[] Gives { get; init; } = Gives ?? Array.Empty<string>();
}

/// <summary>Everything DeskVibes knows how to do. The builder forms are generated from this list.</summary>
public static class Catalog
{
    static Field T(string k, string l, string d = "", string h = "") => new(k, l, FieldKind.Text, d, null, h);
    static Field N(string k, string l, string d, string h = "") => new(k, l, FieldKind.Number, d, null, h);
    static Field C(string k, string l, string d) => new(k, l, FieldKind.Color, d);
    static Field Ch(string k, string l, string d, params string[] c) => new(k, l, FieldKind.Choice, d, c);
    static Field B(string k, string l, bool d = false) => new(k, l, FieldKind.Check, d ? "true" : "false");
    static Field Tm(string k, string l, string d) => new(k, l, FieldKind.Time, d, null, "like 17:30");
    static Field F(string k, string l, string d = "", string h = "") => new(k, l, FieldKind.File, d, null, h);
    static Field L(string k, string l, string d = "", string h = "") => new(k, l, FieldKind.Long, d, null, h);

    public const string GMail = "Email & meetings", GTime = "Time & reminders", GMe = "Me at my PC",
        GPc = "My computer", GApps = "Apps & games", GWeb = "Weather, stocks & web";

    public static readonly Def[] Triggers =
    {
        new("mail.new", GMail, "A new email arrives", "Checks your inbox (set it up in Settings). Leave the boxes empty for any email.",
            new[] { T("from", "From (name or address contains)", "", "e.g. boss@company.com"), T("subject", "Subject contains") },
            new[] { "from", "subject" }),
        new("mail.unread", GMail, "I have lots of unread email", "Fires when your unread count reaches the number.",
            new[] { N("count", "Unread emails", "10") }, new[] { "count" }),
        new("cal.soon", GMail, "A meeting is about to start", "Needs classic Outlook for Windows (Settings).",
            new[] { N("minutes", "Minutes before", "5"), T("subject", "Meeting name contains") }, new[] { "subject", "start", "minutes" }),

        new("time.at", GTime, "At a time of day", "",
            new[] { Tm("time", "Time", "17:00"), Ch("days", "Days", "Weekdays", "Every day", "Weekdays", "Weekends") }),
        new("time.every", GTime, "Every so often", "",
            new[] { N("minutes", "Every how many minutes", "60") }),
        new("time.date", GTime, "On a date (birthday, deadline)", "",
            new[] { T("date", "Date (month-day)", "12-25", "like 03-14"), Tm("time", "Time", "09:00") }),
        new("time.startup", GTime, "When DeskVibes starts", "That's usually when you sign in to Windows.", Array.Empty<Field>()),

        new("pc.idle", GMe, "I've been away for a while", "No mouse or keyboard for this long.",
            new[] { N("minutes", "Minutes away", "10") }, new[] { "away" }),
        new("pc.back", GMe, "I come back to my PC", "After being away at least this long.",
            new[] { N("minutes", "Away at least (minutes)", "15") }, new[] { "away" }),
        new("pc.sitting", GMe, "I've been working without a break", "A break is 5 minutes away from the PC.",
            new[] { N("minutes", "Minutes without a break", "50") }, new[] { "minutes" }),
        new("pc.lock", GMe, "I lock my PC", "", Array.Empty<Field>()),
        new("pc.unlock", GMe, "I unlock my PC", "", Array.Empty<Field>()),
        new("pc.hotkey", GMe, "I press a hotkey", "Works anywhere in Windows.",
            new[] { T("keys", "Keys", "Ctrl+Alt+K", "like Ctrl+Alt+K or Win+Shift+F9") }),
        new("pc.clipboard", GMe, "I copy some text", "",
            new[] { T("contains", "Only if the text contains") }, new[] { "text" }),

        new("pc.battery", GPc, "Battery is getting low", "",
            new[] { N("below", "Below percent", "20") }, new[] { "battery" }),
        new("pc.unplug", GPc, "Charger unplugged", "", Array.Empty<Field>(), new[] { "battery" }),
        new("pc.plug", GPc, "Charger plugged in", "", Array.Empty<Field>(), new[] { "battery" }),
        new("pc.offline", GPc, "Internet goes down", "", Array.Empty<Field>()),
        new("pc.online", GPc, "Internet comes back", "", Array.Empty<Field>()),
        new("pc.usb.in", GPc, "A USB drive is plugged in", "", Array.Empty<Field>(), new[] { "drive" }),
        new("pc.usb.out", GPc, "A USB drive is removed", "", Array.Empty<Field>(), new[] { "drive" }),
        new("pc.cpu", GPc, "The computer is working hard", "CPU stays busy for a while.",
            new[] { N("above", "CPU above percent", "90"), N("seconds", "For seconds", "30") }, new[] { "cpu", "app" }),
        new("pc.memory", GPc, "Memory is nearly full", "",
            new[] { N("above", "Memory above percent", "90") }, new[] { "memory" }),
        new("pc.disk", GPc, "Disk space is low", "",
            new[] { T("drive", "Drive", "C"), N("below", "Free space below (GB)", "10") }, new[] { "free", "drive" }),

        new("app.start", GApps, "An app opens", "Type part of the program name, like spotify, excel or wow.",
            new[] { T("app", "App", "notepad") }, new[] { "app" }),
        new("app.stop", GApps, "An app closes", "",
            new[] { T("app", "App", "notepad") }, new[] { "app" }),
        new("app.focus", GApps, "I switch to an app or window", "Matches the program name or the window title.",
            new[] { T("text", "App or window title contains", "youtube") }, new[] { "app", "title" }),
        new("app.fullscreen", GApps, "Something goes full screen", "A game or a movie.", Array.Empty<Field>(), new[] { "app", "title" }),
        new("app.usedfor", GApps, "I've used an app a long time today", "Counts the time it was in front, today.",
            new[] { T("app", "App", "wow"), N("minutes", "Minutes today", "120") }, new[] { "app", "minutes" }),

        new("web.weather", GWeb, "The weather changes", "Uses Open-Meteo (free, no account). Leave the place empty to use the one in Settings.",
            new[] { Ch("when", "When", "Rain or snow is coming", "Rain or snow is coming", "It gets hotter than", "It gets colder than", "It gets windy"),
                    N("value", "Temperature or wind speed", "32", "for hotter / colder / windy"), N("hours", "Look ahead (hours)", "3"), T("place", "Place") },
            new[] { "temp", "weather", "place", "rain" }),
        new("web.stock", GWeb, "A stock or crypto moves", "Symbols like AAPL, MSFT, ^GSPC, BTC-USD (Yahoo Finance).",
            new[] { T("symbol", "Symbol", "AAPL"), Ch("when", "When", "Moves more than % today", "Goes above", "Goes below", "Moves more than % today"), N("value", "Price or percent", "3") },
            new[] { "symbol", "price", "change" }),
        new("web.down", GWeb, "A website goes down", "Checks every minute.",
            new[] { T("url", "Website", "https://example.com") }, new[] { "url", "status" }),
        new("web.up", GWeb, "A website comes back up", "",
            new[] { T("url", "Website", "https://example.com") }, new[] { "url", "status" }),
        new("web.wow", GWeb, "A WoW realm goes up or down", "Needs a free Blizzard API key in Settings.",
            new[] { T("realm", "Realm", "Stormrage"), Ch("region", "Region", "us", "us", "eu", "kr", "tw"),
                    Ch("game", "Game", "Retail", "Retail", "Classic", "Classic Era", "Anniversary"),
                    Ch("when", "When", "Comes back up", "Comes back up", "Goes down") },
            new[] { "realm", "status" }),
        new("web.rss", GWeb, "A news feed has a new story", "Any RSS or Atom feed.",
            new[] { T("url", "Feed address", "https://feeds.bbci.co.uk/news/rss.xml"), T("contains", "Only if the headline contains") },
            new[] { "title", "link" }),
    };

    public static readonly Def[] Actions =
    {
        new("show.edges", "Show", "Light up the screen edges", "A glow around the edges of every screen. Clicks go straight through it.",
            new[] { C("color", "Color", "#FF3B30"), Ch("style", "Style", "Pulse", "Solid", "Pulse", "Blink"), N("size", "Thickness (pixels)", "26"),
                    N("seconds", "Seconds (0 = until I move the mouse)", "0") }),
        new("show.card", "Show", "Pop up a card", "A little card in the corner. Click it to close it.",
            new[] { T("title", "Title", "{vibe}"), L("text", "Text", ""), C("color", "Color", "#0A84FF"), N("seconds", "Seconds (0 = until I click it)", "10"),
                    Ch("where", "Where", "Bottom right", "Bottom right", "Top right", "Middle"), T("link", "Open this when clicked", "", "website, file or app (optional)") }),
        new("show.banner", "Show", "Show a big banner", "Big text across the top of the screen.",
            new[] { T("text", "Text", "{vibe}"), C("color", "Color", "#FF9500"), N("seconds", "Seconds", "6") }),
        new("show.flash", "Show", "Flash the screen", "A quick flash of color.",
            new[] { C("color", "Color", "#FFFFFF"), N("times", "How many flashes", "2") }),
        new("show.dim", "Show", "Dim the screen", "Darkens everything until you click (good for breaks).",
            new[] { T("text", "Message", "Time for a break"), N("percent", "How dark (percent)", "70"), N("seconds", "Seconds (0 = until I click)", "0") }),
        new("sound.play", "Sound", "Play a sound", "",
            new[] { Ch("sound", "Sound", "Chime", "Chime", "Ding", "Email", "Calendar", "Alert", "Error", "Question", "A sound file"), F("file", "Sound file (.wav)", "", "for 'A sound file'"),
                    N("times", "Times", "1") }),
        new("sound.speak", "Sound", "Say something out loud", "Windows reads the text.",
            new[] { T("text", "Text", "{vibe}") }),
        new("do.open", "Do", "Open a website, file or app", "",
            new[] { F("target", "What to open", "https://", "a link, a file, or a program") }),
        new("do.copy", "Do", "Copy text to the clipboard", "", new[] { L("text", "Text") }),
        new("do.desktop", "Do", "Show the desktop (minimize everything)", "", Array.Empty<Field>()),
        new("do.mute", "Do", "Mute or unmute the sound", "Toggles the mute key.", Array.Empty<Field>()),
        new("do.lock", "Do", "Lock my PC", "", Array.Empty<Field>()),
        new("do.log", "Do", "Write it in the DeskVibes log", "", new[] { T("text", "Text", "{vibe}") }),
    };

    public static readonly Def[] Widgets =
    {
        new("w.clock", "Widgets", "Clock", "",
            new[] { T("zone", "Also show this time zone", "", "e.g. Pacific, London, Tokyo"), B("seconds", "Show seconds"), B("date", "Show the date", true) }),
        new("w.weather", "Widgets", "Weather", "", new[] { T("place", "Place (empty = Settings)") }),
        new("w.tickers", "Widgets", "Stocks & crypto", "", new[] { T("symbols", "Symbols", "AAPL, MSFT, ^GSPC, BTC-USD") }),
        new("w.system", "Widgets", "PC health", "CPU, memory, battery, disk and uptime.", Array.Empty<Field>()),
        new("w.countdown", "Widgets", "Countdown", "",
            new[] { T("title", "Counting down to", "The weekend"), T("target", "When", "Friday 17:00", "a date and time, like 2026-12-25 08:00, or Friday 17:00") }),
        new("w.focus", "Widgets", "Focus timer", "Click it to start or pause. Rings and lights up the edges when time is up.",
            new[] { N("work", "Work minutes", "25"), N("rest", "Break minutes", "5"), C("color", "Color when done", "#30D158") }),
        new("w.note", "Widgets", "Sticky note", "Type right on it.", new[] { C("color", "Paper color", "#FFD60A") }),
        new("w.status", "Widgets", "Status board", "Websites and WoW realms, with a green or red dot.",
            new[] { L("items", "One per line", "https://google.com\nwow:us:Stormrage", "a website, or wow:region:Realm (Classic: wow:us:Realm:classic)") }),
        new("w.clipboard", "Widgets", "Clipboard history", "Click a line to copy it again.", new[] { N("count", "How many", "8") }),
        new("w.screentime", "Widgets", "Screen time today", "Which apps you used most today.", new[] { N("count", "How many apps", "5") }),
        new("w.mail", "Widgets", "Inbox", "Unread count and the newest emails.", Array.Empty<Field>()),
        new("w.meetings", "Widgets", "Today's meetings", "Needs classic Outlook for Windows.", Array.Empty<Field>()),
        new("w.news", "Widgets", "Headlines", "Click a headline to open it.",
            new[] { T("url", "Feed address", "https://feeds.bbci.co.uk/news/rss.xml"), N("count", "How many", "5") }),
        new("w.text", "Widgets", "Text", "Any text, like a quote. {time} and {date} work.",
            new[] { L("text", "Text", "You've got this."), N("size", "Text size", "16") }),
    };

    public static readonly Def[] Conditions =
    {
        new("weekdays", "Only", "On weekdays", "", Array.Empty<Field>()),
        new("weekends", "Only", "On weekends", "", Array.Empty<Field>()),
        new("work", "Only", "During work hours", "Set the hours in Settings.", Array.Empty<Field>()),
        new("notwork", "Only", "Outside work hours", "", Array.Empty<Field>()),
        new("here", "Only", "When I'm at the PC", "Used the mouse or keyboard in the last 2 minutes.", Array.Empty<Field>()),
        new("nogame", "Only", "Not while something is full screen", "Leaves you alone during games and movies.", Array.Empty<Field>()),
        new("battery", "Only", "When on battery", "", Array.Empty<Field>()),
        new("plugged", "Only", "When plugged in", "", Array.Empty<Field>()),
    };

    static readonly Dictionary<string, Def> byId =
        Triggers.Concat(Actions).Concat(Widgets).Concat(Conditions).ToDictionary(d => d.Id);

    public static Def? Find(string id) => id != null && byId.TryGetValue(id, out var d) ? d : null;

    /// <summary>Short sentence for the vibes list, e.g. "A new email arrives (from boss) → Light up the screen edges (red)".</summary>
    public static string Describe(Step s)
    {
        var d = Find(s.Type);
        if (d == null) return s.Type;
        var bits = new List<string>();
        foreach (var f in d.Fields)
        {
            if (!s.P.TryGetValue(f.Key, out var v) || string.IsNullOrWhiteSpace(v)) continue;
            if (f.Kind == FieldKind.Check) { if (v == "true") bits.Add(f.Label.ToLowerInvariant()); continue; }
            if (f.Kind == FieldKind.Long) v = v.Split('\n')[0] + (v.Contains('\n') ? "…" : "");
            if (f.Kind == FieldKind.Color) v = Colors.NameOf(v);
            if (v.Length > 30) v = v[..28] + "…";
            bits.Add(v);
        }
        return bits.Count == 0 ? d.Label : $"{d.Label} ({string.Join(", ", bits)})";
    }

    public static string Summary(Vibe v)
    {
        if (v.IsWidget) return v.Widget == null ? "Widget" : "Widget: " + Describe(v.Widget);
        var then = v.Do.Count == 0 ? "nothing yet" : string.Join(" + ", v.Do.Select(Describe));
        var only = v.Only.Count == 0 ? "" : " · only " + string.Join(", ", v.Only.Select(o => Find(o)?.Label.ToLowerInvariant() ?? o));
        return $"When {Describe(v.When).ToLowerFirst()} → {then.ToLowerFirst()}{only}";
    }

    static string ToLowerFirst(this string s) =>
        s.Length == 0 || s.StartsWith("I ") || s.StartsWith("I'") ? s : char.ToLowerInvariant(s[0]) + s[1..];
}

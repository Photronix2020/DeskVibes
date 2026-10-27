namespace DeskVibes;

/// <summary>Ready-made vibes to start from (the "Ideas" tab).</summary>
public static class Gallery
{
    public sealed record Idea(string Category, string Blurb, Func<Vibe> Make);

    static Vibe Rule(string name, Step when, params Step[] then) => new() { Name = name, Kind = "rule", When = when, Do = then.ToList() };
    static Vibe Rule(string name, Step when, string[] only, int cooldown, params Step[] then) =>
        new() { Name = name, Kind = "rule", When = when, Do = then.ToList(), Only = only.ToList(), CooldownSec = cooldown };
    static Vibe Widget(string name, Step w, int width = 260, string layer = "top") =>
        new() { Name = name, Kind = "widget", Widget = w, Place = new Place { W = width, Layer = layer } };
    static Step S(string type, params (string, string)[] p) => new(type, p);

    public static readonly Idea[] Ideas =
    {
        // email & work
        new("Email & work", "Red glowing edges when your boss emails. Type their address in.",
            () => Rule("Boss email → red edges", S("mail.new", ("from", "boss@company.com")),
                S("show.edges", ("color", "#FF3B30"), ("style", "Pulse")),
                S("show.card", ("title", "Email from {from}"), ("text", "{subject}"), ("color", "#FF3B30"), ("seconds", "0")))),
        new("Email & work", "Soft yellow edges for 8 seconds for any new email.",
            () => Rule("Any email → yellow edges", S("mail.new"), S("show.edges", ("color", "#FFD60A"), ("style", "Solid"), ("seconds", "8"), ("size", "18")))),
        new("Email & work", "Yellow pulsing edges and a card 5 minutes before each meeting (classic Outlook).",
            () => Rule("Meeting soon", S("cal.soon", ("minutes", "5")),
                S("show.edges", ("color", "#FFD60A"), ("style", "Pulse"), ("seconds", "20")),
                S("show.card", ("title", "Meeting at {start}"), ("text", "{subject}"), ("color", "#FFD60A"), ("seconds", "0")),
                S("sound.play", ("sound", "Calendar")))),
        new("Email & work", "A nudge at 5 PM on weekdays to wrap things up.",
            () => Rule("End of the day", S("time.at", ("time", "17:00"), ("days", "Weekdays")),
                S("show.card", ("title", "Wrap it up"), ("text", "It's {time}. Write down tomorrow's first task and call it a day."), ("color", "#BF5AF2"), ("seconds", "0")))),
        new("Email & work", "Boss key: Ctrl+Alt+B hides every window and mutes the sound.",
            () => Rule("Boss key", S("pc.hotkey", ("keys", "Ctrl+Alt+B")), S("do.desktop"), S("do.mute"))),
        new("Email & work", "Ctrl+Alt+L locks the PC and says goodbye.",
            () => Rule("Leaving my desk", S("pc.hotkey", ("keys", "Ctrl+Alt+L")), S("sound.speak", ("text", "See you soon")), S("do.lock"))),

        // health
        new("Health & breaks", "Dims the screen after 50 minutes without a break.",
            () => Rule("Stretch break", S("pc.sitting", ("minutes", "50")),
                S("show.dim", ("text", "Stand up and stretch for a minute"), ("percent", "75")))),
        new("Health & breaks", "A little reminder to drink water every hour during work hours.",
            () => Rule("Drink water", S("time.every", ("minutes", "60")), new[] { "work", "here" }, 0,
                S("show.card", ("title", "💧 Drink some water"), ("text", ""), ("color", "#40C8E0"), ("seconds", "12")))),
        new("Health & breaks", "Every 20 minutes, look at something 20 feet away for 20 seconds.",
            () => Rule("20-20-20 eyes", S("time.every", ("minutes", "20")), new[] { "here", "nogame" }, 0,
                S("show.card", ("title", "Rest your eyes"), ("text", "Look at something far away for 20 seconds."), ("color", "#30D158"), ("seconds", "20")))),
        new("Health & breaks", "Welcome back card after you've been away 15 minutes.",
            () => Rule("Welcome back", S("pc.back", ("minutes", "15")),
                S("show.card", ("title", "Welcome back!"), ("text", "You were away {away}."), ("color", "#7C5CFF"), ("seconds", "8")))),
        new("Games", "Tells you when you've played WoW 2 hours today.",
            () => Rule("Two hours of WoW", S("app.usedfor", ("app", "wow"), ("minutes", "120")),
                S("show.banner", ("text", "That's 2 hours of WoW today — time for a stretch?"), ("color", "#FF9500")))),

        // weather
        new("Weather", "Grab an umbrella: rain or snow in the next 3 hours.",
            () => Rule("Rain coming", S("web.weather", ("when", "Rain or snow is coming"), ("hours", "3")),
                S("show.card", ("title", "☂ Rain on the way"), ("text", "{rain} in {place}"), ("color", "#0A84FF"), ("seconds", "0")))),
        new("Weather", "Freezing outside: below 32°.",
            () => Rule("Freezing", S("web.weather", ("when", "It gets colder than"), ("value", "32")),
                S("show.card", ("title", "❄ It's freezing"), ("text", "{temp} in {place}. Bundle up."), ("color", "#40C8E0"), ("seconds", "0")))),

        // money
        new("Stocks & crypto", "Card and a ding when Apple moves more than 3% in a day.",
            () => Rule("AAPL big move", S("web.stock", ("symbol", "AAPL"), ("when", "Moves more than % today"), ("value", "3")),
                S("show.card", ("title", "{symbol} {change}"), ("text", "Now {price}"), ("color", "#30D158"), ("seconds", "0")), S("sound.play", ("sound", "Ding")))),
        new("Stocks & crypto", "Orange edges when Bitcoin drops below a price.",
            () => Rule("Bitcoin dip", S("web.stock", ("symbol", "BTC-USD"), ("when", "Goes below"), ("value", "60000")),
                S("show.edges", ("color", "#FF9500"), ("style", "Pulse"), ("seconds", "15")),
                S("show.card", ("title", "Bitcoin at {price}"), ("text", "{change} today"), ("color", "#FF9500")))),

        // games & web
        new("Games", "Green edges and a voice when your WoW realm comes back up (Blizzard key in Settings).",
            () => Rule("Realm is back", S("web.wow", ("realm", "Stormrage"), ("region", "us"), ("game", "Retail"), ("when", "Comes back up")),
                S("show.edges", ("color", "#30D158"), ("style", "Pulse")), S("sound.speak", ("text", "{realm} is back up. Time to play!")))),
        new("Games", "Leave me alone while I'm gaming: turns off pop-ups during full screen (add 'Not while something is full screen' to any vibe).",
            () => Rule("Game time", S("app.fullscreen"), S("do.log", ("text", "Started {app}")))),
        new("Internet", "Red banner if your website stops answering.",
            () => Rule("Website down", S("web.down", ("url", "https://example.com")),
                S("show.banner", ("text", "{url} is {status}"), ("color", "#FF3B30"), ("seconds", "10")), S("sound.play", ("sound", "Alert")))),
        new("Internet", "Red edges when the internet drops, green when it's back.",
            () => Rule("Internet down", S("pc.offline"), S("show.edges", ("color", "#FF3B30"), ("style", "Blink"), ("seconds", "6")),
                S("show.card", ("title", "You're offline"), ("text", "The internet connection dropped."), ("color", "#FF3B30"), ("seconds", "0")))),
        new("Internet", "Breaking news about something you care about.",
            () => Rule("News about…", S("web.rss", ("url", "https://feeds.bbci.co.uk/news/rss.xml"), ("contains", "election")),
                S("show.card", ("title", "News"), ("text", "{title}"), ("link", "{link}"), ("color", "#FF375F"), ("seconds", "0")))),

        // pc
        new("My PC", "Battery below 20% and not charging.",
            () => Rule("Low battery", S("pc.battery", ("below", "20")),
                S("show.edges", ("color", "#FF9500"), ("style", "Pulse"), ("seconds", "10")),
                S("show.card", ("title", "Battery at {battery}"), ("text", "Plug in soon."), ("color", "#FF9500")))),
        new("My PC", "Opens the drive when you plug in a USB stick.",
            () => Rule("USB plugged in", S("pc.usb.in"), S("show.card", ("title", "{drive} connected"), ("text", "Click to open it"), ("link", "{drive}"), ("color", "#0A84FF")))),
        new("My PC", "Tells you which app is hogging the CPU.",
            () => Rule("CPU hog", S("pc.cpu", ("above", "90"), ("seconds", "45")), Array.Empty<string>(), 600,
                S("show.card", ("title", "Your PC is working hard"), ("text", "CPU at {cpu}, mostly {app}"), ("color", "#FF9500")))),
        new("My PC", "Warns when drive C has less than 10 GB free.",
            () => Rule("Disk almost full", S("pc.disk", ("drive", "C"), ("below", "10")),
                S("show.card", ("title", "Disk space low"), ("text", "Only {free} left on {drive}"), ("color", "#FF3B30"), ("seconds", "0")))),

        // widgets
        new("Widgets", "A clock with a second time zone.", () => Widget("Clock", S("w.clock", ("zone", "London"), ("date", "true")), 240)),
        new("Widgets", "Weather where you are (set your town in Settings).", () => Widget("Weather", S("w.weather"), 270)),
        new("Widgets", "Live stock and crypto prices.", () => Widget("Markets", S("w.tickers", ("symbols", "AAPL, MSFT, NVDA, ^GSPC, BTC-USD")), 290)),
        new("Widgets", "CPU, memory, graphics card, disks, network speed, ping and the busiest apps.", () => Widget("PC health", S("w.system"), 300)),
        new("Widgets", "Count down to the weekend (or anything).", () => Widget("Weekend in", S("w.countdown", ("title", "The weekend"), ("target", "Friday 17:00")), 250)),
        new("Widgets", "A 25/5 focus timer. Click it to start.", () => Widget("Focus", S("w.focus"), 220)),
        new("Widgets", "A sticky note on your desktop.", () => Widget("Note", S("w.note", ("color", "#FFD60A")), 240, "desktop")),
        new("Widgets", "Websites and WoW realms with green/red dots.", () => Widget("Status", S("w.status", ("items", "https://google.com\nhttps://github.com\nwow:us:Stormrage")), 290)),
        new("Widgets", "Your last 8 copies. Click one to copy it again.", () => Widget("Clipboard", S("w.clipboard"), 300)),
        new("Widgets", "Which apps you used most today.", () => Widget("Screen time", S("w.screentime"), 260)),
        new("Widgets", "Unread count and newest emails.", () => Widget("Inbox", S("w.mail"), 320)),
        new("Widgets", "Today's meetings from classic Outlook.", () => Widget("Today", S("w.meetings"), 300)),
        new("Widgets", "Headlines from any news feed.", () => Widget("Headlines", S("w.news"), 340)),
        new("Widgets", "Your timers and alarms at a glance, with one-click timers.", () => Widget("Timers", S("w.timers"), 270)),
        new("Widgets", "A Stream Deck on your screen: point at it for a grid of buttons that open your programs.", () => Widget("Buttons", S("w.deck"))),
        new("Widgets", "Any text, like a motto.", () => Widget("Motto", S("w.text", ("text", "Ship it."), ("size", "22")), 220, "desktop")),
    };

    /// <summary>What a brand new install starts with: a few widgets and two harmless rules, all ready to switch on.</summary>
    public static List<Vibe> Starter()
    {
        var list = new List<Vibe>();
        var clock = Ideas.First(i => i.Blurb.StartsWith("A clock")).Make(); list.Add(clock);
        var health = Ideas.First(i => i.Blurb.StartsWith("CPU, memory")).Make(); list.Add(health);
        var back = Ideas.First(i => i.Blurb.StartsWith("Welcome back")).Make(); list.Add(back);
        var anyMail = Ideas.First(i => i.Blurb.StartsWith("Soft yellow")).Make(); list.Add(anyMail);
        list.Add(DeckWidget());
        return list;
    }

    /// <summary>The button deck everyone starts with (8 x 2, opens when you point at it).</summary>
    public static Vibe DeckWidget() => Ideas.First(i => i.Blurb.StartsWith("A Stream Deck")).Make();

    /// <summary>Adds the button deck once to setups made before it existed. Deleting it later keeps it gone.</summary>
    public static bool AddDeckOnce(List<Vibe> vibes, Settings settings)
    {
        if (settings.GotDeck) return false;
        settings.GotDeck = true;
        if (vibes.Any(v => v.Widget?.Type == "w.deck")) return true;
        vibes.Add(DeckWidget());
        return true;
    }
}

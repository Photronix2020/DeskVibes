using System.Text.RegularExpressions;

namespace DeskVibes;

/// <summary>Turns a plain sentence ("when I get an email from my boss, turn the screen edges red") into a draft vibe.
/// It's a phrase matcher, not AI: the builder opens with the result so you can check and fix it.</summary>
public static class Describe
{
    public sealed record Result(Vibe Vibe, List<string> Understood, bool GuessedTrigger);

    static readonly RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    static readonly string[] ColorWords = { "red", "orange", "amber", "yellow", "gold", "green", "teal", "cyan", "blue", "purple", "violet", "pink", "white", "gray", "grey" };

    static readonly Dictionary<string, string> Companies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bitcoin"] = "BTC-USD", ["btc"] = "BTC-USD", ["ethereum"] = "ETH-USD", ["eth"] = "ETH-USD", ["dogecoin"] = "DOGE-USD", ["solana"] = "SOL-USD",
        ["apple"] = "AAPL", ["microsoft"] = "MSFT", ["tesla"] = "TSLA", ["nvidia"] = "NVDA", ["amazon"] = "AMZN", ["google"] = "GOOGL",
        ["alphabet"] = "GOOGL", ["meta"] = "META", ["facebook"] = "META", ["netflix"] = "NFLX", ["amd"] = "AMD", ["intel"] = "INTC",
        ["s&p"] = "^GSPC", ["s&p 500"] = "^GSPC", ["dow"] = "^DJI", ["nasdaq"] = "^IXIC", ["gamestop"] = "GME", ["disney"] = "DIS",
    };

    static readonly HashSet<string> NotSymbols = new(StringComparer.Ordinal)
    { "I", "PC", "CPU", "USB", "WOW", "RAM", "OK", "AM", "PM", "A", "IT", "MY", "ME", "TV", "UK", "US", "EU", "AND", "OR", "THE", "IF", "AT", "ON", "TO", "IN", "WIFI", "OS", "GB" };

    public static Result Parse(string sentence)
    {
        var original = (sentence ?? "").Trim();
        var t = " " + original.ToLowerInvariant().Replace("’", "'") + " ";
        var notes = new List<string>();
        var v = new Vibe { Kind = "rule" };

        // ---- when ----
        var (when, span) = FindTrigger(t, original);
        bool guessed = when == null;
        if (when == null) { when = new Step("time.every", ("minutes", "60")); notes.Add("I couldn't tell when it should happen, so I picked every hour. Change it below."); }
        v.When = when;
        notes.Add("When: " + Catalog.Describe(when));

        // the part that isn't the trigger, for finding actions
        var rest = span.length > 0 ? t.Remove(span.start, span.length).Insert(span.start, new string(' ', span.length)) : t;

        // ---- then ----
        var color = FirstColor(rest) ?? FirstColor(t);
        var quoted = Regex.Match(original, "[\"“]([^\"”]+)[\"”]");
        string? said = quoted.Success ? quoted.Groups[1].Value : null;
        if (said == null)
        {
            var m = Regex.Match(original, @"\b(?:saying|that says|which says|says|say|with the (?:text|message))\s*:?\s+(.+?)\s*[.!]?$", I);
            if (m.Success) said = m.Groups[1].Value.Trim().Trim('\'', '"');
        }
        var (defTitle, defText) = CardText(when.Type);
        var found = new List<(int pos, Step step)>();

        void Add(string pattern, Func<Match, Step> make)
        {
            var m = Regex.Match(rest, pattern, I);
            if (m.Success && !found.Any(f => f.step.Type == make(m).Type)) found.Add((m.Index, make(m)));
        }

        Add(@"\b(edges?|borders?|frame|outline|corners?|sides of (the |my )?(screen|monitor))\b", _ =>
        {
            var style = Regex.IsMatch(rest, @"\b(flash|flashing|blink|blinking)\b", I) ? "Blink" : Regex.IsMatch(rest, @"\bsolid\b", I) ? "Solid" : "Pulse";
            var s = new Step("show.edges", ("color", color ?? "#FF3B30"), ("style", style));
            var secs = Number(rest, @"for (\d+) ?(s|sec|secs|seconds)\b");
            if (secs != null) s.P["seconds"] = secs;
            return s;
        });
        if (!found.Any(f => f.step.Type == "show.edges"))
            Add(@"\b(flash|blink) (the |my )?(whole )?(screen|monitor|display)", _ => new Step("show.flash", ("color", color ?? "#FFFFFF")));
        Add(@"\b(dim|darken|black ?out|grey ?out|gray ?out)\b", _ => new Step("show.dim", ("text", said ?? "Time for a break")));
        Add(@"\b(banner|big text|huge text|across the (top|screen))\b", _ => new Step("show.banner", ("text", said ?? defText.Split('\n')[0]), ("color", color ?? "#FF9500")));
        Add(@"\b(pop ?up|popup|notif\w*|card|remind\w*|tell me|alert me|let me know|warn me|show (me )?(a )?(message|note)|nudge)\b", _ =>
            new Step("show.card", ("title", defTitle), ("text", said ?? defText), ("color", color ?? "#0A84FF")));
        Add(@"\b(say|speak|read (it )?(out|aloud)|announce|talk|out loud)\b", _ => new Step("sound.speak", ("text", said ?? SpokenText(when.Type))));
        Add(@"\b(sound|beep|ding|chime|ring|noise|play a)\b", m => new Step("sound.play", ("sound", Regex.IsMatch(rest, @"\b(alarm|loud|urgent)\b", I) ? "Alert" : "Chime")));
        Add(@"\b(?:open|launch|go to|start)\s+(https?://\S+|www\.\S+|[\w-]+\.(?:com|net|org|io|tv|gg)\S*|my email|gmail|youtube|spotify|discord|outlook)", m =>
            new Step("do.open", ("target", OpenTarget(m.Groups[1].Value))));
        Add(@"\block (my |the )?(pc|computer|screen|laptop|workstation)\b", _ => new Step("do.lock"));
        Add(@"\b(mute|unmute|silence)\b", _ => new Step("do.mute"));
        Add(@"\b(minimi[sz]e|hides? (all|everything|my windows)|show (the |my )?desktop|boss key|panic)\b", _ => new Step("do.desktop"));
        Add(@"\b(log it|write it down|keep a log|note it)\b", _ => new Step("do.log", ("text", defText.Replace("\n", " · "))));

        if (found.Count == 0)
        {
            found.Add((0, new Step("show.card", ("title", defTitle), ("text", said ?? defText), ("color", color ?? "#0A84FF"))));
            notes.Add("You didn't say what to do, so it pops up a card.");
        }
        v.Do = found.OrderBy(f => f.pos).Select(f => f.step).ToList();
        foreach (var a in v.Do) notes.Add("Then: " + Catalog.Describe(a));

        // ---- only if ----
        if (Regex.IsMatch(t, @"\b(on )?week ?days\b|monday (to|through|-) friday", I)) v.Only.Add("weekdays");
        else if (Regex.IsMatch(t, @"\bweekends?\b", I)) v.Only.Add("weekends");
        if (Regex.IsMatch(t, @"\b(at work|during work|work hours|working hours|office hours)\b", I)) v.Only.Add("work");
        if (Regex.IsMatch(t, @"\b(not|unless|except) (while |when |if )?(i'?m |i am )?(gaming|playing|in a game|watching|full ?screen)", I)) v.Only.Add("nogame");
        if (Regex.IsMatch(t, @"\b(only )?(when|if) i'?m (here|at (my|the) (pc|desk|computer))\b", I)) v.Only.Add("here");
        foreach (var o in v.Only) notes.Add("Only " + Catalog.Find(o)!.Label.ToLowerInvariant());

        if (Regex.IsMatch(t, @"\bonce an? hour|at most (once )?(an|per|every) hour", I)) v.CooldownSec = 3600;
        else if (Regex.IsMatch(t, @"\bonce a day\b|at most once (a|per) day", I) && !when.Type.StartsWith("time.")) v.CooldownSec = 86400;

        v.Name = NameFor(v);
        return new Result(v, notes, guessed);
    }

    static (Step? step, (int start, int length) span) FindTrigger(string t, string original)
    {
        Match m;
        (Step?, (int, int)) R(Step s, Match mm) => (s, (mm.Index, mm.Length));
        bool Has(string p) => Regex.IsMatch(t, p, I);

        // hotkey
        m = Regex.Match(t, @"\b(?:press(?:ing)?\s+)?((?:ctrl|control|alt|shift|win)\s*\+\s*(?:(?:ctrl|control|alt|shift|win)\s*\+\s*)*[a-z0-9]+)\b", I);
        if (m.Success)
        {
            var keys = string.Join("+", m.Groups[1].Value.Split('+').Select(k => k.Trim()).Select(k => k.Length == 1 ? k.ToUpperInvariant() : char.ToUpperInvariant(k[0]) + k[1..]));
            return R(new Step("pc.hotkey", ("keys", keys)), m);
        }

        // email
        m = Regex.Match(t, @"\b(e-?mails?|mail|inbox|gmail|outlook messages?)\b", I);
        if (m.Success && !Has(@"\bopen (my )?(e-?mail|gmail|outlook)"))
        {
            int start = m.Index, end = m.Index + m.Length;
            var count = Number(t, @"(\d+)\s+(unread|e-?mails|messages)");
            if (Has(@"\bunread\b") && count != null)
                return (new Step("mail.unread", ("count", count)), (start, end - start));
            var s = new Step("mail.new");
            var from = Regex.Match(original, @"\bfrom\s+(?:my\s+|the\s+|a\s+)?([\w.+@'-]+(?-i:\s+[A-Z][\w'-]+)?)", I);
            if (from.Success)
            {
                s.P["from"] = Regex.Replace(from.Groups[1].Value.Trim().TrimEnd('.', ','), "'s?$", "");
                end = Math.Max(end, from.Index + from.Length);
            }
            var subj = Regex.Match(original, @"\b(?:subject|about|titled|mentioning|with)\s+[""']?([^""',.]+?)[""']?(?=[,.]|\s+(?:turn|make|show|flash|play|say|light|then|pop)|$)", I);
            if (subj.Success && !Regex.IsMatch(subj.Groups[1].Value, @"\b(red|yellow|edges?|screen)\b", I)) s.P["subject"] = subj.Groups[1].Value.Trim();
            return (s, (start, Math.Max(0, end - start)));
        }

        // meetings
        m = Regex.Match(t, @"\b(meetings?|calendar|appointments?|calls? (start|is about))\b", I);
        if (m.Success) return R(new Step("cal.soon", ("minutes", Number(t, @"(\d+)\s*(min|minutes|mins)\b") ?? "5")), m);

        // weather
        m = Regex.Match(t, @"\b(rain\w*|snow\w*|umbrella|storms?|thunder\w*|drizzle)\b", I);
        if (m.Success) return R(Weather("Rain or snow is coming", null, original), m);
        m = Regex.Match(t, @"\b(hot|heat|warm(er)?|above \d+ ?(°|degrees))\b", I);
        if (m.Success && Has(@"\b(weather|outside|degrees|°|temperature|hot out|heat wave)\b"))
            return R(Weather("It gets hotter than", Number(t, @"(\d+)\s*(°|degrees)") ?? "90", original), m);
        m = Regex.Match(t, @"\b(cold|freez\w*|frost\w*|icy|below \d+ ?(°|degrees))\b", I);
        if (m.Success && Has(@"\b(weather|outside|degrees|°|temperature|freez\w*|frost|icy|cold out)\b"))
            return R(Weather("It gets colder than", Number(t, @"(\d+)\s*(°|degrees)") ?? "32", original), m);
        m = Regex.Match(t, @"\b(windy|wind)\b", I);
        if (m.Success) return R(Weather("It gets windy", Number(t, @"(\d+)\s*(mph|km)") ?? "25", original), m);

        // WoW realms
        m = Regex.Match(t, @"\b(realm|wow server|warcraft server|wow is (down|up|back)|servers? (are|is) (back|up|down))\b", I);
        if (m.Success && (Has(@"\b(wow|warcraft|realm)\b")))
        {
            var realm = Regex.Match(original, @"\b(?:realm|server)\s+([A-Z][\w'-]+(?:\s+[A-Z][\w'-]+)?)");
            if (!realm.Success) realm = Regex.Match(original, @"\b([A-Z][\w'-]+)\s+(?:realm|server)\b");
            var s = new Step("web.wow", ("realm", realm.Success ? realm.Groups[1].Value : "Stormrage"),
                ("when", Has(@"\b(goes down|go down|is down|offline|crash)") ? "Goes down" : "Comes back up"),
                ("game", Has(@"\bclassic era\b|\bera\b") ? "Classic Era" : Has(@"\banniversary\b") ? "Anniversary" : Has(@"\bclassic\b") ? "Classic" : "Retail"),
                ("region", Has(@"\beu\b|europe") ? "eu" : "us"));
            return R(s, m);
        }

        // stocks
        m = Regex.Match(t, @"\b(stocks?|shares?|ticker|crypto\w*|bitcoin|btc|ethereum|eth|dogecoin|solana|s&p|nasdaq|dow jones|market)\b", I);
        var sym = Regex.Match(original, @"\$([A-Za-z]{1,5}(?:-USD)?)\b|\b([A-Z]{2,5}(?:-USD)?)\b");
        string? symbol = null;
        foreach (Match x in Regex.Matches(original, @"\$([A-Za-z]{1,5}(?:-USD)?)\b|\b([A-Z]{2,5}(?:-USD)?)\b"))
        {
            var cand = (x.Groups[1].Success ? x.Groups[1].Value : x.Groups[2].Value).ToUpperInvariant();
            if (x.Groups[1].Success || !NotSymbols.Contains(cand)) { symbol = cand; break; }
        }
        foreach (var (name, s) in Companies)
            if (symbol == null && Regex.IsMatch(t, $@"\b{Regex.Escape(name)}\b", I)) symbol = s;
        if (m.Success || (symbol != null && Has(@"\b(price|above|below|over|under|drops?|jumps?|moves?|hits?|reaches)\b")))
        {
            var s = new Step("web.stock", ("symbol", symbol ?? "AAPL"));
            var price = Number(t, @"(?:above|over|hits|reaches|passes|exceeds|below|under|drops (?:to|below)|falls (?:to|below))\s*\$?([\d,.]+)");
            if (Has(@"(\d+(\.\d+)?)\s*(%|percent)")) { s.P["when"] = "Moves more than % today"; s.P["value"] = Number(t, @"([\d.]+)\s*(%|percent)")!; }
            else if (price != null && Has(@"\b(below|under|drops|falls)\b")) { s.P["when"] = "Goes below"; s.P["value"] = price; }
            else if (price != null) { s.P["when"] = "Goes above"; s.P["value"] = price; }
            else { s.P["when"] = "Moves more than % today"; s.P["value"] = "3"; }
            return (s, m.Success ? (m.Index, m.Length) : (0, 0));
        }

        // websites
        m = Regex.Match(t, @"(https?://\S+|\b[\w-]+(\.[\w-]+)*\.(com|net|org|io|gov|edu|dev|gg|tv|co)\b\S*)", I);
        if (m.Success && Has(@"\b(down|offline|up|back|working|unreachable|crash)\b"))
        {
            var url = Feeds.NormalizeUrl(m.Value.TrimEnd('.', ',', '!'));
            bool up = Has(@"\b(back up|comes back|is back|up again|back online|working again)\b");
            return R(new Step(up ? "web.up" : "web.down", ("url", url)), m);
        }

        // news
        m = Regex.Match(t, @"\b(news|headlines?|rss|feed|articles?|story|stories)\b", I);
        if (m.Success)
        {
            var about = Regex.Match(original, @"\b(?:about|mentions?|mentioning)\s+([\w' -]+?)(?=[,.]|\s+(?:show|pop|tell|then)|$)", I);
            return R(new Step("web.rss", ("contains", about.Success ? about.Groups[1].Value.Trim() : "")), m);
        }

        // battery & power
        m = Regex.Match(t, @"\bunplug\w*|\bcharger (is )?(removed|unplugged|disconnected)|on battery\b", I);
        if (m.Success && !Has(@"\b(usb|drive|thumb)\b")) return R(new Step("pc.unplug"), m);
        m = Regex.Match(t, @"\bbattery\b", I);
        if (m.Success) return R(new Step("pc.battery", ("below", Number(t, @"(\d+)\s*(%|percent)") ?? "20")), m);
        m = Regex.Match(t, @"\b(plug(ged)? in|charger|charging)\b", I);
        if (m.Success && !Has(@"\b(usb|drive|thumb)\b")) return R(new Step("pc.plug"), m);

        // internet
        m = Regex.Match(t, @"\b(internet|wi-?fi|connection|network|online|offline)\b", I);
        if (m.Success) return R(new Step(Has(@"\b(comes back|is back|back on|back online|reconnect\w*|returns)\b") ? "pc.online" : "pc.offline"), m);

        // usb drives
        m = Regex.Match(t, @"\b(usb|thumb ?drive|flash ?drive|memory stick|sd card|external drive)\b", I);
        if (m.Success) return R(new Step(Has(@"\b(remove|unplug|eject|pull)\w*") ? "pc.usb.out" : "pc.usb.in"), m);

        // computer health
        m = Regex.Match(t, @"\b(cpu|processor|computer is slow|pc is slow|working hard|fans?)\b", I);
        if (m.Success) return R(new Step("pc.cpu", ("above", Number(t, @"(\d+)\s*(%|percent)") ?? "90")), m);
        m = Regex.Match(t, @"\b(memory|ram)\b", I);
        if (m.Success) return R(new Step("pc.memory", ("above", Number(t, @"(\d+)\s*(%|percent)") ?? "90")), m);
        m = Regex.Match(t, @"\b(disk|storage|hard drive|drive space|space is low|running out of space)\b", I);
        if (m.Success) return R(new Step("pc.disk", ("below", Number(t, @"(\d+)\s*(gb|gigs?)") ?? "10")), m);

        // clipboard
        m = Regex.Match(t, @"\b(i copy|copied|clipboard)\b", I);
        if (m.Success) return R(new Step("pc.clipboard"), m);

        // lock / unlock
        m = Regex.Match(t, @"\b(when|after|whenever|if) i unlock\b|\bunlock (my )?(pc|computer)\b.*\b(then|show|pop)", I);
        if (m.Success) return R(new Step("pc.unlock"), m);
        m = Regex.Match(t, @"\b(when|after|whenever|if) i lock\b", I);
        if (m.Success) return R(new Step("pc.lock"), m);

        // apps
        m = Regex.Match(t, @"\b(?:played|playing|used|using|been on|been in|spent)\s+([\w.-]+)\s+(?:for\s+)?(?:more than\s+|over\s+)?(\d+)\s*(hours?|hrs?|minutes?|mins?)", I);
        if (m.Success)
        {
            var n = int.Parse(m.Groups[2].Value) * (m.Groups[3].Value.StartsWith("h") ? 60 : 1);
            return R(new Step("app.usedfor", ("app", AppName(m.Groups[1].Value)), ("minutes", n.ToString())), m);
        }
        m = Regex.Match(t, @"\b(full ?screen|start a game|launch a game|gaming|watching a (movie|video))\b", I);
        if (m.Success) return R(new Step("app.fullscreen"), m);
        m = Regex.Match(t, @"\b(?:when|whenever|if|after)\s+(?:i\s+)?(?:open|start|launch|run)\s+([\w.-]+)", I);
        if (m.Success) return R(new Step("app.start", ("app", AppName(m.Groups[1].Value))), m);
        m = Regex.Match(t, @"\b(?:when|whenever|if|after)\s+(?:i\s+)?(?:close|quit|exit)\s+([\w.-]+)", I);
        if (m.Success) return R(new Step("app.stop", ("app", AppName(m.Groups[1].Value))), m);
        m = Regex.Match(t, @"\b(?:when|whenever|if)\s+(?:i\s+)?(?:switch to|go on|go to|am on|'m on|visit|watch)\s+([\w.-]+)", I);
        if (m.Success) return R(new Step("app.focus", ("text", m.Groups[1].Value)), m);

        // me
        m = Regex.Match(t, @"\b(come back|get back|return|i'?m back)\b", I);
        if (m.Success) return R(new Step("pc.back", ("minutes", Number(t, @"(\d+)\s*(min|minutes|mins)") ?? "15")), m);
        m = Regex.Match(t, @"\b(break|sitting|been working|stretch|stand up|get up|walk around|rest my eyes|eye break|posture)\b", I);
        if (m.Success && !Has(@"\bevery\b"))
            return R(new Step("pc.sitting", ("minutes", Minutes(t) ?? "50")), m);
        m = Regex.Match(t, @"\b(away|idle|leave my desk|left my desk|walk away|not at my (pc|desk|computer))\b", I);
        if (m.Success) return R(new Step("pc.idle", ("minutes", Minutes(t) ?? "10")), m);

        // time
        m = Regex.Match(t, @"\b(?:every|each)\s+(\d+)?\s*(minutes?|mins?|hours?|hrs?|half hour)\b|\bhourly\b", I);
        if (m.Success)
        {
            int n = m.Value.Contains("hourly") ? 60 : m.Value.Contains("half") ? 30 : (m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 1) * (m.Groups[2].Value.StartsWith("h") ? 60 : 1);
            return R(new Step("time.every", ("minutes", n.ToString())), m);
        }
        m = Regex.Match(t, @"\bat\s+(\d{1,2})(?::(\d\d))?\s*(am|pm|a\.m\.|p\.m\.)?|\bat noon\b|\bat midnight\b|\b(end of (the )?(work ?)?day|quitting time|lunch ?time)\b", I);
        if (m.Success)
        {
            string time;
            if (m.Value.Contains("noon") || m.Value.Contains("lunch")) time = "12:00";
            else if (m.Value.Contains("midnight")) time = "00:00";
            else if (m.Value.Contains("end of") || m.Value.Contains("quitting")) time = "17:00";
            else
            {
                int h = int.Parse(m.Groups[1].Value); int min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
                var ap = m.Groups[3].Value.Replace(".", "");
                if (ap == "pm" && h < 12) h += 12; if (ap == "am" && h == 12) h = 0;
                if (ap == "" && h >= 1 && h <= 6) h += 12; // "at 5" probably means 5 PM
                time = $"{h % 24:00}:{min:00}";
            }
            var days = Has(@"\bweekends?\b") ? "Weekends" : Has(@"\b(every ?day|daily|each day)\b") ? "Every day" : "Weekdays";
            return R(new Step("time.at", ("time", time), ("days", days)), m);
        }
        m = Regex.Match(t, @"\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?\s+(\d{1,2})\b", I);
        if (m.Success)
        {
            var month = Array.IndexOf(new[] { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" }, m.Groups[1].Value[..3].ToLowerInvariant()) + 1;
            return R(new Step("time.date", ("date", $"{month:00}-{int.Parse(m.Groups[2].Value):00}")), m);
        }
        m = Regex.Match(t, @"\b(when (my )?(pc|computer) (starts|turns on|boots)|(at|on) (start ?up|log ?in|sign ?in)|when i (log|sign) in|first thing)\b", I);
        if (m.Success) return R(new Step("time.startup"), m);

        return (null, (0, 0));
    }

    static Step Weather(string when, string? value, string original)
    {
        var s = new Step("web.weather", ("when", when));
        if (value != null) s.P["value"] = value;
        var place = Regex.Match(original, @"\b(?:in|for|at)\s+([A-Z][a-zA-Z.]+(?:[ ,]+[A-Z][a-zA-Z.]+)*)");
        if (place.Success && !Regex.IsMatch(place.Groups[1].Value, @"^(The|My|I)\b")) s.P["place"] = place.Groups[1].Value.Trim();
        var hours = Number(original, @"(\d+)\s*(hours?|hrs?)");
        if (hours != null) s.P["hours"] = hours;
        return s;
    }

    static string? Number(string t, string pattern)
    {
        var m = Regex.Match(t, pattern, I);
        return m.Success ? m.Groups[1].Value.Replace(",", "").TrimEnd('.') : null;
    }

    static string? Minutes(string t)
    {
        var m = Regex.Match(t, @"(\d+(?:\.\d+)?)\s*(hours?|hrs?|minutes?|mins?)\b|\b(an|one|half an?) hour\b", I);
        if (!m.Success) return null;
        if (m.Groups[3].Success) return m.Groups[3].Value.StartsWith("half") ? "30" : "60";
        var n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return ((int)(m.Groups[2].Value.StartsWith("h") ? n * 60 : n)).ToString();
    }

    static string AppName(string word) => word.ToLowerInvariant() switch
    {
        "world" or "warcraft" or "wow" => "wow",
        "word" => "winword",
        "chrome" or "google" => "chrome",
        "edge" => "msedge",
        "teams" => "teams",
        "vs" or "visual" => "devenv",
        "code" or "vscode" => "code",
        var w => w,
    };

    static string OpenTarget(string what) => what.ToLowerInvariant() switch
    {
        "my email" or "gmail" => "https://mail.google.com",
        "youtube" => "https://youtube.com",
        "spotify" => "spotify:",
        "discord" => "discord:",
        "outlook" => "outlook",
        var w when w.StartsWith("http") => what,
        _ => "https://" + what.TrimEnd('.', ','),
    };

    static string? FirstColor(string t)
    {
        var best = -1; string? pick = null;
        foreach (var c in ColorWords)
        {
            var m = Regex.Match(t, $@"\b{c}\b", I);
            if (m.Success && (best < 0 || m.Index < best)) { best = m.Index; pick = c; }
        }
        return pick switch
        {
            null => null, "amber" => "Orange", "gold" => "Yellow", "cyan" => "Teal", "violet" => "Purple", "grey" => "Gray",
            _ => Colors.Named.First(n => n.name.Equals(pick, StringComparison.OrdinalIgnoreCase)).hex,
        };
    }

    /// <summary>A sensible card title and text for each trigger, using its {placeholders}.</summary>
    public static (string title, string text) CardText(string trigger) => trigger switch
    {
        "mail.new" => ("New email from {from}", "{subject}"),
        "mail.unread" => ("Inbox", "You have {count} unread emails"),
        "cal.soon" => ("Meeting at {start}", "{subject} starts in {minutes} minutes"),
        "web.weather" => ("Weather in {place}", "{weather}, {temp}. {rain}"),
        "web.stock" => ("{symbol} {change}", "Now {price}"),
        "web.down" => ("Website down", "{url} is {status}"),
        "web.up" => ("Website is back", "{url} is up again"),
        "web.wow" => ("{realm} is {status}", "Realm status changed"),
        "web.rss" => ("News", "{title}"),
        "pc.battery" => ("Battery low", "{battery} left. Time to plug in."),
        "pc.unplug" => ("On battery", "Charger unplugged ({battery})"),
        "pc.plug" => ("Charging", "Plugged in ({battery})"),
        "pc.offline" => ("You're offline", "The internet connection dropped"),
        "pc.online" => ("Back online", "The internet is back"),
        "pc.usb.in" => ("USB drive", "{drive} is plugged in"),
        "pc.usb.out" => ("USB drive", "{drive} was removed"),
        "pc.cpu" => ("Your PC is working hard", "CPU at {cpu} ({app})"),
        "pc.memory" => ("Memory nearly full", "{memory} used"),
        "pc.disk" => ("Disk space low", "{free} free on {drive}"),
        "pc.idle" => ("You've been away", "{away}"),
        "pc.back" => ("Welcome back!", "You were away {away}"),
        "pc.sitting" => ("Time for a break", "You've been at it {minutes} minutes. Stand up and stretch."),
        "pc.clipboard" => ("Copied", "{text}"),
        "app.start" => ("{app} opened", ""),
        "app.stop" => ("{app} closed", ""),
        "app.usedfor" => ("{app}: {minutes} minutes today", "Maybe take a break?"),
        "app.fullscreen" => ("Full screen", "{app}"),
        "time.at" or "time.every" or "time.date" => ("{vibe}", "It's {time}"),
        _ => ("{vibe}", ""),
    };

    static string SpokenText(string trigger) => trigger switch
    {
        "mail.new" => "New email from {from}",
        "web.wow" => "{realm} is {status}",
        "web.stock" => "{symbol} is at {price}",
        "pc.battery" => "Battery is at {battery}",
        "pc.sitting" => "Time to take a break",
        "cal.soon" => "{subject} starts in {minutes} minutes",
        _ => "{vibe}",
    };

    static string NameFor(Vibe v)
    {
        var w = v.When;
        string when = w.Type switch
        {
            "mail.new" => w.Get("from").Length > 0 ? $"Email from {w.Get("from")}" : "New email",
            "web.stock" => $"{w.Get("symbol").ToUpperInvariant()} alert",
            "web.wow" => $"{w.Get("realm")} realm",
            "web.weather" => w.Get("when") switch { "It gets hotter than" => "Heat alert", "It gets colder than" => "Cold alert", "It gets windy" => "Wind alert", _ => "Rain alert" },
            "pc.hotkey" => w.Get("keys"),
            "app.start" or "app.stop" or "app.usedfor" => w.Get("app").Length > 0 ? char.ToUpperInvariant(w.Get("app")[0]) + w.Get("app")[1..] : "App",
            "time.at" => "At " + w.Get("time"),
            _ => Catalog.Find(w.Type)!.Label,
        };
        var first = v.Do.FirstOrDefault();
        string then = first?.Type switch
        {
            "show.edges" => $"{Colors.NameOf(first.Get("color"))} edges",
            "show.card" => "card",
            "show.banner" => "banner",
            "show.dim" => "dim",
            "show.flash" => "flash",
            "sound.speak" => "say it",
            "sound.play" => "sound",
            "do.open" => "open",
            "do.lock" => "lock",
            "do.desktop" => "hide all",
            "do.mute" => "mute",
            _ => "",
        };
        return then.Length > 0 ? $"{when} → {then}" : when;
    }
}

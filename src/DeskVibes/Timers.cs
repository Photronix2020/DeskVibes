using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DeskVibes;

/// <summary>A countdown timer or an alarm at a time of day. Saved with the settings, so they survive a restart.</summary>
public sealed class TimerItem
{
    public string Id { get; set; } = Vibe.NewId();
    /// <summary>"timer" or "alarm".</summary>
    public string Kind { get; set; } = "timer";
    public string Label { get; set; } = "";
    /// <summary>When it rings next.</summary>
    public DateTime Due { get; set; }
    /// <summary>A timer's length, for "again" and the list.</summary>
    public int Seconds { get; set; }
    /// <summary>An alarm's time of day, like 07:30.</summary>
    public string Time { get; set; } = "";
    /// <summary>"Once", "Every day", "Weekdays" or "Weekends".</summary>
    public string Days { get; set; } = "Once";
    /// <summary>Alarms can be switched off and on again; a one-time alarm switches itself off after ringing.</summary>
    public bool On { get; set; } = true;

    [JsonIgnore] public bool IsAlarm => Kind == "alarm";
}

/// <summary>The running timers and alarms, and the plain-words logic around them (no UI here, so it's testable).</summary>
public static class Timers
{
    public static readonly string[] Repeats = { "Once", "Every day", "Weekdays", "Weekends" };
    public static readonly int[] QuickMinutes = { 1, 3, 5, 10, 15, 25, 30, 45, 60 };

    /// <summary>The live list (the app points this at its settings).</summary>
    public static List<TimerItem> Items { get; set; } = new();
    /// <summary>Something was added, removed or changed: time to save.</summary>
    public static event Action? Changed;
    /// <summary>Opens the timer window ("timer" or "alarm"); set by the app.</summary>
    public static Action<string>? ShowDialog;

    public static void Add(TimerItem t) { Items.Add(t); Changed?.Invoke(); }
    public static void Remove(string id) { if (Items.RemoveAll(t => t.Id == id) > 0) Changed?.Invoke(); }
    public static void Touch() => Changed?.Invoke();

    /// <summary>Running timers first (soonest first), then alarms that are on, then alarms that are off.</summary>
    public static IEnumerable<TimerItem> Sorted(IEnumerable<TimerItem> items) =>
        items.OrderBy(t => t.IsAlarm ? (t.On ? 1 : 2) : 0).ThenBy(t => t.On ? t.Due : DateTime.MaxValue).ThenBy(t => t.Time);

    // ---------- making them ----------

    public static TimerItem Timer(TimeSpan length, string label, DateTime now)
    {
        if (length < TimeSpan.FromSeconds(1)) length = TimeSpan.FromSeconds(1);
        if (length > TimeSpan.FromDays(7)) length = TimeSpan.FromDays(7);
        return new TimerItem { Kind = "timer", Label = label.Trim(), Seconds = (int)length.TotalSeconds, Due = now + length };
    }

    public static TimerItem Alarm(TimeSpan timeOfDay, string days, string label, DateTime now)
    {
        timeOfDay = new TimeSpan(timeOfDay.Hours, timeOfDay.Minutes, 0);
        if (!Repeats.Contains(days)) days = "Once";
        return new TimerItem
        {
            Kind = "alarm", Label = label.Trim(), Time = $"{timeOfDay.Hours:00}:{timeOfDay.Minutes:00}", Days = days,
            Due = NextAlarm(timeOfDay, days, now),
        };
    }

    public static bool DayMatches(string days, DayOfWeek d) => days switch
    {
        "Weekdays" => d is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
        "Weekends" => d is DayOfWeek.Saturday or DayOfWeek.Sunday,
        _ => true,
    };

    /// <summary>The first time after <paramref name="after"/> that the alarm should ring.</summary>
    public static DateTime NextAlarm(TimeSpan timeOfDay, string days, DateTime after)
    {
        for (int i = 0; i < 8; i++)
        {
            var at = after.Date.AddDays(i) + timeOfDay;
            if (at > after && DayMatches(days, at.DayOfWeek)) return at;
        }
        return after.Date.AddDays(1) + timeOfDay;
    }

    public static TimeSpan TimeOf(TimerItem alarm) =>
        TimeSpan.TryParseExact(alarm.Time, @"hh\:mm", CultureInfo.InvariantCulture, out var t) ? t : TimeSpan.FromHours(7);

    /// <summary>Switches an alarm on or off; switching on picks its next time.</summary>
    public static void SetOn(TimerItem alarm, bool on, DateTime now)
    {
        alarm.On = on;
        if (on) alarm.Due = NextAlarm(TimeOf(alarm), alarm.Days, now);
    }

    /// <summary>A new short timer after the one that just rang.</summary>
    public static TimerItem Snooze(TimerItem rang, int minutes, DateTime now) =>
        Timer(TimeSpan.FromMinutes(minutes), rang.Label.Length > 0 ? rang.Label : rang.IsAlarm ? "Alarm " + Clock(TimeOf(rang)) : "Timer", now);

    // ---------- ringing ----------

    /// <summary>Takes out everything that's due. Timers are removed, one-time alarms switch off, repeating alarms move on.
    /// Anything more than 10 minutes late (the PC was asleep or DeskVibes was closed) goes to <paramref name="missed"/> instead.</summary>
    public static List<TimerItem> TakeDue(List<TimerItem> items, DateTime now, List<TimerItem>? missed = null)
    {
        var ring = new List<TimerItem>();
        foreach (var t in items.Where(t => t.On && t.Due <= now).ToList())
        {
            if (now - t.Due > TimeSpan.FromMinutes(10)) missed?.Add(t); else ring.Add(t);
            if (!t.IsAlarm) items.Remove(t);
            else if (t.Days == "Once") t.On = false;
            else t.Due = NextAlarm(TimeOf(t), t.Days, now);
        }
        return ring;
    }

    // ---------- words ----------

    public static string Clock(TimeSpan t) => DateTime.Today.Add(t).ToString("t", CultureInfo.CurrentCulture);

    /// <summary>"4:59" or "1:02:03" for a timer; "7:30 AM", "Tomorrow 7:30 AM" or "Mon 7:30 AM" for an alarm.</summary>
    public static string Left(TimerItem t, DateTime now)
    {
        if (t.IsAlarm)
        {
            if (!t.On) return "off";
            var when = Clock(t.Due.TimeOfDay);
            return t.Due.Date == now.Date ? when : t.Due.Date == now.Date.AddDays(1) ? "Tomorrow " + when : t.Due.ToString("ddd ", CultureInfo.CurrentCulture) + when;
        }
        var left = t.Due - now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        left = TimeSpan.FromSeconds(Math.Ceiling(left.TotalSeconds));
        return left.TotalHours >= 1 ? $"{(int)left.TotalHours}:{left.Minutes:00}:{left.Seconds:00}" : $"{left.Minutes}:{left.Seconds:00}";
    }

    /// <summary>"10 minutes", "1 hour 30 minutes", "45 seconds".</summary>
    public static string Length(int seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        var bits = new List<string>();
        if ((int)t.TotalHours > 0) bits.Add(Plural((int)t.TotalHours, "hour"));
        if (t.Minutes > 0) bits.Add(Plural(t.Minutes, "minute"));
        if (t.Seconds > 0 || bits.Count == 0) bits.Add(Plural(t.Seconds, "second"));
        return string.Join(" ", bits);
    }

    static string Plural(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    /// <summary>The name shown in lists: the label, or "10 minute timer" / "Alarm 7:30 AM".</summary>
    public static string Name(TimerItem t) =>
        t.Label.Length > 0 ? t.Label : t.IsAlarm ? "Alarm" : Length(t.Seconds) + " timer";

    public static string RepeatText(TimerItem a) => a.Days switch { "Once" => "", "Every day" => "every day", _ => a.Days.ToLowerInvariant() };

    // ---------- understanding a sentence ----------

    static readonly RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    const string Dur = @"(?:(?:\d+(?:\.\d+)?|an?|one|half an?)\s*(?:hours?|hrs?|h|minutes?|mins?|m|seconds?|secs?|s)\b(?:\s*(?:and\s+)?)?)+";

    /// <summary>"set a 10 minute timer for the pizza", "remind me in 20 minutes to check the oven", "wake me up at 7 on weekdays",
    /// "alarm 6:45pm call mom". Null when the sentence isn't about a timer or an alarm (then it's a normal vibe).</summary>
    public static TimerItem? Quick(string sentence, DateTime now)
    {
        var s = " " + (sentence ?? "").Trim().Replace("’", "'") + " ";
        bool timerWord = Regex.IsMatch(s, @"\b(timer|countdown|count down)\b", I);
        bool alarmWord = Regex.IsMatch(s, @"\b(alarm|wake me)\b", I);
        var dur = Regex.Match(s, @"\b(?:in|for)\s+(" + Dur + ")|(" + Dur + @")(?=\s*(?:-\s*)?(?:timer|countdown))|\btimer\s+(?:for\s+|of\s+)?(" + Dur + ")", I);
        bool remindIn = Regex.IsMatch(s, @"\b(remind|tell|alert|ping|nudge|buzz)\s+me\b", I) && Regex.IsMatch(s, @"\bin\s+" + Dur, I);
        var length = dur.Success ? ParseLength(dur.Value) : null;

        // a clock time: "at 7", "7:30", "6pm", "noon"
        const string Clk = @"(\d{1,2})(?::(\d\d))?\s*(am|pm|a\.m\.|p\.m\.)?(?=[\s,.!]|$)";
        var m = Regex.Match(s, @"\bat\s+" + Clk + @"|\b(noon|midnight)\b", I);
        if (!m.Success)
        {
            var bare = Regex.Match(s, @"\b(?:for|to)?\s*" + Clk, I);
            if (bare.Success && (bare.Groups[2].Success || bare.Groups[3].Success)) m = bare;
        }
        bool clock = m.Success;

        if ((timerWord || remindIn || (alarmWord && !clock)) && length != null)
        {
            var rest = s.Remove(dur.Index, dur.Length).Insert(dur.Index, " ");
            return Timer(length.Value, LabelFrom(rest, s[(dur.Index + dur.Length)..]), now);
        }

        bool remindAt = Regex.IsMatch(s, @"\b(remind|tell|alert|nudge)\s+me\b", I) && clock && Regex.IsMatch(s, @"\bat\s+(\d|noon|midnight)", I);
        if (!(alarmWord || remindAt) || !clock) return null;
        TimeSpan tod;
        if (m.Groups[4].Success) tod = m.Groups[4].Value.Equals("noon", StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromHours(12) : TimeSpan.Zero;
        else
        {
            int h = int.Parse(m.Groups[1].Value), min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            if (h > 23 || min > 59) return null;
            var ap = m.Groups[3].Value.Replace(".", "").ToLowerInvariant();
            if (ap == "pm" && h < 12) h += 12;
            if (ap == "am" && h == 12) h = 0;
            if (ap == "" && h <= 12)
            {
                if (Regex.IsMatch(s, @"\bwake me|\bmorning\b", I)) { if (h == 12) h = 0; }
                else if (Regex.IsMatch(s, @"\b(tonight|evening|afternoon)\b", I)) { if (h < 12) h += 12; }
                else
                {
                    // "alarm at 7": whichever 7 comes first
                    var a = NextAlarm(new TimeSpan(h % 12, min, 0), "Once", now);
                    var b = NextAlarm(new TimeSpan(h % 12 + 12, min, 0), "Once", now);
                    h = (b < a ? h % 12 + 12 : h % 12);
                }
            }
            tod = new TimeSpan(h, min, 0);
        }
        string days = Regex.IsMatch(s, @"\b(on )?week ?days\b|monday (to|through|-) friday|\bwork ?days\b", I) ? "Weekdays"
            : Regex.IsMatch(s, @"\b(on )?weekends?\b", I) ? "Weekends"
            : Regex.IsMatch(s, @"\b(every ?day|daily|each day|every morning|every night)\b", I) ? "Every day" : "Once";
        var label = LabelFrom(s.Remove(m.Index, m.Length).Insert(m.Index, " "), s[(m.Index + m.Length)..]);
        return Alarm(tod, days, label, now);
    }

    /// <summary>The length in a phrase like "1 hour 30 minutes", "half an hour", "90 sec". Null if there isn't one.</summary>
    public static TimeSpan? ParseLength(string text)
    {
        double total = 0; bool any = false;
        foreach (Match m in Regex.Matches(text, @"(\d+(?:\.\d+)?|an?|one|half an?)\s*(hours?|hrs?|h|minutes?|mins?|m|seconds?|secs?|s)\b", I))
        {
            var n = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "a" or "an" or "one" => 1,
                var x when x.StartsWith("half") => 0.5,
                var x => double.Parse(x, CultureInfo.InvariantCulture),
            };
            var unit = m.Groups[2].Value.ToLowerInvariant();
            total += unit.StartsWith('h') ? n * 3600 : unit.StartsWith('m') ? n * 60 : n;
            any = true;
        }
        return any && total >= 1 ? TimeSpan.FromSeconds(Math.Round(total)) : null;
    }

    /// <summary>What it's for: "…for the pizza", "…to check the oven", "…called tea", "…: call mom".</summary>
    static string LabelFrom(string rest, string tail)
    {
        const string When = @"\b(on )?(week ?days|weekends?|every ?day|daily|each day|every morning|every night|monday (to|through|-) friday|work ?days|tonight|tomorrow( morning| night)?|today|in the (morning|evening|afternoon)|this (morning|evening|afternoon))\b";
        rest = Regex.Replace(rest, When, " ", I);
        var m = Regex.Match(rest, @"(?:\b(?:to|for|called|named|labell?ed|saying|that says|about)\b|:)\s+(.+?)\s*$", I);
        string label;
        if (m.Success) label = m.Groups[1].Value;
        else
        {
            // "alarm 6:45pm call mom", "timer 10 min pizza": whatever comes after the time
            label = Regex.Replace(tail, When, " ", I);
            label = Regex.Replace(label, @"^\s*(?:-|,|please)?\s*", "", I);
        }
        label = label.Trim().TrimEnd('.', '!', ',', '?').Trim();
        if (label.Length == 0) return "";
        label = Regex.Replace(label, @"\s+(?:in|at|on|for)\s*$", "", I).Trim();
        label = Regex.Replace(label, @"^(?:(?:the|my|a|an)\s+)", "", I).Trim();
        label = Regex.Replace(label, @"\s{2,}", " ");
        if (Regex.IsMatch(label, @"^(?:a|an|the|me|it|timer|alarm|set|up)?$", I)) return "";
        if (label.Length > 60) label = label[..60];
        return char.ToUpperInvariant(label[0]) + label[1..];
    }
}

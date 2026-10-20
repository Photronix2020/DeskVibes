using System.Drawing;
using System.Globalization;
using System.Text.Json.Serialization;

namespace DeskVibes;

/// <summary>One "vibe": either a rule (when X happens, do Y) or a widget (an info card on the desktop).</summary>
public sealed class Vibe
{
    public string Id { get; set; } = NewId();
    public string Name { get; set; } = "New vibe";
    public bool On { get; set; } = true;
    /// <summary>"rule" or "widget".</summary>
    public string Kind { get; set; } = "rule";
    public Step When { get; set; } = new("time.every");
    /// <summary>Condition ids from <see cref="Catalog.Conditions"/> that must all hold.</summary>
    public List<string> Only { get; set; } = new();
    public int CooldownSec { get; set; }
    public List<Step> Do { get; set; } = new();
    public Step? Widget { get; set; }
    public Place Place { get; set; } = new();

    [JsonIgnore] public bool IsWidget => Kind == "widget";

    public static string NewId() => Guid.NewGuid().ToString("N")[..10];

    public Vibe Clone()
    {
        var v = Json.Clone(this);
        return v;
    }
}

/// <summary>A trigger, action or widget: a catalog type plus its answers.</summary>
public sealed class Step
{
    public string Type { get; set; } = "";
    public Dictionary<string, string> P { get; set; } = new();

    public Step() { }
    public Step(string type, params (string k, string v)[] p)
    {
        Type = type;
        foreach (var (k, v) in p) P[k] = v;
    }

    public string Get(string key, string def = "")
    {
        if (P.TryGetValue(key, out var v) && v != null) return v;
        var f = Catalog.Find(Type)?.Fields.FirstOrDefault(x => x.Key == key);
        return f?.Default ?? def;
    }

    public double Num(string key, double def = 0)
    {
        var s = Get(key, "").Trim().TrimStart('$').Replace(",", "").TrimEnd('%');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;
    }

    public bool Bool(string key) => Get(key, "false").Equals("true", StringComparison.OrdinalIgnoreCase);

    public Color Color(string key, Color def) => Colors.Parse(Get(key, ""), def);
}

/// <summary>Where and how a widget sits on screen.</summary>
public sealed class Place
{
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int W { get; set; } = 260;
    public int H { get; set; } = 130;
    /// <summary>"top" (always on top), "normal", or "desktop" (stays behind other windows).</summary>
    public string Layer { get; set; } = "top";
    public bool ClickThrough { get; set; }
    public bool Locked { get; set; }
    public int Opacity { get; set; } = 94;
    public string Note { get; set; } = "";
}

public sealed class Settings
{
    public string Location { get; set; } = "";
    public string Units { get; set; } = "F";
    public bool Clock24 { get; set; }
    public string ImapServer { get; set; } = "";
    public int ImapPort { get; set; } = 993;
    public string ImapUser { get; set; } = "";
    /// <summary>Stored encrypted with Windows DPAPI (current user only).</summary>
    public string ImapPasswordProtected { get; set; } = "";
    public int MailEveryMin { get; set; } = 1;
    public bool UseOutlook { get; set; }
    public string BlizzardId { get; set; } = "";
    public string BlizzardSecretProtected { get; set; } = "";
    /// <summary>The user's own Anthropic API key for AI "Describe it", DPAPI-encrypted. Empty = phrase matcher only.</summary>
    public string AnthropicKeyProtected { get; set; } = "";
    public bool QuietHours { get; set; }
    public string QuietFrom { get; set; } = "22:00";
    public string QuietTo { get; set; } = "07:00";
    public string WorkFrom { get; set; } = "09:00";
    public string WorkTo { get; set; } = "17:00";
    public bool Paused { get; set; }
    public bool StartWithWindows { get; set; }
    public bool SeenWelcome { get; set; }
    /// <summary>The button deck was added (new installs get it in the starter set, older ones once on update).</summary>
    public bool GotDeck { get; set; }
}

/// <summary>Values an event hands to its actions, used to fill {placeholders} in texts.</summary>
public sealed class Ctx : Dictionary<string, string>
{
    public Ctx() : base(StringComparer.OrdinalIgnoreCase) { }
    public Ctx(IDictionary<string, string> d) : base(d, StringComparer.OrdinalIgnoreCase) { }

    public Ctx With(string k, string v) { this[k] = v; return this; }

    public static Ctx Of(params (string k, string v)[] p)
    {
        var c = new Ctx();
        foreach (var (k, v) in p) c[k] = v;
        return c;
    }

    /// <summary>Replaces {key} with values from this context, plus {time}, {date} and {day}.</summary>
    public string Fill(string text, DateTime now)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('{')) return text ?? "";
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf('{', i);
            if (open < 0) { sb.Append(text, i, text.Length - i); break; }
            int close = text.IndexOf('}', open + 1);
            if (close < 0) { sb.Append(text, i, text.Length - i); break; }
            sb.Append(text, i, open - i);
            var key = text.Substring(open + 1, close - open - 1).Trim();
            string? val = key.ToLowerInvariant() switch
            {
                "time" => now.ToString("t", CultureInfo.CurrentCulture),
                "date" => now.ToString("D", CultureInfo.CurrentCulture),
                "day" => now.DayOfWeek.ToString(),
                _ => TryGetValue(key, out var v) ? v : null,
            };
            sb.Append(val ?? text.Substring(open, close - open + 1));
            i = close + 1;
        }
        return sb.ToString();
    }
}

public static class Colors
{
    public static readonly (string name, string hex)[] Named =
    {
        ("Red", "#FF3B30"), ("Orange", "#FF9500"), ("Yellow", "#FFD60A"), ("Green", "#30D158"),
        ("Teal", "#40C8E0"), ("Blue", "#0A84FF"), ("Purple", "#BF5AF2"), ("Pink", "#FF375F"),
        ("White", "#F2F2F7"), ("Gray", "#8E8E93"),
    };

    public static Color Parse(string s, Color def)
    {
        if (string.IsNullOrWhiteSpace(s)) return def;
        s = s.Trim();
        foreach (var (n, h) in Named)
            if (n.Equals(s, StringComparison.OrdinalIgnoreCase)) { s = h; break; }
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 6 && int.TryParse(s, NumberStyles.HexNumber, null, out var rgb))
            return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
        try { var c = Color.FromName(s); if (c.IsKnownColor) return c; } catch { }
        return def;
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static string NameOf(string hex)
    {
        foreach (var (n, h) in Named)
            if (h.Equals(hex, StringComparison.OrdinalIgnoreCase) || n.Equals(hex, StringComparison.OrdinalIgnoreCase)) return n.ToLowerInvariant();
        return hex;
    }
}

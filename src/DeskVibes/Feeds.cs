using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;

namespace DeskVibes;

public sealed record HourCast(DateTime Time, int RainChance, int Code);
public sealed record WeatherInfo(string Place, double Temp, string Units, int Code, double Wind, double High, double Low, List<HourCast> Hours)
{
    public string Text => Feeds.WeatherText(Code);
    public string Icon => Feeds.WeatherIcon(Code);
}
public sealed record QuoteInfo(string Symbol, string Name, double Price, double ChangePct, string Currency);
public sealed record SiteInfo(string Url, bool Up, string Status);
public sealed record NewsItem(string Title, string Link, string Id);
public sealed record RealmInfo(string Name, bool Up, string Status, string Population);

/// <summary>Things from the internet. Every call returns the latest cached value at once and refreshes in the background.</summary>
public interface IFeeds
{
    WeatherInfo? Weather(string place);
    QuoteInfo? Quote(string symbol);
    SiteInfo? Site(string url);
    IReadOnlyList<NewsItem>? News(string url);
    RealmInfo? Realm(string region, string game, string realm);
    /// <summary>The last error for a feed, to show in widgets ("weather:Boston").</summary>
    string? Problem(string key);
}

public sealed class Feeds : IFeeds
{
    static readonly HttpClient http = MakeClient();
    readonly Func<Settings> settings;
    readonly ConcurrentDictionary<string, Entry> cache = new();

    sealed class Entry
    {
        public object? Value;
        public DateTime Fetched = DateTime.MinValue;
        public int Busy;
        public string? Error;
    }

    public Feeds(Func<Settings> settings) { this.settings = settings; }

    static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) DeskVibes/0.1");
        return h;
    }

    T? Get<T>(string key, TimeSpan maxAge, Func<Task<T>> fetch) where T : class
    {
        var e = cache.GetOrAdd(key, _ => new Entry());
        var age = DateTime.UtcNow - e.Fetched;
        // after an error, wait a minute before trying again
        bool stale = e.Error != null ? age > TimeSpan.FromMinutes(1) : age > maxAge;
        if (stale && Interlocked.CompareExchange(ref e.Busy, 1, 0) == 0)
        {
            Task.Run(async () =>
            {
                try { e.Value = await fetch(); e.Error = null; }
                catch (Exception ex) { e.Error = Friendly(ex); }
                finally { e.Fetched = DateTime.UtcNow; e.Busy = 0; }
            });
        }
        return e.Value as T;
    }

    static string Friendly(Exception ex) => ex switch
    {
        TaskCanceledException => "timed out",
        HttpRequestException h when h.StatusCode != null => $"error {(int)h.StatusCode.Value}",
        HttpRequestException => "can't connect",
        _ => ex.Message,
    };

    public string? Problem(string key) => cache.TryGetValue(key, out var e) ? e.Error : null;

    // ---- Weather (Open-Meteo, no key) ----

    public WeatherInfo? Weather(string place)
    {
        var s = settings();
        if (string.IsNullOrWhiteSpace(place)) place = s.Location;
        if (string.IsNullOrWhiteSpace(place)) return null;
        var units = s.Units == "C" ? "C" : "F";
        return Get("weather:" + place.Trim().ToLowerInvariant() + ":" + units, TimeSpan.FromMinutes(10), () => FetchWeather(place.Trim(), units));
    }

    async Task<WeatherInfo> FetchWeather(string place, string units)
    {
        var (lat, lon, name) = await Geocode(place);
        var url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat.ToString(CultureInfo.InvariantCulture) +
                  "&longitude=" + lon.ToString(CultureInfo.InvariantCulture) +
                  "&current=temperature_2m,weather_code,wind_speed_10m&hourly=precipitation_probability,weather_code" +
                  "&daily=temperature_2m_max,temperature_2m_min&forecast_days=2&timezone=auto" +
                  (units == "F" ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "");
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
        var r = doc.RootElement;
        var cur = r.GetProperty("current");
        var hourly = r.GetProperty("hourly");
        var times = hourly.GetProperty("time").EnumerateArray().Select(t => DateTime.Parse(t.GetString()!, CultureInfo.InvariantCulture)).ToList();
        var probs = hourly.GetProperty("precipitation_probability").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetInt32() : 0).ToList();
        var codes = hourly.GetProperty("weather_code").EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetInt32() : 0).ToList();
        // times are local to the place; keep the hours from now on
        int offset = r.TryGetProperty("utc_offset_seconds", out var off) ? off.GetInt32() : 0;
        var placeNow = DateTime.UtcNow.AddSeconds(offset);
        var hours = new List<HourCast>();
        for (int i = 0; i < times.Count; i++)
            if (times[i] > placeNow.AddHours(-1)) hours.Add(new HourCast(times[i], probs[i], codes[i]));
        var daily = r.GetProperty("daily");
        return new WeatherInfo(name, cur.GetProperty("temperature_2m").GetDouble(), units, cur.GetProperty("weather_code").GetInt32(),
            cur.GetProperty("wind_speed_10m").GetDouble(),
            daily.GetProperty("temperature_2m_max")[0].GetDouble(), daily.GetProperty("temperature_2m_min")[0].GetDouble(), hours);
    }

    readonly ConcurrentDictionary<string, (double, double, string)> places = new();

    async Task<(double lat, double lon, string name)> Geocode(string place)
    {
        if (places.TryGetValue(place.ToLowerInvariant(), out var hit)) return hit;
        // "Boston, MA" -> search "Boston", prefer a result whose region/country matches the rest
        var parts = place.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var url = "https://geocoding-api.open-meteo.com/v1/search?count=10&language=en&name=" + Uri.EscapeDataString(parts[0]);
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
        if (!doc.RootElement.TryGetProperty("results", out var res) || res.GetArrayLength() == 0)
            throw new Exception($"can't find \"{place}\"");
        var pick = res[0];
        if (parts.Length > 1)
        {
            var want = parts[1].ToLowerInvariant();
            foreach (var x in res.EnumerateArray())
            {
                string Prop(string n) => x.TryGetProperty(n, out var p) ? (p.GetString() ?? "").ToLowerInvariant() : "";
                var a1 = Prop("admin1"); var cc = Prop("country_code"); var co = Prop("country");
                if (a1 == want || cc == want || co == want || (want.Length == 2 && StateAbbr(a1) == want)) { pick = x; break; }
            }
        }
        var v = (pick.GetProperty("latitude").GetDouble(), pick.GetProperty("longitude").GetDouble(), pick.GetProperty("name").GetString() ?? place);
        places[place.ToLowerInvariant()] = v;
        return v;
    }

    static string StateAbbr(string state) => state switch
    {
        "alabama" => "al", "alaska" => "ak", "arizona" => "az", "arkansas" => "ar", "california" => "ca", "colorado" => "co",
        "connecticut" => "ct", "delaware" => "de", "florida" => "fl", "georgia" => "ga", "hawaii" => "hi", "idaho" => "id",
        "illinois" => "il", "indiana" => "in", "iowa" => "ia", "kansas" => "ks", "kentucky" => "ky", "louisiana" => "la",
        "maine" => "me", "maryland" => "md", "massachusetts" => "ma", "michigan" => "mi", "minnesota" => "mn", "mississippi" => "ms",
        "missouri" => "mo", "montana" => "mt", "nebraska" => "ne", "nevada" => "nv", "new hampshire" => "nh", "new jersey" => "nj",
        "new mexico" => "nm", "new york" => "ny", "north carolina" => "nc", "north dakota" => "nd", "ohio" => "oh", "oklahoma" => "ok",
        "oregon" => "or", "pennsylvania" => "pa", "rhode island" => "ri", "south carolina" => "sc", "south dakota" => "sd",
        "tennessee" => "tn", "texas" => "tx", "utah" => "ut", "vermont" => "vt", "virginia" => "va", "washington" => "wa",
        "west virginia" => "wv", "wisconsin" => "wi", "wyoming" => "wy", _ => "",
    };

    public static string WeatherText(int code) => code switch
    {
        0 => "Clear", 1 => "Mostly clear", 2 => "Partly cloudy", 3 => "Cloudy", 45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle", 56 or 57 => "Freezing drizzle", 61 => "Light rain", 63 => "Rain", 65 => "Heavy rain",
        66 or 67 => "Freezing rain", 71 => "Light snow", 73 => "Snow", 75 => "Heavy snow", 77 => "Snow grains",
        80 or 81 => "Showers", 82 => "Heavy showers", 85 or 86 => "Snow showers", 95 => "Thunderstorm", 96 or 99 => "Thunderstorm with hail",
        _ => "—",
    };

    public static string WeatherIcon(int code) => code switch
    {
        0 or 1 => "☀", 2 => "⛅", 3 or 45 or 48 => "☁", >= 71 and <= 77 or 85 or 86 => "❄", >= 95 => "⚡", >= 51 => "☂", _ => "·",
    };

    public static bool IsSnow(int code) => code is >= 71 and <= 77 or 85 or 86;
    public static bool IsWet(int code) => code >= 51;

    // ---- Stocks & crypto (Yahoo Finance chart endpoint) ----

    public QuoteInfo? Quote(string symbol)
    {
        symbol = symbol.Trim().ToUpperInvariant();
        if (symbol.Length == 0) return null;
        return Get("quote:" + symbol, TimeSpan.FromMinutes(2), () => FetchQuote(symbol));
    }

    async Task<QuoteInfo> FetchQuote(string symbol)
    {
        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=1d&interval=5m";
        using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
        var res = doc.RootElement.GetProperty("chart").GetProperty("result");
        if (res.ValueKind != JsonValueKind.Array || res.GetArrayLength() == 0) throw new Exception($"unknown symbol {symbol}");
        var meta = res[0].GetProperty("meta");
        double price = meta.GetProperty("regularMarketPrice").GetDouble();
        double prev = meta.TryGetProperty("chartPreviousClose", out var pc) ? pc.GetDouble()
                    : meta.TryGetProperty("previousClose", out var p2) ? p2.GetDouble() : price;
        string name = meta.TryGetProperty("shortName", out var sn) ? sn.GetString() ?? symbol : symbol;
        string cur = meta.TryGetProperty("currency", out var c) ? c.GetString() ?? "" : "";
        return new QuoteInfo(symbol, name, price, prev == 0 ? 0 : (price - prev) / prev * 100, cur);
    }

    // ---- Websites ----

    public SiteInfo? Site(string url)
    {
        url = NormalizeUrl(url);
        if (url.Length == 0) return null;
        return Get("site:" + url, TimeSpan.FromSeconds(60), () => FetchSite(url));
    }

    public static string NormalizeUrl(string url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0 || url == "https://") return "";
        return url.Contains("://") ? url : "https://" + url;
    }

    static async Task<SiteInfo> FetchSite(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            int code = (int)resp.StatusCode;
            return new SiteInfo(url, code < 500, code < 400 ? "up" : $"answering {code}");
        }
        catch (TaskCanceledException) { return new SiteInfo(url, false, "not answering"); }
        catch (HttpRequestException ex) { return new SiteInfo(url, false, ex.StatusCode != null ? $"error {(int)ex.StatusCode}" : "can't connect"); }
    }

    // ---- News feeds ----

    public IReadOnlyList<NewsItem>? News(string url)
    {
        url = NormalizeUrl(url);
        if (url.Length == 0) return null;
        return Get<IReadOnlyList<NewsItem>>("news:" + url, TimeSpan.FromMinutes(10), async () => ParseFeed(await http.GetStringAsync(url)));
    }

    public static IReadOnlyList<NewsItem> ParseFeed(string xml)
    {
        var doc = XDocument.Parse(xml);
        var list = new List<NewsItem>();
        foreach (var el in doc.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            string Child(string n) => el.Elements().FirstOrDefault(e => e.Name.LocalName == n)?.Value.Trim() ?? "";
            var title = System.Net.WebUtility.HtmlDecode(Child("title"));
            var link = Child("link");
            if (link.Length == 0)
                link = el.Elements().FirstOrDefault(e => e.Name.LocalName == "link")?.Attribute("href")?.Value ?? "";
            var id = Child("guid"); if (id.Length == 0) id = Child("id"); if (id.Length == 0) id = link + title;
            if (title.Length > 0) list.Add(new NewsItem(title, link, id));
        }
        return list;
    }

    // ---- WoW realms (Blizzard Game Data API, needs a free client id + secret) ----

    string? token; DateTime tokenUntil;
    readonly SemaphoreSlim tokenLock = new(1, 1);

    public RealmInfo? Realm(string region, string game, string realm)
    {
        region = (region ?? "us").Trim().ToLowerInvariant();
        if (realm.Trim().Length == 0) return null;
        var key = $"realm:{region}:{NamespaceFor(game, region)}:{Slug(realm)}";
        var s = settings();
        if (s.BlizzardId.Length == 0 || s.BlizzardSecretProtected.Length == 0)
        {
            cache.GetOrAdd(key, _ => new Entry()).Error = "add a Blizzard API key in Settings";
            return null;
        }
        return Get(key, TimeSpan.FromMinutes(2), () => FetchRealm(region, game, realm));
    }

    public static string NamespaceFor(string game, string region) => (game ?? "").ToLowerInvariant() switch
    {
        "classic" => $"dynamic-classic-{region}",
        "classic era" or "era" => $"dynamic-classic1x-{region}",
        "anniversary" => $"dynamic-classicann-{region}",
        _ => $"dynamic-{region}",
    };

    public static string Slug(string realm) =>
        new string(realm.Trim().ToLowerInvariant().Replace(' ', '-').Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());

    async Task<RealmInfo> FetchRealm(string region, string game, string realm)
    {
        var tok = await Token();
        var url = $"https://{region}.api.blizzard.com/data/wow/search/connected-realm?namespace={NamespaceFor(game, region)}&realms.slug={Slug(realm)}&locale=en_US";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tok);
        using var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var results = doc.RootElement.GetProperty("results");
        if (results.GetArrayLength() == 0) throw new Exception($"no realm \"{realm}\"");
        var data = results[0].GetProperty("data");
        var status = data.GetProperty("status").GetProperty("type").GetString() ?? "";
        var pop = data.TryGetProperty("population", out var p) && p.TryGetProperty("type", out var pt) ? pt.GetString() ?? "" : "";
        return new RealmInfo(realm, status == "UP", status == "UP" ? "up" : "down", pop.Replace('_', ' ').ToLowerInvariant());
    }

    async Task<string> Token()
    {
        await tokenLock.WaitAsync();
        try
        {
            if (token != null && DateTime.UtcNow < tokenUntil) return token;
            var s = settings();
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://oauth.battle.net/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" }),
            };
            var basic = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s.BlizzardId + ":" + Secret.Unprotect(s.BlizzardSecretProtected)));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) throw new Exception("Blizzard key was refused");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            token = doc.RootElement.GetProperty("access_token").GetString();
            tokenUntil = DateTime.UtcNow.AddSeconds(doc.RootElement.GetProperty("expires_in").GetInt32() - 300);
            return token!;
        }
        finally { tokenLock.Release(); }
    }

    public void ForgetToken() => token = null;
}

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DeskVibes;

/// <summary>Optional "Describe it" with real AI: sends the sentence and a compact catalog to the Claude Messages API using
/// the user's own Anthropic key, and checks the answer against <see cref="Catalog"/> before using it. Anything that goes
/// wrong (no key, no internet, a refused key, an answer that doesn't fit) falls back to the <see cref="Describe"/> phrase matcher.
/// The key is never logged or written anywhere except DPAPI-encrypted settings.</summary>
public static class AiDescribe
{
    public const string Model = "claude-opus-5-5";
    const string Url = "https://api.anthropic.com/v1/messages";
    const string FallbackBeta = "server-side-fallback-2026-07-01";

    static readonly HttpClient http = MakeClient();

    static HttpClient MakeClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("DeskVibes/0.1");
        return h;
    }

    /// <summary>Uses Claude when a key is set, the phrase matcher otherwise (or when Claude can't help).</summary>
    public static async Task<Describe.Result> ParseAsync(string sentence, string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(sentence)) return Describe.Parse(sentence);
        string why;
        try
        {
            var (status, body) = await Send(BuildRequest(sentence, true), apiKey.Trim(), true, ct);
            // if the account or endpoint doesn't take the fallback option, ask once more without it
            if (status == HttpStatusCode.BadRequest && body.Contains("fallback", StringComparison.OrdinalIgnoreCase))
                (status, body) = await Send(BuildRequest(sentence, false), apiKey.Trim(), false, ct);
            if (status == HttpStatusCode.OK)
            {
                var r = FromResponse(body);
                if (r != null) return r;
                why = "Claude's answer didn't fit what DeskVibes can do";
            }
            else why = Problem(status);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { why = "Claude took too long to answer"; }
        catch (HttpRequestException) { why = "Claude couldn't be reached (is the internet on?)"; }
        catch (Exception) { why = "something went wrong asking Claude"; }
        return WithNote(Describe.Parse(sentence), why + ", so I used the built-in matcher instead.");
    }

    /// <summary>Checks a key without spending anything: asks the API about the model.</summary>
    public static async Task<string> TestKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return "Paste a key first.";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models/" + Model);
            AddHeaders(req, apiKey.Trim(), false);
            using var res = await http.SendAsync(req);
            return res.StatusCode == HttpStatusCode.OK ? "✓ The key works." : Problem(res.StatusCode) + ".";
        }
        catch (HttpRequestException) { return "Couldn't reach Anthropic. Is the internet on?"; }
        catch (Exception) { return "Couldn't check the key right now."; }
    }

    static async Task<(HttpStatusCode, string)> Send(string json, string key, bool fallbacks, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        AddHeaders(req, key, fallbacks);
        using var res = await http.SendAsync(req, ct);
        return (res.StatusCode, await res.Content.ReadAsStringAsync(ct));
    }

    static void AddHeaders(HttpRequestMessage req, string key, bool fallbacks)
    {
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        if (fallbacks) req.Headers.Add("anthropic-beta", FallbackBeta);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    static string Problem(HttpStatusCode status) => (int)status switch
    {
        401 => "Anthropic refused the API key (check it in Settings)",
        403 => "that API key isn't allowed to use Claude",
        402 or 400 => "Anthropic turned the request down (is there credit on the account?)",
        429 => "Claude is busy or the account hit its limit",
        >= 500 => "Claude is having trouble right now",
        _ => $"Claude answered with an error ({(int)status})",
    };

    static Describe.Result WithNote(Describe.Result r, string note)
    {
        var notes = new List<string> { note };
        notes.AddRange(r.Understood);
        return r with { Understood = notes };
    }

    // ---------- the request ----------

    /// <summary>Every trigger, action, widget and condition with its fields, as compact JSON for the prompt.</summary>
    public static string CatalogJson()
    {
        JsonArray Defs(IEnumerable<Def> defs)
        {
            var a = new JsonArray();
            foreach (var d in defs)
            {
                var o = new JsonObject { ["id"] = d.Id, ["label"] = d.Label };
                if (d.Help.Length > 0) o["help"] = d.Help;
                if (d.Fields.Length > 0)
                {
                    var fs = new JsonArray();
                    foreach (var f in d.Fields)
                    {
                        var fo = new JsonObject { ["key"] = f.Key, ["label"] = f.Label, ["kind"] = f.Kind.ToString().ToLowerInvariant() };
                        if (f.Default.Length > 0) fo["default"] = f.Default;
                        if (f.Choices != null) fo["choices"] = new JsonArray(f.Choices.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray());
                        if (f.Hint.Length > 0) fo["hint"] = f.Hint;
                        fs.Add(fo);
                    }
                    o["fields"] = fs;
                }
                if (d.Gives.Length > 0) o["gives"] = new JsonArray(d.Gives.Select(g => (JsonNode)JsonValue.Create("{" + g + "}")!).ToArray());
                a.Add(o);
            }
            return a;
        }
        var root = new JsonObject
        {
            ["triggers"] = Defs(Catalog.Triggers),
            ["actions"] = Defs(Catalog.Actions),
            ["widgets"] = Defs(Catalog.Widgets),
            ["conditions"] = new JsonArray(Catalog.Conditions.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["label"] = c.Label }).ToArray()),
            ["colors"] = new JsonObject(Colors.Named.Select(c => KeyValuePair.Create(c.name, (JsonNode?)JsonValue.Create(c.hex)))),
        };
        return root.ToJsonString();
    }

    /// <summary>The JSON schema Claude's answer must follow (structured outputs).</summary>
    public static JsonObject OutputSchema()
    {
        static JsonArray Ids(IEnumerable<Def> defs) => new(defs.Select(d => (JsonNode)JsonValue.Create(d.Id)!).ToArray());
        static JsonObject Str() => new() { ["type"] = "string" };
        static JsonObject Step(IEnumerable<Def> defs) => new()
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["type"] = new JsonObject { ["type"] = "string", ["enum"] = Ids(defs) },
                ["answers"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["key"] = Str(), ["value"] = Str() },
                        ["required"] = new JsonArray("key", "value"),
                        ["additionalProperties"] = false,
                    },
                },
            },
            ["required"] = new JsonArray("type", "answers"),
            ["additionalProperties"] = false,
        };
        static JsonObject OrNull(JsonObject o) => new() { ["anyOf"] = new JsonArray(o, new JsonObject { ["type"] = "null" }) };

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["kind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("rule", "widget") },
                ["name"] = Str(),
                ["trigger"] = OrNull(Step(Catalog.Triggers)),
                ["actions"] = new JsonObject { ["type"] = "array", ["items"] = Step(Catalog.Actions) },
                ["only"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string", ["enum"] = Ids(Catalog.Conditions) } },
                ["cooldown_minutes"] = new JsonObject { ["type"] = "integer" },
                ["widget"] = OrNull(Step(Catalog.Widgets)),
                ["notes"] = new JsonObject { ["type"] = "array", ["items"] = Str() },
            },
            ["required"] = new JsonArray("kind", "name", "trigger", "actions", "only", "cooldown_minutes", "widget", "notes"),
            ["additionalProperties"] = false,
        };
    }

    public static string SystemPrompt() =>
        "You set up \"vibes\" in DeskVibes, a Windows tray app for everyday, non-technical people. A person describes what they want " +
        "in one sentence; you turn it into one vibe. A vibe is either a rule (when the trigger happens, run the actions in order, only if " +
        "every condition in \"only\" holds) or a widget (an info card that sits on the desktop).\n" +
        "Use only the ids, field keys and choices in the catalog below. Every answer value is a string. For choice fields use one of the " +
        "choices exactly. Colors are hex values from \"colors\" (or another hex). Times are 24-hour like 17:30. Leave out fields the person " +
        "didn't mention so the defaults apply. Text fields may use placeholders: the trigger's \"gives\" list plus {vibe}, {time}, {date} and {day}.\n" +
        "For a rule, set widget to null; for a widget, set trigger to null and actions and only to empty lists. cooldown_minutes is 0 unless " +
        "they ask for something like \"at most once an hour\". name is short (under 40 characters), like \"Email from boss → red edges\".\n" +
        "notes: zero to three short, friendly sentences in plain words about anything you had to guess or that DeskVibes can't do exactly " +
        "(then pick the closest thing it can do). No jargon, no ids.\n\nCatalog:\n" + CatalogJson();

    /// <summary>The Messages API request body for one sentence.</summary>
    public static string BuildRequest(string sentence, bool fallbacks = true)
    {
        var body = new JsonObject
        {
            ["model"] = Model,
            ["max_tokens"] = 16000,
            // the catalog is the same every time, so cache it
            ["system"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = SystemPrompt(), ["cache_control"] = new JsonObject { ["type"] = "ephemeral" } }),
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = sentence.Trim() }),
            ["output_config"] = new JsonObject
            {
                ["effort"] = "low",
                ["format"] = new JsonObject { ["type"] = "json_schema", ["schema"] = OutputSchema() },
            },
        };
        if (fallbacks) body["fallbacks"] = "default";
        return body.ToJsonString();
    }

    // ---------- the answer ----------

    /// <summary>Reads a Messages API response. Null if Claude declined, was cut off, or the answer doesn't fit the catalog.</summary>
    public static Describe.Result? FromResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            var stop = root.TryGetProperty("stop_reason", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            if (stop is "refusal" or "max_tokens") return null;
            if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return null;
            foreach (var block in content.EnumerateArray())
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text" && block.TryGetProperty("text", out var text))
                    return FromJson(text.GetString() ?? "");
            return null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Turns Claude's JSON into a vibe. Unknown trigger, action or widget types are refused (like share codes);
    /// unknown field keys are dropped and odd values fall back to the defaults.</summary>
    public static Describe.Result? FromJson(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); } catch (JsonException) { return null; }
        if (root is not JsonObject o) return null;
        var notes = new List<string> { "Understood with Claude AI." };
        var v = new Vibe { Kind = Str(o["kind"]) == "widget" ? "widget" : "rule" };

        if (v.IsWidget)
        {
            var w = ReadStep(o["widget"], Catalog.Widgets);
            if (w == null) return null;
            v.Widget = w;
            notes.Add("A widget: " + Catalog.Describe(w));
        }
        else
        {
            var when = ReadStep(o["trigger"], Catalog.Triggers);
            if (when == null) return null;
            v.When = when;
            notes.Add("When: " + Catalog.Describe(when));
            if (o["actions"] is JsonArray acts)
                foreach (var a in acts)
                {
                    var step = ReadStep(a, Catalog.Actions);
                    if (step == null) return null;
                    v.Do.Add(step);
                }
            if (v.Do.Count == 0)
            {
                var (title, text) = Describe.CardText(when.Type);
                v.Do.Add(new Step("show.card", ("title", title), ("text", text)));
                notes.Add("You didn't say what to do, so it pops up a card.");
            }
            foreach (var a in v.Do) notes.Add("Then: " + Catalog.Describe(a));
            if (o["only"] is JsonArray only)
                foreach (var c in only)
                {
                    var id = Str(c);
                    if (Catalog.Conditions.Any(d => d.Id == id) && !v.Only.Contains(id)) v.Only.Add(id);
                }
            foreach (var c in v.Only) notes.Add("Only " + Catalog.Find(c)!.Label.ToLowerInvariant());
            if (o["cooldown_minutes"] is JsonValue cd && cd.TryGetValue<int>(out var mins) && mins > 0)
            {
                v.CooldownSec = Math.Min(mins, 7 * 24 * 60) * 60;
                notes.Add($"At most once every {(mins % 60 == 0 ? $"{mins / 60} hour{(mins == 60 ? "" : "s")}" : $"{mins} minutes")}");
            }
        }

        var name = Str(o["name"]).Trim();
        v.Name = name.Length == 0 ? (v.IsWidget ? Catalog.Find(v.Widget!.Type)!.Label : Catalog.Find(v.When.Type)!.Label)
                                  : name.Length > 60 ? name[..60] : name;
        if (o["notes"] is JsonArray extra)
            foreach (var n in extra.Take(3))
            {
                var s = Str(n).Trim();
                if (s.Length > 0) notes.Add(s.Length > 200 ? s[..200] + "…" : s);
            }
        return new Describe.Result(v, notes, false);
    }

    static string Str(JsonNode? n) => n is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : "";

    static Step? ReadStep(JsonNode? node, Def[] allowed)
    {
        if (node is not JsonObject so) return null;
        var def = allowed.FirstOrDefault(d => d.Id == Str(so["type"]));
        if (def == null) return null;
        var step = new Step(def.Id);
        if (so["answers"] is JsonArray answers)
            foreach (var a in answers)
            {
                if (a is not JsonObject ao) continue;
                var f = def.Fields.FirstOrDefault(x => x.Key == Str(ao["key"]));
                if (f == null) continue;
                var val = Clean(f, ao["value"] is JsonValue jv ? jv.ToString() : "");
                if (val != null) step.P[f.Key] = val;
            }
        return step;
    }

    /// <summary>A value that fits the field, or null to keep the default.</summary>
    static string? Clean(Field f, string value)
    {
        value = value.Trim();
        if (value.Length == 0) return null;
        switch (f.Kind)
        {
            case FieldKind.Choice:
                return f.Choices?.FirstOrDefault(c => c.Equals(value, StringComparison.OrdinalIgnoreCase));
            case FieldKind.Number:
                return double.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 0
                    ? d.ToString(CultureInfo.InvariantCulture) : null;
            case FieldKind.Check:
                return value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : value.Equals("false", StringComparison.OrdinalIgnoreCase) ? "false" : null;
            case FieldKind.Color:
                var none = System.Drawing.Color.FromArgb(1, 2, 3, 4);
                var c = Colors.Parse(value, none);
                return c == none ? null : Colors.Hex(c);
            case FieldKind.Time:
                var m = Regex.Match(value, @"^(\d{1,2}):(\d\d)$");
                return m.Success && int.Parse(m.Groups[1].Value) < 24 && int.Parse(m.Groups[2].Value) < 60
                    ? $"{int.Parse(m.Groups[1].Value):00}:{m.Groups[2].Value}" : null;
            case FieldKind.Long:
                return value.Length > 2000 ? value[..2000] : value;
            default:
                return value.Length > 500 ? value[..500] : value;
        }
    }
}

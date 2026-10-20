using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace DeskVibes;

public sealed record MailItem(string Id, string From, string Subject, DateTime Seen);
public sealed record Meeting(string Subject, DateTime Start, DateTime End, string Where);

public interface IMail
{
    int Unread { get; }
    IReadOnlyList<MailItem> Recent { get; }
    IReadOnlyList<Meeting> Meetings { get; }
    string? Problem { get; }
    bool Configured { get; }
}

/// <summary>Watches the inbox (IMAP, and/or classic Outlook) and the Outlook calendar. New messages go to <see cref="NewMail"/>.</summary>
public sealed class MailHub : IMail, IDisposable
{
    readonly Func<Settings> settings;
    readonly System.Threading.Timer timer;
    readonly HashSet<string> seen = new();
    bool seeded;
    int busy;
    readonly List<MailItem> recent = new();

    public int Unread { get; private set; }
    public IReadOnlyList<MailItem> Recent { get { lock (recent) return recent.ToList(); } }
    public IReadOnlyList<Meeting> Meetings { get; private set; } = Array.Empty<Meeting>();
    public string? Problem { get; private set; }
    public bool Configured => settings().ImapServer.Length > 0 || settings().UseOutlook;

    /// <summary>Raised on a worker thread for each new message.</summary>
    public event Action<MailItem>? NewMail;

    public MailHub(Func<Settings> settings)
    {
        this.settings = settings;
        timer = new System.Threading.Timer(_ => Poll(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    DateTime lastMail = DateTime.MinValue, lastCal = DateTime.MinValue;

    /// <summary>Forget what's been seen, e.g. after the settings change.</summary>
    public void Reset() { lock (seen) { seen.Clear(); seeded = false; } lastMail = DateTime.MinValue; lastCal = DateTime.MinValue; Problem = null; }

    void Poll()
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            var s = settings();
            if (!Configured) { Problem = null; return; }
            if (DateTime.UtcNow - lastMail >= TimeSpan.FromMinutes(Math.Max(1, s.MailEveryMin)))
            {
                lastMail = DateTime.UtcNow;
                var found = new List<MailItem>();
                int unread = 0;
                string? problem = null;
                if (s.ImapServer.Length > 0)
                {
                    try
                    {
                        var (n, items) = Imap.CheckAsync(s.ImapServer, s.ImapPort, s.ImapUser, Secret.Unprotect(s.ImapPasswordProtected), KnownIds())
                            .GetAwaiter().GetResult();
                        unread += n; found.AddRange(items);
                    }
                    catch (Exception ex) { problem = "Email: " + ex.Message; }
                }
                if (s.UseOutlook)
                {
                    try { var (n, items) = Outlook.Inbox(); unread += n; found.AddRange(items); }
                    catch (Exception ex) { problem = "Outlook: " + ex.Message; }
                }
                Problem = problem;
                Unread = unread;
                Take(found);
            }
            if (s.UseOutlook && DateTime.UtcNow - lastCal >= TimeSpan.FromMinutes(2))
            {
                lastCal = DateTime.UtcNow;
                try { Meetings = Outlook.Today(); } catch { }
            }
        }
        finally { busy = 0; }
    }

    HashSet<string> KnownIds() { lock (seen) return new HashSet<string>(seen); }

    /// <summary>Records messages; the first check only learns what's already there.</summary>
    public void Take(IEnumerable<MailItem> items)
    {
        var fresh = new List<MailItem>();
        lock (seen)
        {
            foreach (var m in items)
                if (seen.Add(m.Id) && seeded) fresh.Add(m);
            seeded = true;
        }
        lock (recent)
        {
            foreach (var m in items.Reverse()) if (!recent.Any(r => r.Id == m.Id)) recent.Insert(0, m);
            if (recent.Count > 20) recent.RemoveRange(20, recent.Count - 20);
        }
        foreach (var m in fresh) NewMail?.Invoke(m);
    }

    public void Dispose() => timer.Dispose();

    /// <summary>Tries the email settings and returns a message for the Settings page.</summary>
    public static async Task<string> TestImap(string server, int port, string user, string password)
    {
        try
        {
            var (n, _) = await Imap.CheckAsync(server, port, user, password, new HashSet<string>());
            return $"It works! You have {n} unread email{(n == 1 ? "" : "s")}.";
        }
        catch (Exception ex) { return "That didn't work: " + ex.Message; }
    }
}

/// <summary>A tiny read-only IMAP client: counts unread mail and reads the From/Subject of new ones. It never marks anything read.</summary>
public static class Imap
{
    public static async Task<(int unread, List<MailItem> items)> CheckAsync(string server, int port, string user, string password, HashSet<string> known)
    {
        using var tcp = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await tcp.ConnectAsync(server, port <= 0 ? 993 : port, cts.Token);
        using var ssl = new SslStream(tcp.GetStream());
        await ssl.AuthenticateAsClientAsync(server);
        var io = new Conn(ssl);
        await io.ReadLine(); // greeting
        var login = await io.Cmd($"LOGIN {Quote(user)} {Quote(password)}");
        if (!login.ok) throw new Exception(login.last.Contains("AUTHENTICATIONFAILED") || login.last.Contains("Invalid") ?
            "the email or app password was refused" : "sign-in failed: " + Trim(login.last));
        var ex = await io.Cmd("EXAMINE INBOX");
        if (!ex.ok) throw new Exception("can't open the inbox");
        var search = await io.Cmd("UID SEARCH UNSEEN");
        var uids = new List<string>();
        foreach (var l in search.lines)
            if (l.StartsWith("* SEARCH", StringComparison.OrdinalIgnoreCase))
                uids.AddRange(l[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var items = new List<MailItem>();
        var newest = uids.Select(u => long.TryParse(u, out var n) ? n : 0).Where(n => n > 0).OrderByDescending(n => n)
            .Take(15).ToList();
        var want = newest.Where(u => !known.Contains("imap:" + u)).ToList();
        var details = new Dictionary<long, (string from, string subject)>();
        if (want.Count > 0)
        {
            var f = await io.Cmd($"UID FETCH {string.Join(',', want)} (UID BODY.PEEK[HEADER.FIELDS (FROM SUBJECT)])");
            details = ParseFetch(f.lines);
        }
        foreach (var u in newest)
        {
            details.TryGetValue(u, out var d);
            items.Add(new MailItem("imap:" + u, d.from ?? "", d.subject ?? "", DateTime.Now));
        }
        try { await io.Cmd("LOGOUT"); } catch { }
        return (uids.Count, items);
    }

    static string Trim(string s) { var i = s.IndexOf(' '); i = i < 0 ? -1 : s.IndexOf(' ', i + 1); return i < 0 ? s : s[(i + 1)..]; }

    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>Reads FETCH responses: header blocks come as literals after "{n}".</summary>
    public static Dictionary<long, (string from, string subject)> ParseFetch(List<string> lines)
    {
        var res = new Dictionary<long, (string, string)>();
        long uid = 0; var header = new StringBuilder();
        void Flush()
        {
            if (uid == 0) return;
            var h = header.ToString();
            res[uid] = (Mime.DisplayName(HeaderValue(h, "From")), Mime.Decode(HeaderValue(h, "Subject")));
            uid = 0; header.Clear();
        }
        foreach (var l in lines)
        {
            if (l.StartsWith("* ") && l.Contains("FETCH", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
                var m = Regex.Match(l, @"UID (\d+)", RegexOptions.IgnoreCase);
                uid = m.Success ? long.Parse(m.Groups[1].Value) : 0;
                continue;
            }
            if (uid != 0) header.Append(l).Append('\n');
        }
        Flush();
        return res;
    }

    static string HeaderValue(string headers, string name)
    {
        // unfold continuation lines
        var unfolded = Regex.Replace(headers, @"\r?\n[ \t]+", " ");
        foreach (var line in unfolded.Split('\n'))
            if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) return line[(name.Length + 1)..].Trim();
        return "";
    }

    sealed class Conn
    {
        readonly Stream s;
        readonly byte[] buf = new byte[8192];
        int len, pos, tag;
        public Conn(Stream s) { this.s = s; }

        public async Task<string> ReadLine()
        {
            var sb = new List<byte>();
            while (true)
            {
                if (pos >= len) { len = await s.ReadAsync(buf); pos = 0; if (len <= 0) throw new Exception("the server hung up"); }
                byte b = buf[pos++];
                if (b == '\n') break;
                if (b != '\r') sb.Add(b);
            }
            return Encoding.UTF8.GetString(sb.ToArray());
        }

        async Task<string> ReadBytes(int n)
        {
            var bytes = new byte[n]; int got = 0;
            while (got < n)
            {
                if (pos >= len) { len = await s.ReadAsync(buf); pos = 0; if (len <= 0) throw new Exception("the server hung up"); }
                int take = Math.Min(n - got, len - pos);
                Array.Copy(buf, pos, bytes, got, take); pos += take; got += take;
            }
            return Encoding.UTF8.GetString(bytes);
        }

        public async Task<(bool ok, List<string> lines, string last)> Cmd(string cmd)
        {
            var t = "a" + (++tag);
            var data = Encoding.UTF8.GetBytes(t + " " + cmd + "\r\n");
            await s.WriteAsync(data); await s.FlushAsync();
            var lines = new List<string>();
            while (true)
            {
                var line = await ReadLine();
                var lit = Regex.Match(line, @"\{(\d+)\}$");
                if (lit.Success)
                {
                    lines.Add(line);
                    var body = await ReadBytes(int.Parse(lit.Groups[1].Value));
                    lines.AddRange(body.Replace("\r", "").Split('\n'));
                    continue;
                }
                if (line.StartsWith(t + " ")) return (line.Substring(t.Length + 1).StartsWith("OK", StringComparison.OrdinalIgnoreCase), lines, line);
                lines.Add(line);
            }
        }
    }
}

/// <summary>Decodes MIME header words like =?UTF-8?B?SGVsbG8=?=.</summary>
public static class Mime
{
    public static string Decode(string s)
    {
        if (!s.Contains("=?")) return s;
        s = Regex.Replace(s, @"\?=\s+=\?", "?==?");
        return Regex.Replace(s, @"=\?([^?]+)\?([bBqQ])\?([^?]*)\?=", m =>
        {
            try
            {
                var enc = Encoding.GetEncoding(m.Groups[1].Value.Equals("utf8", StringComparison.OrdinalIgnoreCase) ? "utf-8" : m.Groups[1].Value);
                var text = m.Groups[3].Value;
                byte[] bytes;
                if (m.Groups[2].Value is "B" or "b") bytes = Convert.FromBase64String(text);
                else
                {
                    var list = new List<byte>();
                    for (int i = 0; i < text.Length; i++)
                    {
                        if (text[i] == '_') list.Add(32);
                        else if (text[i] == '=' && i + 2 < text.Length) { list.Add(Convert.ToByte(text.Substring(i + 1, 2), 16)); i += 2; }
                        else list.Add((byte)text[i]);
                    }
                    bytes = list.ToArray();
                }
                return enc.GetString(bytes);
            }
            catch { return m.Value; }
        });
    }

    /// <summary>"Jane Doe" &lt;jane@x.com&gt; → Jane Doe &lt;jane@x.com&gt; (keeps the address so filters can match it).</summary>
    public static string DisplayName(string from)
    {
        from = Decode(from).Trim();
        var m = Regex.Match(from, "^\"?([^\"<]*)\"?\\s*<([^>]+)>");
        if (!m.Success) return from;
        var name = m.Groups[1].Value.Trim();
        return name.Length == 0 ? m.Groups[2].Value : $"{name} <{m.Groups[2].Value}>";
    }

    /// <summary>Just the name part, for display.</summary>
    public static string ShortName(string from)
    {
        var i = from.IndexOf('<');
        return i > 0 ? from[..i].Trim() : from;
    }
}

/// <summary>Classic Outlook for Windows, through COM. Only used while Outlook is already running.</summary>
public static class Outlook
{
    static bool Running() => System.Diagnostics.Process.GetProcessesByName("OUTLOOK").Length > 0;

    static dynamic App()
    {
        var t = Type.GetTypeFromProgID("Outlook.Application") ?? throw new Exception("classic Outlook isn't installed");
        return Activator.CreateInstance(t)!;
    }

    public static (int unread, List<MailItem> items) Inbox()
    {
        if (!Running()) throw new Exception("Outlook isn't open");
        dynamic app = App();
        dynamic inbox = app.GetNamespace("MAPI").GetDefaultFolder(6);
        dynamic unread = inbox.Items.Restrict("[Unread] = True");
        int count = unread.Count;
        var list = new List<MailItem>();
        unread.Sort("[ReceivedTime]", true);
        int n = 0;
        foreach (dynamic m in unread)
        {
            if (++n > 15) break;
            try { list.Add(new MailItem("ol:" + (string)m.EntryID, $"{(string)m.SenderName} <{(string)m.SenderEmailAddress}>", (string)m.Subject, DateTime.Now)); }
            catch { }
        }
        return (count, list);
    }

    public static List<Meeting> Today()
    {
        if (!Running()) return new List<Meeting>();
        dynamic app = App();
        dynamic items = app.GetNamespace("MAPI").GetDefaultFolder(9).Items;
        items.IncludeRecurrences = true;
        items.Sort("[Start]");
        var from = DateTime.Today; var to = from.AddDays(1);
        string f = $"[Start] >= '{from:g}' AND [Start] < '{to:g}'";
        dynamic today = items.Restrict(f);
        var list = new List<Meeting>();
        foreach (dynamic a in today)
        {
            try { list.Add(new Meeting((string)a.Subject, (DateTime)a.Start, (DateTime)a.End, (string)(a.Location ?? ""))); } catch { }
            if (list.Count > 30) break;
        }
        return list;
    }
}

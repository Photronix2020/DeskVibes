using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DeskVibes;

public static class Json
{
    public static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string Write<T>(T v) => JsonSerializer.Serialize(v, Opts);
    public static T? Read<T>(string s) => JsonSerializer.Deserialize<T>(s, Opts);
    public static T Clone<T>(T v) => Read<T>(Write(v))!;
}

/// <summary>Everything DeskVibes saves, in %APPDATA%\DeskVibes.</summary>
public sealed class Store
{
    public string Dir { get; }
    public List<Vibe> Vibes { get; private set; } = new();
    public Settings Settings { get; private set; } = new();
    public bool FirstRun { get; private set; }

    public Store(string? dir = null)
    {
        Dir = dir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeskVibes");
        Directory.CreateDirectory(Dir);
    }

    string VibesFile => Path.Combine(Dir, "vibes.json");
    string SettingsFile => Path.Combine(Dir, "settings.json");

    public void Load()
    {
        Settings = TryRead<Settings>(SettingsFile) ?? new Settings();
        var v = TryRead<List<Vibe>>(VibesFile);
        FirstRun = v == null;
        Vibes = v ?? new List<Vibe>();
    }

    static T? TryRead<T>(string path)
    {
        try { return File.Exists(path) ? Json.Read<T>(File.ReadAllText(path)) : default; }
        catch
        {
            try { File.Copy(path, path + ".broken", true); } catch { }
            return default;
        }
    }

    public void Save()
    {
        WriteSafe(VibesFile, Json.Write(Vibes));
        WriteSafe(SettingsFile, Json.Write(Settings));
    }

    static void WriteSafe(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, true);
    }

    public Vibe? Get(string id) => Vibes.FirstOrDefault(v => v.Id == id);

    public void Upsert(Vibe v)
    {
        int i = Vibes.FindIndex(x => x.Id == v.Id);
        if (i >= 0) Vibes[i] = v; else Vibes.Add(v);
        Save();
    }

    public void Remove(string id)
    {
        Vibes.RemoveAll(v => v.Id == id);
        Save();
    }
}

/// <summary>Share codes: a vibe packed into text you can paste to a friend. "DV1:" + base64(deflate(json)).</summary>
public static class Share
{
    const string Prefix = "DV1:";

    public static string Encode(Vibe v)
    {
        var copy = v.Clone();
        copy.Place.Note = "";
        var bytes = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(copy));
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.SmallestSize)) z.Write(bytes);
        return Prefix + Convert.ToBase64String(ms.ToArray());
    }

    public static Vibe? Decode(string code)
    {
        try
        {
            code = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
            int at = code.IndexOf(Prefix, StringComparison.Ordinal);
            if (at < 0) return null;
            var data = Convert.FromBase64String(code[(at + Prefix.Length)..]);
            using var z = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
            using var r = new StreamReader(z, Encoding.UTF8);
            var v = Json.Read<Vibe>(r.ReadToEnd());
            if (v == null) return null;
            v.Id = Vibe.NewId();
            bool known = v.IsWidget ? v.Widget != null && Catalog.Find(v.Widget.Type) != null
                                    : Catalog.Find(v.When.Type) != null && v.Do.All(a => Catalog.Find(a.Type) != null);
            return known ? v : null;
        }
        catch { return null; }
    }
}

/// <summary>Windows DPAPI: secrets are encrypted so only this Windows user can read them.</summary>
public static class Secret
{
    [StructLayout(LayoutKind.Sequential)]
    struct Blob { public int cb; public IntPtr pb; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob input, string? desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref Blob output);

    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);

    static byte[]? Run(byte[] data, bool protect)
    {
        var inBlob = new Blob { cb = data.Length, pb = Marshal.AllocHGlobal(Math.Max(1, data.Length)) };
        var outBlob = new Blob();
        try
        {
            Marshal.Copy(data, 0, inBlob.pb, data.Length);
            bool ok = protect ? CryptProtectData(ref inBlob, "DeskVibes", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref outBlob)
                              : CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, ref outBlob);
            if (!ok) return null;
            var res = new byte[outBlob.cb];
            Marshal.Copy(outBlob.pb, res, 0, outBlob.cb);
            return res;
        }
        finally
        {
            Marshal.FreeHGlobal(inBlob.pb);
            if (outBlob.pb != IntPtr.Zero) LocalFree(outBlob.pb);
        }
    }

    public static string Protect(string plain) =>
        string.IsNullOrEmpty(plain) ? "" : Convert.ToBase64String(Run(Encoding.UTF8.GetBytes(plain), true) ?? Array.Empty<byte>());

    public static string Unprotect(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try { var b = Run(Convert.FromBase64String(stored), false); return b == null ? "" : Encoding.UTF8.GetString(b); }
        catch { return ""; }
    }
}

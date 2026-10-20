using System.Diagnostics;
using System.Media;

namespace DeskVibes;

/// <summary>Carries out the "then do this" part of a vibe.</summary>
public static class Actions
{
    public static Engine? Engine;
    /// <summary>Set while DeskVibes itself copies text, so "I copy some text" doesn't fire on it.</summary>
    public static DateTime SelfCopyUntil = DateTime.MinValue;
    static dynamic? voice;

    public static void Run(Vibe v, Step a, Ctx ctx)
    {
        var now = DateTime.Now;
        string F(string key) => ctx.Fill(a.Get(key), now);
        switch (a.Type)
        {
            case "show.edges":
                Overlays.Edges(a.Color("color", Color.Red), a.Get("style"), (int)a.Num("size", 26), a.Num("seconds", 0));
                break;
            case "show.card":
                Cards.Show(F("title"), F("text"), a.Color("color", Theme.Accent), a.Num("seconds", 10), a.Get("where"), F("link"));
                break;
            case "show.banner":
                Overlays.Banner(F("text"), a.Color("color", Color.Orange), a.Num("seconds", 6));
                break;
            case "show.flash":
                Overlays.Flash(a.Color("color", Color.White), (int)a.Num("times", 2));
                break;
            case "show.dim":
                Overlays.Dim(F("text"), (int)a.Num("percent", 70), a.Num("seconds", 0));
                break;
            case "sound.play":
                PlaySound(a.Get("sound"), a.Get("file"), (int)a.Num("times", 1));
                break;
            case "sound.speak":
                Speak(F("text"));
                break;
            case "do.open":
                OpenThing(F("target"));
                break;
            case "do.copy":
                SelfCopyUntil = DateTime.Now.AddSeconds(1);
                var text = F("text");
                if (text.Length > 0) Clipboard.SetText(text);
                break;
            case "do.desktop":
            {
                var t = Type.GetTypeFromProgID("Shell.Application");
                if (t != null) { dynamic shell = Activator.CreateInstance(t)!; shell.MinimizeAll(); }
                break;
            }
            case "do.mute":
                Native.PressKey(0xAD);
                break;
            case "do.lock":
                Native.LockWorkStation();
                break;
            case "do.log":
                Engine?.Note("📝 " + F("text"));
                break;
            default:
                throw new Exception("unknown action " + a.Type);
        }
    }

    static readonly string Media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    public static void PlaySound(string sound, string file, int times)
    {
        string? path = sound switch
        {
            "Chime" => Path.Combine(Media, "Windows Notify System Generic.wav"),
            "Ding" => Path.Combine(Media, "Windows Ding.wav"),
            "Email" => Path.Combine(Media, "Windows Notify Email.wav"),
            "Calendar" => Path.Combine(Media, "Windows Notify Calendar.wav"),
            "Alert" => Path.Combine(Media, "Windows Exclamation.wav"),
            "Error" => Path.Combine(Media, "Windows Critical Stop.wav"),
            "Question" => Path.Combine(Media, "Windows Background.wav"),
            "A sound file" => file,
            _ => null,
        };
        times = Math.Clamp(times, 1, 10);
        Task.Run(async () =>
        {
            for (int i = 0; i < times; i++)
            {
                if (path != null && File.Exists(path)) { using var p = new SoundPlayer(path); p.PlaySync(); }
                else { SystemSounds.Asterisk.Play(); await Task.Delay(700); }
                if (i < times - 1) await Task.Delay(250);
            }
        });
    }

    public static void Speak(string text)
    {
        if (text.Trim().Length == 0) return;
        if (voice == null)
        {
            var t = Type.GetTypeFromProgID("SAPI.SpVoice") ?? throw new Exception("Windows speech isn't available");
            voice = Activator.CreateInstance(t);
        }
        voice!.Speak(text, 1 | 2); // async, purge anything still being said
    }

    /// <summary>Opens a link, file, folder or program with whatever Windows normally uses.</summary>
    public static void OpenThing(string target)
    {
        target = target.Trim().Trim('"');
        if (target.Length == 0 || target == "https://") return;
        if (target.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) target = "https://" + target;
        if (target.Length == 2 && target[1] == ':') target += "\\";
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
    }
}

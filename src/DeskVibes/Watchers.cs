namespace DeskVibes;

/// <summary>A hidden window that hears Windows messages: hotkeys, clipboard changes, lock and unlock.</summary>
public sealed class Watchers : NativeWindow, IDisposable
{
    public static readonly List<string> ClipHistory = new();
    readonly Action<string, Ctx> push;
    readonly Dictionary<int, string> hotkeys = new();
    int nextId = 1;

    public Watchers(Action<string, Ctx> push)
    {
        this.push = push;
        CreateHandle(new CreateParams { Caption = "DeskVibesWatcher", Parent = new IntPtr(-3) /* HWND_MESSAGE */ });
        Native.AddClipboardFormatListener(Handle);
        Native.WTSRegisterSessionNotification(Handle, 0);
    }

    /// <summary>Registers the hotkeys the vibes use. Returns the ones Windows refused (taken by another app).</summary>
    public List<string> SetHotkeys(IEnumerable<string> keys)
    {
        foreach (var id in hotkeys.Keys) Native.UnregisterHotKey(Handle, id);
        hotkeys.Clear();
        var failed = new List<string>();
        foreach (var k in keys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Native.ParseHotkey(k, out var mods, out var vk)) { failed.Add(k); continue; }
            int id = nextId++;
            if (Native.RegisterHotKey(Handle, id, mods | 0x4000 /* MOD_NOREPEAT */, vk)) hotkeys[id] = Engine.Hotkey(k);
            else failed.Add(k);
        }
        return failed;
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case Native.WM_HOTKEY:
                if (hotkeys.TryGetValue(m.WParam.ToInt32(), out var k)) push("pc.hotkey", Ctx.Of(("keys", k)));
                break;
            case Native.WM_CLIPBOARDUPDATE:
                OnClipboard();
                break;
            case Native.WM_WTSSESSION_CHANGE:
                if (m.WParam.ToInt32() == Native.WTS_SESSION_LOCK) push("pc.lock", new Ctx());
                if (m.WParam.ToInt32() == Native.WTS_SESSION_UNLOCK) push("pc.unlock", new Ctx());
                break;
        }
        base.WndProc(ref m);
    }

    void OnClipboard()
    {
        string text;
        try { if (!Clipboard.ContainsText()) return; text = Clipboard.GetText(); }
        catch { return; } // another app is holding the clipboard
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (ClipHistory)
        {
            ClipHistory.Remove(text);
            ClipHistory.Insert(0, text.Length > 5000 ? text[..5000] : text);
            if (ClipHistory.Count > 30) ClipHistory.RemoveAt(30);
        }
        if (DateTime.Now < Actions.SelfCopyUntil) return;
        push("pc.clipboard", Ctx.Of(("text", text.Length > 300 ? text[..300] : text)));
    }

    public void Dispose()
    {
        foreach (var id in hotkeys.Keys) Native.UnregisterHotKey(Handle, id);
        Native.RemoveClipboardFormatListener(Handle);
        Native.WTSUnRegisterSessionNotification(Handle);
        DestroyHandle();
    }
}

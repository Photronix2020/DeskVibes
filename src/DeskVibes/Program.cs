using System.Text;

namespace DeskVibes;

static class Program
{
    /// <summary>
    /// DeskVibes.exe            opens the window (or brings the running copy to the front)
    /// DeskVibes.exe --tray     starts quietly in the tray (used when starting with Windows)
    /// --data DIR               keeps vibes and settings in DIR instead of %APPDATA%\DeskVibes
    /// --demo                   shows the edges, a card and a banner right away
    /// --shot FILE SECONDS      saves a screenshot after SECONDS and quits (for testing)
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string? data = Arg(args, "--data");
        string? shot = Arg(args, "--shot");
        bool tray = args.Contains("--tray");

        using var mutex = new Mutex(true, data == null ? "DeskVibes.Single" : "DeskVibes.Single." + data.GetHashCode(), out bool first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, data == null ? "DeskVibes.Show" : "DeskVibes.Show." + data.GetHashCode());
        if (!first)
        {
            showSignal.Set();
            return;
        }

        ApplicationConfiguration.Initialize();
        var store = new Store(data);
        store.Load();

        // --render-ui DIR: draws the windows into PNG files without showing them (for checking the layout)
        string? render = Arg(args, "--render-ui");
        if (render != null)
        {
            var quiet = new TrayApp(store, false);
            Theme.Quiet = true;
            void Snap(Form f, string file)
            {
                f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-30000, -30000);
                if (!f.Visible) f.Show();
                f.Refresh(); Application.DoEvents();
                using var bmp = new Bitmap(f.Width, f.Height);
                using (var g = Graphics.FromImage(bmp))
                {
                    var hdc = g.GetHdc();
                    PrintWindow(f.Handle, hdc, 2 /* PW_RENDERFULLCONTENT */);
                    g.ReleaseHdc(hdc);
                }
                bmp.Save(Path.Combine(render, file), System.Drawing.Imaging.ImageFormat.Png);
            }
            var main = new MainForm(quiet);
            foreach (var page in new[] { "vibes", "ideas", "settings" }) { main.Show(page); Snap(main, $"ui-{page}.png"); }
            // the lower half of Settings too
            static IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(x => All(x).Prepend(x));
            var settingsPage = All(main).OfType<ScrollableControl>().FirstOrDefault(c => c.AutoScroll && c.Visible);
            if (settingsPage != null)
            {
                foreach (var y in new[] { 900, 100000 })
                {
                    settingsPage.AutoScrollPosition = new Point(0, y);
                    Snap(main, y == 900 ? "ui-settings-2.png" : "ui-settings-3.png");
                }
                settingsPage.AutoScrollPosition = new Point(0, 0);
            }
            var r = Describe.Parse("when I get an email from my boss turn the edges of the screen red");
            Snap(new BuilderForm(r.Vibe, quiet.Engine, true, string.Join("\n", r.Understood)), "ui-builder.png");
            var ai = AiDescribe.FromJson("""
                {"kind":"rule","name":"Boss email → red edges","trigger":{"type":"mail.new","answers":[{"key":"from","value":"boss"}]},
                 "actions":[{"type":"show.edges","answers":[{"key":"color","value":"#FF3B30"},{"key":"style","value":"Blink"}]},
                            {"type":"sound.speak","answers":[{"key":"text","value":"Your boss wrote: {subject}"}]}],
                 "only":["work","nogame"],"cooldown_minutes":0,"widget":null,
                 "notes":["I took \"my boss\" to mean any sender with boss in the name or address. Put their real address in if that's too broad."]}
                """)!;
            Snap(new BuilderForm(ai.Vibe, quiet.Engine, true, string.Join("\n", ai.Understood)), "ui-builder-ai.png");
            Snap(new BuilderForm(Gallery.Ideas.First(i => i.Make().IsWidget).Make(), quiet.Engine, true), "ui-builder-widget.png");
            var shown = new List<(WidgetForm f, string n)>();
            foreach (var idea in Gallery.Ideas.Where(i => i.Category == "Widgets"))
            {
                var v = idea.Make();
                v.Place.X = -30000; v.Place.Y = -30000;
                if (v.Widget!.Type == "w.note") v.Place.Note = "Call mom\r\nBuy milk";
                var wf = new WidgetForm(v, new WidgetContent(v, quiet.Engine, () => store.Settings), _ => { }, _ => { });
                wf.Show(); wf.Location = new Point(-30000, -30000);
                shown.Add((wf, v.Widget.Type));
            }
            var until = DateTime.Now.AddSeconds(8);
            while (DateTime.Now < until) { Application.DoEvents(); foreach (var (f, _) in shown) f.Invalidate(); Thread.Sleep(100); }
            foreach (var (f, n) in shown) Snap(f, $"widget-{n}.png");
            quiet.Quit();
            return;
        }
        var app = new TrayApp(store, !tray && shot == null);

        // a second launch asks this copy to show its window
        var waiter = new Thread(() =>
        {
            while (showSignal.WaitOne())
                app.Post(() => app.ShowMain());
        }) { IsBackground = true };
        waiter.Start();

        if (args.Contains("--demo"))
        {
            var t = new System.Windows.Forms.Timer { Interval = 800 };
            t.Tick += (_, _) =>
            {
                t.Stop();
                Overlays.Edges(Colors.Parse("Red", Color.Red), "Solid", 26, 5);
                Cards.Show("New email from Your Boss", "Can you send me the report before 5?", Colors.Parse("Red", Color.Red), 6, "Bottom right", "");
                Cards.Show("☂ Rain on the way", "Rain around 3 PM (70%) in Boston", Colors.Parse("Blue", Color.Blue), 6, "Bottom right", "");
                Overlays.Banner("Stormrage is back up!", Colors.Parse("Green", Color.Green), 5);
            };
            t.Start();
        }
        if (shot != null)
        {
            double secs = double.TryParse(args.SkipWhile(a => a != "--shot").Skip(2).FirstOrDefault(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 4;
            var t = new System.Windows.Forms.Timer { Interval = (int)(secs * 1000) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                var b = SystemInformation.VirtualScreen;
                using var bmp = new Bitmap(b.Width, b.Height);
                using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
                bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                app.Quit();
            };
            t.Start();
        }
        Application.Run(app);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

    static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

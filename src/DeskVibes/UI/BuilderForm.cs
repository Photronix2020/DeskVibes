namespace DeskVibes;

/// <summary>The no-code builder: "When this happens… do this", or a widget. Forms come from <see cref="Catalog"/>.</summary>
public sealed class BuilderForm : Form
{
    public Vibe Result { get; private set; }
    readonly Engine engine;
    readonly FlowLayoutPanel body;
    readonly TextBox name;
    readonly Label hint;
    bool nameTouched;
    const int W = 560;

    public BuilderForm(Vibe vibe, Engine engine, bool isNew, string notes = "")
    {
        Result = vibe.Clone();
        this.engine = engine;
        Text = (isNew ? "New " : "Change ") + (Result.IsWidget ? "widget" : "vibe");
        Icon = Theme.MakeIcon();
        BackColor = Theme.Back; ForeColor = Theme.Text; Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(W + 60, 680);
        MinimumSize = new Size(500, 400);
        nameTouched = !isNew;

        body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            Padding = new Padding(22, 14, 22, 14), BackColor = Theme.Back,
        };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(14, 8, 14, 8), BackColor = Theme.Panel };
        var save = Theme.Button("Save", true); save.Click += (_, _) => { Finish(); };
        var cancel = Theme.Button("Cancel"); cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        bottom.Controls.Add(save); bottom.Controls.Add(cancel);
        if (!Result.IsWidget)
        {
            var test = Theme.Button("▶ Try it now"); test.Click += (_, _) => { Collect(); engine.Test(Result); };
            bottom.Controls.Add(test);
        }
        hint = Theme.Label("", Theme.Small, Theme.Faint);
        hint.Margin = new Padding(10, 12, 3, 3);
        bottom.Controls.Add(hint);
        Controls.Add(body);
        Controls.Add(bottom);
        AcceptButton = null; CancelButton = cancel;

        if (notes.Length > 0)
        {
            var n = Theme.Label(notes, Theme.Small, Theme.Dim);
            n.MaximumSize = new Size(W, 0); n.BackColor = Theme.Panel; n.Padding = new Padding(10, 8, 10, 8); n.Margin = new Padding(0, 0, 0, 8);
            body.Controls.Add(Theme.Label("Here's what I understood", Theme.Title));
            body.Controls.Add(n);
        }
        name = Box(Result.Name);
        name.TextChanged += (_, _) => { if (name.Focused) nameTouched = true; };
        Section("Name");
        body.Controls.Add(name);

        if (Result.IsWidget) BuildWidget(); else BuildRule();
        Load += (_, _) => Theme.DarkTitle(this);
    }

    TextBox Box(string text, bool multi = false)
    {
        var t = new TextBox { Text = text, Width = W - 10, Multiline = multi, Height = multi ? 64 : 26, Margin = new Padding(3, 2, 3, 8) };
        if (multi) { t.ScrollBars = ScrollBars.Vertical; t.AcceptsReturn = true; }
        Theme.Style(t);
        return t;
    }

    void Section(string text, string sub = "")
    {
        var l = Theme.Label(text, Theme.Title, Theme.Text);
        l.Margin = new Padding(0, body.Controls.Count == 0 ? 0 : 14, 0, 2);
        body.Controls.Add(l);
        if (sub.Length > 0) { var s = Theme.Label(sub, Theme.Small, Theme.Faint); s.MaximumSize = new Size(W, 0); s.Margin = new Padding(1, 0, 0, 6); body.Controls.Add(s); }
    }

    protected override bool ShowWithoutActivation => Theme.Quiet;

    // ---------- rules ----------

    Panel? whenFields;
    Label? whenHelp;
    FlowLayoutPanel? actionList;
    Label? placeholders;

    void BuildRule()
    {
        Section("When this happens");
        var pick = Picker(Catalog.Triggers, Result.When.Type);
        body.Controls.Add(pick);
        whenHelp = Theme.Label("", Theme.Small, Theme.Faint); whenHelp.MaximumSize = new Size(W, 0);
        body.Controls.Add(whenHelp);
        whenFields = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        body.Controls.Add(whenFields);
        void ShowWhen()
        {
            var d = Catalog.Find(Result.When.Type)!;
            whenHelp!.Text = d.Help; whenHelp.Visible = d.Help.Length > 0;
            whenFields!.Controls.Clear();
            foreach (var f in d.Fields) whenFields.Controls.Add(Fields.Make(f, Result.When, W, Changed));
            UpdatePlaceholders();
        }
        pick.SelectedIndexChanged += (_, _) =>
        {
            var d = ((Item)pick.SelectedItem!).Def;
            if (d.Id == Result.When.Type) return;
            Result.When = new Step(d.Id);
            ShowWhen(); Changed();
        };
        ShowWhen();

        Section("Do this");
        placeholders = Theme.Label("", Theme.Small, Theme.Faint); placeholders.MaximumSize = new Size(W, 0);
        body.Controls.Add(placeholders);
        actionList = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        body.Controls.Add(actionList);
        if (Result.Do.Count == 0) Result.Do.Add(new Step("show.card"));
        foreach (var a in Result.Do) AddActionEditor(a);
        var more = Theme.Button("+ Add another thing to do");
        more.Click += (_, _) => { var s = new Step("sound.play"); Result.Do.Add(s); AddActionEditor(s); Changed(); };
        body.Controls.Add(more);

        Section("Only if", "Leave these empty to always run.");
        var only = new FlowLayoutPanel { Width = W, AutoSize = true, Margin = new Padding(0) };
        foreach (var c in Catalog.Conditions)
        {
            var cb = new CheckBox { Text = c.Label, Checked = Result.Only.Contains(c.Id), AutoSize = true, Margin = new Padding(3, 3, 14, 3) };
            Theme.Style(cb);
            if (c.Help.Length > 0) new ToolTip().SetToolTip(cb, c.Help);
            cb.CheckedChanged += (_, _) => { Result.Only.Remove(c.Id); if (cb.Checked) Result.Only.Add(c.Id); };
            only.Controls.Add(cb);
        }
        body.Controls.Add(only);

        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        row.Controls.Add(Theme.Label("Don't repeat within"));
        var cool = new NumericUpDown { Minimum = 0, Maximum = 10080, Value = Math.Min(10080, Result.CooldownSec / 60), Width = 70 };
        Theme.Style(cool);
        cool.ValueChanged += (_, _) => Result.CooldownSec = (int)cool.Value * 60;
        row.Controls.Add(cool);
        row.Controls.Add(Theme.Label("minutes (0 = no limit)", null, Theme.Dim));
        body.Controls.Add(row);
    }

    void AddActionEditor(Step a)
    {
        var box = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Panel,
            Padding = new Padding(10, 8, 10, 6), Margin = new Padding(0, 0, 0, 8), MinimumSize = new Size(W, 0), MaximumSize = new Size(W + 10, 0),
        };
        var top = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
        var pick = Picker(Catalog.Actions, a.Type, W - 100);
        var remove = Theme.Button("✕"); remove.Padding = new Padding(4, 0, 4, 0);
        top.Controls.Add(pick); top.Controls.Add(remove);
        box.Controls.Add(top);
        var help = Theme.Label("", Theme.Small, Theme.Faint); help.MaximumSize = new Size(W - 20, 0);
        box.Controls.Add(help);
        var fields = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        box.Controls.Add(fields);
        void Show()
        {
            var d = Catalog.Find(a.Type)!;
            help.Text = d.Help; help.Visible = d.Help.Length > 0;
            fields.Controls.Clear();
            foreach (var f in d.Fields) fields.Controls.Add(Fields.Make(f, a, W - 20, Changed));
        }
        pick.SelectedIndexChanged += (_, _) =>
        {
            var d = ((Item)pick.SelectedItem!).Def;
            if (d.Id == a.Type) return;
            a.Type = d.Id; a.P.Clear();
            // fill texts with something sensible for this trigger
            var (title, text) = Describe.CardText(Result.When.Type);
            if (d.Id == "show.card") { a.P["title"] = title; a.P["text"] = text; }
            if (d.Id is "show.banner" or "do.log") a.P["text"] = (title + (text.Length > 0 ? " — " + text : "")).Replace("\n", " ");
            Show(); Changed();
        };
        remove.Click += (_, _) =>
        {
            if (Result.Do.Count <= 1) { hint.Text = "A vibe needs at least one thing to do."; return; }
            Result.Do.Remove(a); actionList!.Controls.Remove(box); box.Dispose(); Changed();
        };
        Show();
        actionList!.Controls.Add(box);
    }

    void UpdatePlaceholders()
    {
        if (placeholders == null) return;
        var gives = Catalog.Find(Result.When.Type)!.Gives.Select(g => "{" + g + "}").Concat(new[] { "{vibe}", "{time}" });
        placeholders.Text = "In texts you can use " + string.Join(" ", gives);
    }

    // ---------- widgets ----------

    void BuildWidget()
    {
        Result.Widget ??= new Step("w.clock");
        Section("Widget");
        var pick = Picker(Catalog.Widgets, Result.Widget.Type);
        body.Controls.Add(pick);
        var help = Theme.Label("", Theme.Small, Theme.Faint); help.MaximumSize = new Size(W, 0);
        body.Controls.Add(help);
        var fields = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0) };
        body.Controls.Add(fields);
        void Show()
        {
            var d = Catalog.Find(Result.Widget!.Type)!;
            help.Text = d.Help; help.Visible = d.Help.Length > 0;
            fields.Controls.Clear();
            foreach (var f in d.Fields) fields.Controls.Add(Fields.Make(f, Result.Widget, W, Changed));
        }
        pick.SelectedIndexChanged += (_, _) =>
        {
            var d = ((Item)pick.SelectedItem!).Def;
            if (d.Id == Result.Widget!.Type) return;
            Result.Widget = new Step(d.Id);
            Show(); Changed();
        };
        Show();

        Section("Where it sits");
        var p = Result.Place;
        foreach (var (val, label) in new[] { ("top", "Always on top of other windows"), ("normal", "Like a normal window"), ("desktop", "On the desktop, behind other windows") })
        {
            var r = new RadioButton { Text = label, Checked = p.Layer == val, AutoSize = true };
            Theme.Style(r);
            r.CheckedChanged += (_, _) => { if (r.Checked) p.Layer = val; };
            body.Controls.Add(r);
        }
        var lockIt = new CheckBox { Text = "Lock it in place", Checked = p.Locked, AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
        Theme.Style(lockIt); lockIt.CheckedChanged += (_, _) => p.Locked = lockIt.Checked;
        body.Controls.Add(lockIt);
        var through = new CheckBox { Text = "Let clicks go through it (you can't drag it while this is on)", Checked = p.ClickThrough, AutoSize = true };
        Theme.Style(through); through.CheckedChanged += (_, _) => p.ClickThrough = through.Checked;
        body.Controls.Add(through);
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        row.Controls.Add(Theme.Label("See-through"));
        var op = new TrackBar { Minimum = 20, Maximum = 100, TickFrequency = 10, Value = Math.Clamp(p.Opacity, 20, 100), Width = 260, BackColor = Theme.Back };
        var opl = Theme.Label(op.Value + "%", null, Theme.Dim);
        op.ValueChanged += (_, _) => { p.Opacity = op.Value; opl.Text = op.Value + "%"; };
        row.Controls.Add(op); row.Controls.Add(opl);
        body.Controls.Add(row);
    }

    // ---------- shared ----------

    sealed record Item(Def Def, string Text) { public override string ToString() => Text; }

    ComboBox Picker(Def[] defs, string selected, int width = W - 10)
    {
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width, MaxDropDownItems = 24, Margin = new Padding(3, 2, 3, 4) };
        Theme.Style(cb);
        bool groups = defs.Select(d => d.Group).Distinct().Count() > 1;
        foreach (var d in defs) cb.Items.Add(new Item(d, groups ? $"{d.Group}  ·  {d.Label}" : d.Label));
        cb.SelectedItem = cb.Items.Cast<Item>().FirstOrDefault(i => i.Def.Id == selected) ?? cb.Items[0];
        return cb;
    }

    void Changed()
    {
        if (nameTouched) return;
        if (Result.IsWidget) name.Text = Catalog.Find(Result.Widget!.Type)!.Label;
        else name.Text = AutoName(Result);
    }

    static string AutoName(Vibe v)
    {
        var when = Catalog.Find(v.When.Type)!.Label;
        var first = v.Do.FirstOrDefault();
        var then = first == null ? "" : first.Type == "show.edges" ? $"{Colors.NameOf(first.Get("color"))} edges" : Catalog.Find(first.Type)!.Label.ToLowerInvariant();
        return then.Length > 0 ? $"{when} → {then}" : when;
    }

    void Collect() { Result.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : "My vibe"; }

    void Finish()
    {
        Collect();
        if (!Result.IsWidget && Result.When.Type == "pc.hotkey" && !Native.ParseHotkey(Result.When.Get("keys"), out _, out _))
        { hint.Text = "I can't read that hotkey. Try something like Ctrl+Alt+K."; return; }
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>Makes an input for a catalog field and writes the answer back into the step.</summary>
public static class Fields
{
    public static Control Make(Field f, Step step, int width, Action changed)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 2, 0, 4) };
        var label = Theme.Label(f.Label + (f.Hint.Length > 0 && f.Kind != FieldKind.Check ? $"   ({f.Hint})" : ""), Theme.Small, Theme.Dim);
        label.Margin = new Padding(3, 4, 3, 1);
        if (f.Kind != FieldKind.Check) panel.Controls.Add(label);
        string value = step.P.TryGetValue(f.Key, out var v) ? v : f.Default;
        void Set(string s) { step.P[f.Key] = s; changed(); }
        if (!step.P.ContainsKey(f.Key) && f.Default.Length > 0) step.P[f.Key] = f.Default;

        switch (f.Kind)
        {
            case FieldKind.Number:
            {
                var t = new TextBox { Text = value, Width = 110 };
                Theme.Style(t);
                t.TextChanged += (_, _) => Set(t.Text.Trim());
                panel.Controls.Add(t);
                break;
            }
            case FieldKind.Check:
            {
                var c = new CheckBox { Text = f.Label, Checked = value == "true", AutoSize = true };
                Theme.Style(c);
                c.CheckedChanged += (_, _) => Set(c.Checked ? "true" : "false");
                panel.Controls.Add(c);
                break;
            }
            case FieldKind.Choice:
            {
                var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Math.Min(width - 10, 300) };
                Theme.Style(c);
                foreach (var ch in f.Choices!) c.Items.Add(ch);
                c.SelectedItem = f.Choices.Contains(value) ? value : f.Default;
                c.SelectedIndexChanged += (_, _) => Set((string)c.SelectedItem!);
                panel.Controls.Add(c);
                break;
            }
            case FieldKind.Color:
            {
                var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
                var swatches = new List<Button>();
                var more = Theme.Button("…"); more.Padding = new Padding(2, 0, 2, 0);
                foreach (var (n, hex) in Colors.Named)
                {
                    var b = new Button { Width = 30, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = Colors.Parse(hex, Color.Gray), Margin = new Padding(2), Cursor = Cursors.Hand, Tag = hex };
                    b.FlatAppearance.BorderSize = 2;
                    b.FlatAppearance.BorderColor = Theme.Back;
                    new ToolTip().SetToolTip(b, n);
                    b.Click += (_, _) => { Set(hex); Mark(); };
                    swatches.Add(b); row.Controls.Add(b);
                }
                more.Click += (_, _) =>
                {
                    using var dlg = new ColorDialog { Color = Colors.Parse(step.Get(f.Key), Color.Red), FullOpen = true };
                    if (dlg.ShowDialog() == DialogResult.OK) { Set(Colors.Hex(dlg.Color)); Mark(); }
                };
                row.Controls.Add(more);
                void Mark()
                {
                    var cur = Colors.Parse(step.Get(f.Key), Color.Empty);
                    foreach (var b in swatches) b.FlatAppearance.BorderColor = Colors.Parse((string)b.Tag!, Color.Empty).ToArgb() == cur.ToArgb() ? Theme.Text : Theme.Back;
                    more.BackColor = swatches.Any(b => Colors.Parse((string)b.Tag!, Color.Empty).ToArgb() == cur.ToArgb()) ? Theme.Raised : cur;
                }
                Mark();
                panel.Controls.Add(row);
                break;
            }
            case FieldKind.File:
            {
                var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0), WrapContents = false };
                var t = new TextBox { Text = value, Width = width - 110 };
                Theme.Style(t);
                t.TextChanged += (_, _) => Set(t.Text);
                var browse = Theme.Button("Browse…");
                browse.Click += (_, _) =>
                {
                    using var dlg = new OpenFileDialog { Title = f.Label, Filter = f.Key == "file" ? "Sounds (*.wav)|*.wav|All files|*.*" : "All files|*.*" };
                    if (dlg.ShowDialog() == DialogResult.OK) t.Text = dlg.FileName;
                };
                row.Controls.Add(t); row.Controls.Add(browse);
                panel.Controls.Add(row);
                break;
            }
            default:
            {
                bool multi = f.Kind == FieldKind.Long;
                var t = new TextBox { Text = multi ? value.Replace("\r\n", "\n").Replace("\n", "\r\n") : value, Width = width - 10, Multiline = multi, Height = multi ? 60 : 26 };
                if (multi) { t.ScrollBars = ScrollBars.Vertical; t.AcceptsReturn = true; }
                if (f.Kind == FieldKind.Time) t.Width = 110;
                Theme.Style(t);
                t.TextChanged += (_, _) => Set(t.Text.Replace("\r\n", "\n"));
                panel.Controls.Add(t);
                break;
            }
        }
        return panel;
    }
}

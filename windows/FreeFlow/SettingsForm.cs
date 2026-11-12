using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FreeFlow;

/// A dropdown that ignores the mouse wheel unless it's open, so scrolling the page never changes a setting by accident.
sealed class SafeCombo : ComboBox
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    protected override void WndProc(ref Message m)
    {
        const int WM_MOUSEWHEEL = 0x020A;
        if (m.Msg == WM_MOUSEWHEEL && !DroppedDown)
        {
            // hand the wheel to the scrolling page instead
            Control p = Parent;
            while (p != null && !(p is ScrollableControl sc && sc.AutoScroll)) p = p.Parent;
            if (p != null) SendMessage(p.Handle, m.Msg, m.WParam, m.LParam);
            return;
        }
        base.WndProc(ref m);
    }
}

sealed class SettingsForm : Form
{
    readonly Settings s = Settings.Current;

    static readonly Color PageBg = Color.FromArgb(243, 243, 247);
    static readonly Color CardBg = Color.White;
    static readonly Color Border = Color.FromArgb(226, 226, 234);
    static readonly Color Accent = Color.FromArgb(108, 63, 245);
    static readonly Color Muted = Color.FromArgb(110, 110, 125);

    const int LabelW = 190;

    public SettingsForm()
    {
        Text = "FreeFlow Settings";
        Font = new Font("Segoe UI", 9.75f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(820, 760);
        MinimumSize = new Size(640, 520);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Program.AppIcon;
        BackColor = PageBg;

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(16, 6), Font = new Font("Segoe UI", 10f) };
        tabs.TabPages.Add(General());
        tabs.TabPages.Add(Voice());
        tabs.TabPages.Add(Ai());
        tabs.TabPages.Add(Words());
        tabs.TabPages.Add(Help());
        Controls.Add(tabs);
    }

    // ───────── layout: pages of white cards that always fit the window ─────────

    sealed class Page
    {
        public TabPage Tab;
        public FlowLayoutPanel Flow;
    }

    Page NewPage(string title)
    {
        var tp = new TabPage(title) { BackColor = PageBg };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(18, 16, 18, 24), BackColor = PageBg,
        };
        tp.Controls.Add(flow);
        flow.Resize += (_, _) => Fit(flow);
        tp.VisibleChanged += (_, _) => Fit(flow);
        return new Page { Tab = tp, Flow = flow };
    }

    /// A white card with a title; returns the card to add rows to.
    FlowLayoutPanel Card(Page page, string title, string subtitle = null)
    {
        var card = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = CardBg,
            Padding = new Padding(18, 14, 18, 14), Margin = new Padding(0, 0, 0, 14), Tag = "card",
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        card.Controls.Add(new Label
        {
            Text = title, AutoSize = true, ForeColor = Accent,
            Font = new Font("Segoe UI Semibold", 12f), Margin = new Padding(0, 0, 0, subtitle == null ? 8 : 0),
        });
        if (subtitle != null) card.Controls.Add(Note(subtitle));
        page.Flow.Controls.Add(card);
        return card;
    }

    /// Resize cards, fields and notes to the window so nothing is ever cut off.
    void Fit(FlowLayoutPanel flow)
    {
        int w = flow.ClientSize.Width - flow.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 4;
        if (w < 300) return;
        flow.SuspendLayout();
        foreach (Control c in flow.Controls)
        {
            c.MinimumSize = new Size(w, 0);
            c.MaximumSize = new Size(w, 0);
            c.Width = w;
            FitInside(c, w - 36);
        }
        flow.ResumeLayout(true);
    }

    void FitInside(Control parent, int inner)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c.Tag as string)
            {
                case "note": c.MaximumSize = new Size(inner, 0); break;
                case "field": c.Width = Math.Max(160, Math.Min(440, inner - LabelW - 12)); break;
                case "full": c.Width = inner; break;
            }
            if (c.HasChildren) FitInside(c, inner);
        }
    }

    static Label Note(string t, Color? c = null) => new()
    {
        Text = t, AutoSize = true, MaximumSize = new Size(600, 0), ForeColor = c ?? Muted,
        Margin = new Padding(0, 2, 0, 8), Tag = "note",
    };

    static Control Row(string label, Control c)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
        row.Controls.Add(new Label { Text = label, Width = LabelW, Height = 30, TextAlign = ContentAlignment.MiddleLeft });
        row.Controls.Add(c);
        return row;
    }

    ComboBox Pick<T>(IList<T> values, T current, Func<T, string> label, Action<T> set)
    {
        var cb = new SafeCombo { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360, Tag = "field", FlatStyle = FlatStyle.System };
        foreach (var v in values) cb.Items.Add(label(v));
        cb.SelectedIndex = Math.Max(0, values.IndexOf(current));
        cb.SelectedIndexChanged += (_, _) => { set(values[cb.SelectedIndex]); s.Save(); };
        return cb;
    }

    CheckBox Check(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 4, 0, 4), Tag = "note" };
        c.CheckedChanged += (_, _) => { set(c.Checked); s.Save(); };
        return c;
    }

    TextBox Field(string value, Action<string> set, bool secret = false)
    {
        var t = new TextBox { Text = value ?? "", Width = 360, UseSystemPasswordChar = secret, Tag = "field" };
        t.TextChanged += (_, _) => { set(t.Text.Trim()); s.Save(); };
        return t;
    }

    TextBox Multi(string value, Action<string> set, int height = 140)
    {
        var t = new TextBox
        {
            Text = (value ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"), Multiline = true, ScrollBars = ScrollBars.Vertical,
            Width = 600, Height = height, AcceptsReturn = true, Font = new Font("Consolas", 10f), Tag = "full",
        };
        t.TextChanged += (_, _) => { set(t.Text.Replace("\r\n", "\n")); s.Save(); };
        return t;
    }

    static LinkLabel Link(string text, string url)
    {
        var l = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 2, 0, 8), LinkColor = Accent };
        l.LinkClicked += (_, _) => Program.Open(url);
        return l;
    }

    static Button Btn(string text, Action onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 4, 8, 4) };
        b.Click += (_, _) => onClick();
        return b;
    }

    static FlowLayoutPanel Group(params Control[] cs)
    {
        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        p.Controls.AddRange(cs);
        return p;
    }

    // ───────── General ─────────

    TabPage General()
    {
        var page = NewPage("General");

        var sc = Card(page, "Shortcuts");
        sc.Controls.Add(Row("Push-to-talk key", Pick(Enum.GetValues<TriggerKey>(), s.Trigger, Labels.Of, v => s.Trigger = v)));
        sc.Controls.Add(Note("Hold to talk, release to insert. Double-tap for hands-free, tap again to finish. " +
                             "Add Shift while holding for Command mode (edit selected text by voice). Esc cancels."));
        sc.Controls.Add(Row("Start / stop hands-free", Pick(Labels.ToggleChoices, s.ToggleCombo, Labels.Of, v => s.ToggleCombo = v)));
        sc.Controls.Add(Row("Paste last transcript", Pick(Labels.PasteChoices, s.PasteCombo, Labels.Of, v => s.PasteCombo = v)));
        sc.Controls.Add(Row("Translate / improve text", Pick(Labels.TranslateChoices, s.TranslateCombo, Labels.Of, v => s.TranslateCombo = v)));

        var bub = Card(page, "Floating bubble");
        bub.Controls.Add(Check("Show the floating bubble (drag it anywhere · click the mic to dictate)", s.ShowDock, v => s.ShowDock = v));
        bub.Controls.Add(Check("Show the 文A translate button", s.TranslateAfterDictation, v => s.TranslateAfterDictation = v));
        bub.Controls.Add(Note("📋 next to it shows your last 5 dictations — click one to copy it."));
        bub.Controls.Add(Btn("Move the bubble back to the corner", () => { s.DockX = -1; s.DockY = -1; s.Save(); }));

        var lang = Card(page, "Microphone & language");
        var mics = new List<int> { -1 };
        var names = Recorder.Devices();
        for (int i = 0; i < names.Count; i++) mics.Add(i);
        lang.Controls.Add(Row("Microphone", Pick(mics, s.MicDevice, i => i < 0 ? "Windows default" : names[i], v => s.MicDevice = v)));
        var langs = Languages.All.ToList();
        lang.Controls.Add(Row("I speak", Pick(langs, langs.FirstOrDefault(l => l.Code == s.Language) ?? langs[0], l => l.Name, l => s.Language = l.Code)));
        lang.Controls.Add(Note("Auto-detect handles switching between languages. Pick one if short phrases come out in the wrong language."));
        var targets = new List<Lang> { new("", "Don't translate", "") };
        targets.AddRange(Languages.All.Skip(1));
        lang.Controls.Add(Row("Always translate dictation to", Pick(targets, targets.FirstOrDefault(l => l.English == s.TranslateTo) ?? targets[0], l => l.Name, l => s.TranslateTo = l.English)));

        var ins = Card(page, "Inserting text");
        ins.Controls.Add(Row("Method", Pick(Enum.GetValues<InsertMethod>(), s.Insert, Labels.Of, v => s.Insert = v)));
        ins.Controls.Add(Check("Restore my clipboard after pasting", s.RestoreClipboard, v => s.RestoreClipboard = v));
        ins.Controls.Add(Check("Add a space after each dictation", s.TrailingSpace, v => s.TrailingSpace = v));

        var fb = Card(page, "Feedback & startup");
        fb.Controls.Add(Check("Play start / stop sounds", s.Sounds, v => s.Sounds = v));
        fb.Controls.Add(Check("Mute speakers while dictating", s.MuteMedia, v => s.MuteMedia = v));
        fb.Controls.Add(Check("Show the recording pill with the waveform", s.ShowOverlay, v => s.ShowOverlay = v));
        fb.Controls.Add(Check("Ignore recordings with no speech", s.SilenceDetection, v => s.SilenceDetection = v));
        fb.Controls.Add(Check("Start FreeFlow when I sign in to Windows", Startup.Enabled, v => Startup.Enabled = v));
        return page.Tab;
    }

    // ───────── Voice ─────────

    TabPage Voice()
    {
        var page = NewPage("Voice");
        var card = Card(page, "Speech-to-text engine");

        var groq = Group(
            Row("Groq API key", Field(s.GroqKey, v => s.GroqKey = v, secret: true)),
            Row("Model", Field(s.GroqWhisperModel, v => s.GroqWhisperModel = v)),
            Link("Get a free key at console.groq.com/keys", "https://console.groq.com/keys"),
            Note("Fastest and most accurate. Audio is sent to Groq. The same key powers translation and AI Polish."));
        var local = LocalWhisperPanel();
        var custom = Group(
            Row("Base URL", Field(s.CustomBase, v => s.CustomBase = v)),
            Row("API key", Field(s.CustomKey, v => s.CustomKey = v, secret: true)),
            Row("Model", Field(s.CustomSttModel, v => s.CustomSttModel = v)),
            Note("Calls {base}/audio/transcriptions — OpenAI, a self-hosted faster-whisper server, LocalAI, etc."));

        void Sync()
        {
            groq.Visible = s.Stt == SttProvider.Groq;
            local.Visible = s.Stt == SttProvider.LocalWhisper;
            custom.Visible = s.Stt == SttProvider.Custom;
        }
        var pick = Pick(Enum.GetValues<SttProvider>(), s.Stt, Labels.Of, v => s.Stt = v);
        pick.SelectedIndexChanged += (_, _) => Sync();
        card.Controls.Add(Row("Engine", pick));
        card.Controls.Add(groq);
        card.Controls.Add(local);
        card.Controls.Add(custom);
        Sync();
        return page.Tab;
    }

    Control LocalWhisperPanel()
    {
        var status = new Label { AutoSize = true, ForeColor = Muted, Margin = new Padding(0, 8, 10, 0) };
        var start = Btn("Start engine now", async () => { try { await LocalWhisper.Shared.EnsureServer(); } catch (Exception ex) { status.Text = ex.Message; } });
        var statusRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        statusRow.Controls.Add(status);
        statusRow.Controls.Add(start);

        void UpdateStatus() => status.Text = "Status: " + LocalWhisper.Shared.Status;
        Action onStatus = () => { if (IsHandleCreated) BeginInvoke(UpdateStatus); };
        LocalWhisper.Shared.StatusChanged += onStatus;
        FormClosed += (_, _) => LocalWhisper.Shared.StatusChanged -= onStatus;
        UpdateStatus();

        var models = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        var error = Note("", Color.Firebrick);
        var dl = ModelDownloader.Shared;

        void Build()
        {
            models.SuspendLayout();
            models.Controls.Clear();
            foreach (var m in WhisperModel.All)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
                var radio = new RadioButton { Text = m.Name, Checked = s.WhisperModel == m.Id, AutoSize = false, Width = 210, Height = 28, Font = new Font("Segoe UI Semibold", 9.5f) };
                radio.CheckedChanged += (_, _) => { if (radio.Checked) { s.WhisperModel = m.Id; s.Save(); } };
                row.Controls.Add(radio);
                if (dl.Progress.TryGetValue(m.Id, out var pr))
                {
                    row.Controls.Add(new ProgressBar { Value = (int)(pr * 100), Width = 110, Height = 18, Margin = new Padding(0, 6, 6, 0) });
                    row.Controls.Add(Btn("Cancel", () => dl.Cancel(m.Id)));
                }
                else if (dl.IsDownloaded(m))
                {
                    row.Controls.Add(new Label { Text = "✓ Downloaded", ForeColor = Color.SeaGreen, AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
                    row.Controls.Add(Btn("Delete", () => dl.Delete(m)));
                }
                else row.Controls.Add(Btn("Download", () => dl.Download(m)));
                models.Controls.Add(row);
                models.Controls.Add(new Label { Text = m.Detail, AutoSize = true, ForeColor = Muted, Margin = new Padding(20, 0, 0, 6), Tag = "note" });
            }
            error.Text = dl.LastError ?? "";
            models.ResumeLayout();
        }

        Action onDl = () => { if (IsHandleCreated) BeginInvoke(Build); };
        dl.Changed += onDl;
        FormClosed += (_, _) => dl.Changed -= onDl;
        Build();

        return Group(
            statusRow,
            models,
            error,
            Note("Runs entirely on your PC — audio never leaves it, but it's slower than Groq on most laptops. " +
                 "Models live in %APPDATA%\\FreeFlow\\models."));
    }

    // ───────── AI ─────────

    TabPage Ai()
    {
        var page = NewPage("AI & Translate");

        var tr = Card(page, "Translation & improve by voice", "Used by the 文A button and ✨ Improve by voice.");
        tr.Controls.Add(Row("Groq API key", Field(s.GroqKey, v => s.GroqKey = v, secret: true)));
        if (s.QuickLangs == null || s.QuickLangs.Length < 4) s.QuickLangs = new[] { "es-CO", "en-US", "fr-FR", "" };
        var langs = new List<TLang> { null };
        langs.AddRange(TLang.All);
        for (int i = 0; i < 4; i++)
        {
            int slot = i;
            var cur = TLang.ById(s.QuickLangs[slot]);
            tr.Controls.Add(Row($"Quick language {slot + 1}", Pick(langs, cur, l => l == null ? "— none —" : $"{l.Flag}  {l.Label}",
                l => { var q = s.QuickLangs.ToArray(); q[slot] = l?.Id ?? ""; s.QuickLangs = q; })));
        }
        tr.Controls.Add(Note("These are the tiles in the 文A card; every other language is under “More languages”."));

        var pol = Card(page, "AI Polish", "Cleans up dictation: removes ums, applies self-corrections (“3pm, no actually 4pm” → 4pm), fixes punctuation, formats lists.");
        var off = Group(Note("Only simple rules are applied (filler words, capitalization, “new line”)."));
        var ollama = Group(
            Row("Server URL", Field(s.OllamaUrl, v => s.OllamaUrl = v)),
            Row("Model", Field(s.OllamaModel, v => s.OllamaModel = v)),
            Link("Install Ollama (free) from ollama.com", "https://ollama.com/download"));
        var groq = Group(
            Row("Model", Field(s.GroqLlmModel, v => s.GroqLlmModel = v)),
            Note("Uses the Groq key above. If Groq retires the model, FreeFlow switches to a current one automatically."));
        var custom = Group(
            Row("Base URL", Field(s.CustomBase, v => s.CustomBase = v)),
            Row("API key", Field(s.CustomKey, v => s.CustomKey = v, secret: true)),
            Row("Model", Field(s.CustomLlmModel, v => s.CustomLlmModel = v)));
        void Sync()
        {
            off.Visible = s.Ai == AiProvider.Off; ollama.Visible = s.Ai == AiProvider.Ollama;
            groq.Visible = s.Ai == AiProvider.Groq; custom.Visible = s.Ai == AiProvider.Custom;
        }
        var pick = Pick(Enum.GetValues<AiProvider>(), s.Ai, Labels.Of, v => s.Ai = v);
        pick.SelectedIndexChanged += (_, _) => Sync();
        pol.Controls.Add(Row("Provider", pick));
        pol.Controls.AddRange(new Control[] { off, ollama, groq, custom });
        Sync();
        pol.Controls.Add(Check("Adapt the tone to the app I'm typing in", s.ContextAware, v => s.ContextAware = v));
        pol.Controls.Add(new Label { Text = "Personal instructions", AutoSize = true, Margin = new Padding(0, 8, 0, 4) });
        pol.Controls.Add(Multi(s.CustomInstructions, v => s.CustomInstructions = v, 70));
        pol.Controls.Add(Note("e.g. “Use British spelling. Never use em dashes. Keep my French accents.”"));

        var test = Card(page, "Try it");
        const string sample = "um so I think we should uh meet tomorrow at three no actually four pm and um can you bring the the slides";
        test.Controls.Add(Note("“" + sample + "”"));
        var result = Note("");
        result.ForeColor = Color.Black;
        Button run = null;
        run = Btn("Test AI polish", async () =>
        {
            run.Enabled = false; result.Text = "Running…";
            try { result.Text = await Polisher.PolishOrThrow(sample, FocusContext.Empty); }
            catch (Exception ex) { result.Text = "⚠ " + ex.Message; }
            run.Enabled = true;
        });
        test.Controls.Add(run);
        test.Controls.Add(result);
        return page.Tab;
    }

    // ───────── Dictionary ─────────

    TabPage Words()
    {
        var page = NewPage("Dictionary");
        var d = Card(page, "Personal dictionary", "One per line. Names, jargon, brands: “Medellín”, “Focus Tribe”. Fix a word that keeps getting misheard with an arrow: “wisper -> Wispr”.");
        d.Controls.Add(Multi(s.Dictionary, v => s.Dictionary = v));
        var sn = Card(page, "Snippets", "Say a phrase, get a block of text. One per line: “my calendly link => https://calendly.com/you”. Use \\n for line breaks.");
        sn.Controls.Add(Multi(s.Snippets, v => s.Snippets = v));
        return page.Tab;
    }

    // ───────── Help ─────────

    TabPage Help()
    {
        var page = NewPage("Help");
        var how = Card(page, "How to use FreeFlow");
        how.Controls.Add(Note(
            $"• Hold {Labels.Short(s.Trigger)}, talk, release → text appears where your cursor is.\n" +
            $"• Or click the mic on the floating bubble to start, and again to finish.\n" +
            $"• Double-tap {Labels.Short(s.Trigger)} for hands-free; tap again to finish.\n" +
            $"• 文A on the bubble translates the text box · 📋 shows your last 5 dictations.\n" +
            $"• Hold {Labels.Short(s.Trigger)} + Shift for Command mode (edit selected text by voice).\n" +
            $"• {Labels.Of(s.PasteCombo)} pastes your last dictation again · Esc cancels.", Color.Black));
        var tr = Card(page, "Troubleshooting");
        tr.Controls.Add(Note("• Nothing recorded? Windows Settings → Privacy & security → Microphone → allow desktop apps."));
        tr.Controls.Add(Btn("Open microphone privacy settings", () => Program.Open("ms-settings:privacy-microphone")));
        tr.Controls.Add(Note("• Text doesn't paste into an app running as administrator: Windows blocks that unless FreeFlow is also run as administrator."));
        tr.Controls.Add(Note("• Slow? Check the timings in History. High “voice” → Groq is the fastest engine. High “AI” → turn AI Polish off."));
        tr.Controls.Add(Btn("Open FreeFlow data folder", () => Program.Open(Paths.AppData)));
        tr.Controls.Add(Note($"Version {Application.ProductVersion}"));
        return page.Tab;
    }
}

static class Startup
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Enabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue("FreeFlow") != null;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Key);
            if (value) k.SetValue("FreeFlow", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("FreeFlow", false);
        }
    }
}

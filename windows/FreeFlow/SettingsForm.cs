using Microsoft.Win32;

namespace FreeFlow;

sealed class SettingsForm : Form
{
    readonly Settings s = Settings.Current;
    const int FieldWidth = 360;

    public SettingsForm()
    {
        Text = "FreeFlow Settings";
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(760, 720);
        MinimumSize = new Size(640, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Program.AppIcon;

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
        tabs.TabPages.Add(General());
        tabs.TabPages.Add(Voice());
        tabs.TabPages.Add(Ai());
        tabs.TabPages.Add(Words());
        tabs.TabPages.Add(Help());
        Controls.Add(tabs);
    }

    // ───────── layout helpers ─────────

    static (TabPage, FlowLayoutPanel) Page(string title)
    {
        var tp = new TabPage(title) { BackColor = SystemColors.Window };
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(18, 12, 18, 18),
        };
        tp.Controls.Add(p);
        return (tp, p);
    }

    static Label Header(string t) => new()
    {
        Text = t, AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Margin = new Padding(0, 14, 0, 4),
    };

    static Label Note(string t, Color? c = null) => new()
    {
        Text = t, AutoSize = true, MaximumSize = new Size(640, 0), ForeColor = c ?? Color.DimGray, Margin = new Padding(0, 2, 0, 6),
    };

    static Control Row(string label, Control c)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(0, 3, 0, 3) };
        row.Controls.Add(new Label { Text = label, Width = 200, Height = 28, TextAlign = ContentAlignment.MiddleLeft });
        row.Controls.Add(c);
        return row;
    }

    ComboBox Pick<T>(IList<T> values, T current, Func<T, string> label, Action<T> set)
    {
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = FieldWidth };
        foreach (var v in values) cb.Items.Add(label(v));
        cb.SelectedIndex = Math.Max(0, values.IndexOf(current));
        cb.SelectedIndexChanged += (_, _) => { set(values[cb.SelectedIndex]); s.Save(); };
        return cb;
    }

    CheckBox Check(string text, bool value, Action<bool> set)
    {
        var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
        c.CheckedChanged += (_, _) => { set(c.Checked); s.Save(); };
        return c;
    }

    TextBox Field(string value, Action<string> set, bool secret = false, int width = FieldWidth)
    {
        var t = new TextBox { Text = value ?? "", Width = width, UseSystemPasswordChar = secret };
        t.TextChanged += (_, _) => { set(t.Text.Trim()); s.Save(); };
        return t;
    }

    TextBox Multi(string value, Action<string> set, int height = 140)
    {
        var t = new TextBox
        {
            Text = (value ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"), Multiline = true, ScrollBars = ScrollBars.Vertical,
            Width = 640, Height = height, AcceptsReturn = true, Font = new Font("Consolas", 10f),
        };
        t.TextChanged += (_, _) => { set(t.Text.Replace("\r\n", "\n")); s.Save(); };
        return t;
    }

    static LinkLabel Link(string text, string url)
    {
        var l = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 2, 0, 6) };
        l.LinkClicked += (_, _) => Program.Open(url);
        return l;
    }

    static Control Panel(params Control[] cs)
    {
        var p = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        p.Controls.AddRange(cs);
        return p;
    }

    // ───────── tabs ─────────

    TabPage General()
    {
        var (tp, p) = Page("General");
        p.Controls.Add(Header("Shortcuts"));
        var triggers = Enum.GetValues<TriggerKey>();
        p.Controls.Add(Row("Push-to-talk key", Pick(triggers, s.Trigger, Labels.Of, v => s.Trigger = v)));
        p.Controls.Add(Note("Hold to talk, release to insert. Quick double-tap for hands-free, tap again to finish. " +
                            "Add Shift while holding for Command mode: select text and say “make this friendlier”, or ask for anything with nothing selected. Esc cancels."));
        p.Controls.Add(Row("Start / stop hands-free", Pick(Labels.ToggleChoices, s.ToggleCombo, Labels.Of, v => s.ToggleCombo = v)));
        p.Controls.Add(Row("Paste last transcript", Pick(Labels.PasteChoices, s.PasteCombo, Labels.Of, v => s.PasteCombo = v)));
        p.Controls.Add(Row("Translate / improve text", Pick(Labels.TranslateChoices, s.TranslateCombo, Labels.Of, v => s.TranslateCombo = v)));
        p.Controls.Add(Check("Show the 文A Translate button for 5 s after each dictation", s.TranslateAfterDictation, v => s.TranslateAfterDictation = v));

        p.Controls.Add(Header("Microphone & language"));
        var mics = new List<int> { -1 };
        var names = Recorder.Devices();
        for (int i = 0; i < names.Count; i++) mics.Add(i);
        p.Controls.Add(Row("Microphone", Pick(mics, s.MicDevice, i => i < 0 ? "Windows default" : names[i], v => s.MicDevice = v)));
        var langs = Languages.All.ToList();
        p.Controls.Add(Row("I speak", Pick(langs, langs.FirstOrDefault(l => l.Code == s.Language) ?? langs[0], l => l.Name, l => s.Language = l.Code)));
        p.Controls.Add(Note("Auto-detect handles switching between languages. Pick one if short phrases come out in the wrong language."));
        var targets = new List<Lang> { new("", "Don't translate", "") };
        targets.AddRange(Languages.All.Skip(1));
        p.Controls.Add(Row("Translate output to", Pick(targets, targets.FirstOrDefault(l => l.English == s.TranslateTo) ?? targets[0], l => l.Name, l => s.TranslateTo = l.English)));
        p.Controls.Add(Note("Translation uses the AI Polish provider."));

        p.Controls.Add(Header("Inserting text"));
        var methods = Enum.GetValues<InsertMethod>();
        p.Controls.Add(Row("Method", Pick(methods, s.Insert, Labels.Of, v => s.Insert = v)));
        p.Controls.Add(Check("Restore my clipboard after pasting", s.RestoreClipboard, v => s.RestoreClipboard = v));
        p.Controls.Add(Check("Add a space after each dictation", s.TrailingSpace, v => s.TrailingSpace = v));

        p.Controls.Add(Header("Feedback"));
        p.Controls.Add(Check("Play start / stop sounds", s.Sounds, v => s.Sounds = v));
        p.Controls.Add(Check("Mute speakers while dictating", s.MuteMedia, v => s.MuteMedia = v));
        p.Controls.Add(Check("Show the floating recording pill", s.ShowOverlay, v => s.ShowOverlay = v));
        p.Controls.Add(Check("Ignore recordings with no speech", s.SilenceDetection, v => s.SilenceDetection = v));

        p.Controls.Add(Header("Startup"));
        p.Controls.Add(Check("Start FreeFlow when I sign in to Windows", Startup.Enabled, v => Startup.Enabled = v));
        return tp;
    }

    TabPage Voice()
    {
        var (tp, p) = Page("Voice");
        p.Controls.Add(Header("Speech-to-text engine"));
        var provs = Enum.GetValues<SttProvider>();

        var local = LocalWhisperPanel();
        var groq = Panel(
            Header("Groq (free tier)"),
            Row("API key", Field(s.GroqKey, v => s.GroqKey = v, secret: true)),
            Row("Model", Field(s.GroqWhisperModel, v => s.GroqWhisperModel = v)),
            Link("Get a free key at console.groq.com/keys", "https://console.groq.com/keys"),
            Note("Audio is sent to Groq. The same key powers Groq AI Polish."));
        var custom = Panel(
            Header("OpenAI-compatible API"),
            Row("Base URL", Field(s.CustomBase, v => s.CustomBase = v)),
            Row("API key", Field(s.CustomKey, v => s.CustomKey = v, secret: true)),
            Row("Model", Field(s.CustomSttModel, v => s.CustomSttModel = v)),
            Note("Calls {base}/audio/transcriptions — OpenAI, a self-hosted faster-whisper server, LocalAI, etc."));

        void Sync() { local.Visible = s.Stt == SttProvider.LocalWhisper; groq.Visible = s.Stt == SttProvider.Groq; custom.Visible = s.Stt == SttProvider.Custom; }
        var pick = Pick(provs, s.Stt, Labels.Of, v => s.Stt = v);
        pick.SelectedIndexChanged += (_, _) => Sync();
        p.Controls.Add(Row("Engine", pick));
        p.Controls.Add(local);
        p.Controls.Add(groq);
        p.Controls.Add(custom);
        Sync();
        return tp;
    }

    Control LocalWhisperPanel()
    {
        var status = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 10, 0) };
        var start = new Button { Text = "Start engine now", AutoSize = true };
        start.Click += async (_, _) => { try { await LocalWhisper.Shared.EnsureServer(); } catch (Exception ex) { status.Text = ex.Message; } };
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
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 4) };
                var radio = new RadioButton { Text = m.Name, Checked = s.WhisperModel == m.Id, AutoSize = true, Width = 230, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
                radio.CheckedChanged += (_, _) => { if (radio.Checked) { s.WhisperModel = m.Id; s.Save(); } };
                var info = new Label { Text = m.Detail, AutoSize = false, Width = 300, Height = 30, ForeColor = Color.DimGray, TextAlign = ContentAlignment.MiddleLeft };
                row.Controls.Add(radio);
                row.Controls.Add(info);
                if (dl.Progress.TryGetValue(m.Id, out var pr))
                {
                    row.Controls.Add(new ProgressBar { Value = (int)(pr * 100), Width = 90, Height = 18, Margin = new Padding(0, 6, 6, 0) });
                    var cancel = new Button { Text = "Cancel", AutoSize = true };
                    cancel.Click += (_, _) => dl.Cancel(m.Id);
                    row.Controls.Add(cancel);
                }
                else if (dl.IsDownloaded(m))
                {
                    row.Controls.Add(new Label { Text = "✓ Downloaded", ForeColor = Color.SeaGreen, AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
                    var del = new Button { Text = "Delete", AutoSize = true };
                    del.Click += (_, _) => dl.Delete(m);
                    row.Controls.Add(del);
                }
                else
                {
                    var get = new Button { Text = "Download", AutoSize = true };
                    get.Click += (_, _) => dl.Download(m);
                    row.Controls.Add(get);
                }
                models.Controls.Add(row);
            }
            error.Text = dl.LastError ?? "";
            models.ResumeLayout();
        }

        Action onDl = () => { if (IsHandleCreated) BeginInvoke(Build); };
        dl.Changed += onDl;
        FormClosed += (_, _) => dl.Changed -= onDl;
        Build();

        return Panel(
            Header("Local Whisper (whisper.cpp, bundled)"),
            statusRow,
            Header("Model"),
            models,
            error,
            Note("Runs entirely on your PC — audio never leaves it. Models live in %APPDATA%\\FreeFlow\\models. " +
                 "On older laptops pick Small, or use Groq for near-instant results."));
    }

    TabPage Ai()
    {
        var (tp, p) = Page("AI Polish");
        p.Controls.Add(Header("AI that cleans up what you said"));
        p.Controls.Add(Note("Removes ums and repetitions, applies self-corrections (“3pm, no actually 4pm” → “4pm”), fixes punctuation, formats lists, " +
                            "follows “new line / new paragraph”, and adapts tone to the app you're in. Also powers Command mode and translation."));

        var off = Panel(Note("Only simple rules are applied (filler words, capitalization, “new line”). Command mode and translation are disabled."));
        var ollama = Panel(
            Header("Ollama"),
            Row("Server URL", Field(s.OllamaUrl, v => s.OllamaUrl = v)),
            Row("Model", Field(s.OllamaModel, v => s.OllamaModel = v)),
            Link("Install Ollama (free) from ollama.com", "https://ollama.com/download"),
            Note("Then in a terminal: ollama pull llama3.2 — small models (llama3.2, qwen2.5:3b, gemma3:4b) are fast enough for real-time cleanup."));
        var groq = Panel(
            Header("Groq (free tier)"),
            Row("API key", Field(s.GroqKey, v => s.GroqKey = v, secret: true)),
            Row("Model", Field(s.GroqLlmModel, v => s.GroqLlmModel = v)),
            Link("Get a free key at console.groq.com/keys", "https://console.groq.com/keys"),
            Note("Very fast. Text is sent to Groq. Try llama-3.1-8b-instant for even lower latency."));
        var custom = Panel(
            Header("OpenAI-compatible API"),
            Row("Base URL", Field(s.CustomBase, v => s.CustomBase = v)),
            Row("API key", Field(s.CustomKey, v => s.CustomKey = v, secret: true)),
            Row("Model", Field(s.CustomLlmModel, v => s.CustomLlmModel = v)),
            Note("Works with OpenAI, xAI Grok (https://api.x.ai/v1), OpenRouter, LM Studio, etc."));

        void Sync()
        {
            off.Visible = s.Ai == AiProvider.Off; ollama.Visible = s.Ai == AiProvider.Ollama;
            groq.Visible = s.Ai == AiProvider.Groq; custom.Visible = s.Ai == AiProvider.Custom;
        }
        var pick = Pick(Enum.GetValues<AiProvider>(), s.Ai, Labels.Of, v => s.Ai = v);
        pick.SelectedIndexChanged += (_, _) => Sync();
        p.Controls.Add(Row("Provider", pick));
        p.Controls.AddRange(new[] { off, ollama, groq, custom });
        Sync();

        p.Controls.Add(Header("Translation & improve by voice"));
        p.Controls.Add(Row("Groq API key", Field(s.GroqKey, v => s.GroqKey = v, secret: true)));
        var langs = new List<TLang> { null };
        langs.AddRange(TLang.All);
        for (int i = 0; i < 4; i++)
        {
            int slot = i;
            if (s.QuickLangs == null || s.QuickLangs.Length < 4) s.QuickLangs = new[] { "es-CO", "en-US", "fr-FR", "" };
            var cur = TLang.ById(s.QuickLangs[slot]);
            p.Controls.Add(Row($"Quick language {slot + 1}", Pick(langs, cur, l => l == null ? "— none —" : $"{l.Flag}  {l.Label}",
                l => { var q = s.QuickLangs.ToArray(); q[slot] = l?.Id ?? ""; s.QuickLangs = q; })));
        }
        p.Controls.Add(Note("These are the tiles in the 文A card; every other language is under “More languages”. " +
                            "Translation and ✨ Improve by voice use this Groq key — the same one as Voice and AI Polish."));

        p.Controls.Add(Header("Style"));
        p.Controls.Add(Check("Adapt to the app I'm typing in", s.ContextAware, v => s.ContextAware = v));
        p.Controls.Add(Note("Casual in Slack / WhatsApp / Discord, proper paragraphs in Outlook / Gmail, literal in code editors and terminals."));
        p.Controls.Add(new Label { Text = "Personal instructions", AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
        p.Controls.Add(Multi(s.CustomInstructions, v => s.CustomInstructions = v, 70));
        p.Controls.Add(Note("e.g. “Use British spelling. Never use em dashes. Keep my French accents.”"));

        p.Controls.Add(Header("Try it"));
        const string sample = "um so I think we should uh meet tomorrow at three no actually four pm and um can you bring the the slides";
        p.Controls.Add(Note("“" + sample + "”"));
        var result = new Label { AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 6, 0, 0) };
        var test = new Button { Text = "Test AI polish", AutoSize = true };
        test.Click += async (_, _) =>
        {
            test.Enabled = false; result.Text = "Running…";
            try { result.Text = await Polisher.PolishOrThrow(sample, FocusContext.Empty); }
            catch (Exception ex) { result.Text = "⚠ " + ex.Message; }
            test.Enabled = true;
        };
        p.Controls.Add(test);
        p.Controls.Add(result);
        return tp;
    }

    TabPage Words()
    {
        var (tp, p) = Page("Dictionary");
        p.Controls.Add(Header("Personal dictionary"));
        p.Controls.Add(Multi(s.Dictionary, v => s.Dictionary = v));
        p.Controls.Add(Note("One per line. Names, jargon, brands: “Medellín”, “Focus Tribe”. Fix a word that keeps getting misheard with an arrow: “wisper -> Wispr”."));
        p.Controls.Add(Header("Snippets"));
        p.Controls.Add(Multi(s.Snippets, v => s.Snippets = v));
        p.Controls.Add(Note("Say a phrase, get a block of text. One per line: “my calendly link => https://calendly.com/you”. Use \\n for line breaks."));
        return tp;
    }

    TabPage Help()
    {
        var (tp, p) = Page("Help");
        p.Controls.Add(Header("How to use FreeFlow"));
        p.Controls.Add(Note(
            $"• Hold {Labels.Short(s.Trigger)}, talk, release → text appears where your cursor is.\n" +
            $"• Double-tap {Labels.Short(s.Trigger)} for hands-free; tap again to finish.\n" +
            $"• {Labels.Of(s.ToggleCombo)} also starts/stops hands-free from anywhere.\n" +
            $"• Hold {Labels.Short(s.Trigger)} + Shift for Command mode (edit selected text by voice, or ask for anything).\n" +
            $"• {Labels.Of(s.PasteCombo)} pastes your last dictation again.\n" +
            "• Esc cancels.", Color.Black));
        p.Controls.Add(Header("Troubleshooting"));
        p.Controls.Add(Note("• Nothing recorded? Windows Settings → Privacy & security → Microphone → allow desktop apps."));
        var mic = new Button { Text = "Open microphone privacy settings", AutoSize = true };
        mic.Click += (_, _) => Program.Open("ms-settings:privacy-microphone");
        p.Controls.Add(mic);
        p.Controls.Add(Note("• Text doesn't paste into an app running as administrator: Windows blocks that unless FreeFlow is also run as administrator."));
        p.Controls.Add(Note("• Slow? Check the timings in History. High “voice” → pick the Small model or Groq. High “AI” → try Groq or turn AI Polish off."));
        var folder = new Button { Text = "Open FreeFlow data folder", AutoSize = true };
        folder.Click += (_, _) => Program.Open(Paths.AppData);
        p.Controls.Add(folder);
        p.Controls.Add(Note($"Version {Application.ProductVersion}"));
        return tp;
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

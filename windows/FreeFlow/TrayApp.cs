namespace FreeFlow;

enum RecState { Idle, PushToTalk, PendingTap, HandsFree, Processing }

/// The app: tray icon + the hold/tap/double-tap state machine + the dictation pipeline.
sealed class TrayApp : ApplicationContext
{
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem statusItem;
    readonly KeyboardHook hook = new();
    readonly HotkeyWindow hotkeys = new();
    readonly Recorder recorder = new();
    readonly Overlay overlay = new();
    readonly MediaMuter muter = new();
    readonly TranslateBar translateBar = new();
    readonly DockBar dock = new();
    string preSelection;
    Task<CaretContext.Around> caretTask;
    readonly System.Windows.Forms.Timer tapTimer = new() { Interval = 350 };
    readonly System.Windows.Forms.Timer announceTimer = new() { Interval = 150 };
    readonly System.Windows.Forms.Timer maxTimer = new() { Interval = 600_000 };

    RecState state = RecState.Idle;
    bool commandMode, ignoreNextUp, announced;
    DateTime downTime;
    string wavPath;
    FocusContext context = FocusContext.Empty;
    CancellationTokenSource cts;
    string lastText;
    SettingsForm settingsForm;
    HistoryForm historyForm;

    public TrayApp()
    {
        var menu = new ContextMenuStrip();
        statusItem = new ToolStripMenuItem { Enabled = false };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Start / stop dictation", null, (_, _) => ToggleHandsFree());
        menu.Items.Add("Paste last transcript", null, (_, _) => PasteLast());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Show / hide floating bubble", null, (_, _) =>
        {
            Settings.Current.ShowDock = !Settings.Current.ShowDock;
            Settings.Current.Save();
            dock.ShowDock();
        });
        menu.Items.Add("History…", null, (_, _) => OpenHistory());
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit FreeFlow", null, (_, _) => Quit());
        menu.Opening += (_, _) =>
            statusItem.Text = $"Hold {Labels.Short(Settings.Current.Trigger)} to dictate · {HistoryStore.Shared.TotalWords:N0} words so far";

        tray = new NotifyIcon
        {
            Icon = Program.AppIcon,
            Text = "FreeFlow — voice dictation",
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.DoubleClick += (_, _) => OpenSettings();

        hook.TriggerDown = TriggerDown;
        hook.TriggerUp = TriggerUp;
        hook.CommandModifier = () => { if (state is RecState.PushToTalk or RecState.HandsFree && !commandMode) { commandMode = true; if (announced) overlay.Set(Phase.Command); } };
        hook.OtherKey = () => { if (state == RecState.PushToTalk) CancelRecording(silently: true); };
        hook.Escape = Cancel;
        hook.Install();
        hotkeys.Toggle = ToggleHandsFree;
        hotkeys.PasteLast = PasteLast;
        hotkeys.Quit = Quit;
        Application.ApplicationExit += (_, _) => { try { tray.Visible = false; } catch { } };
        hotkeys.Translate = () => translateBar.ShowMenu();
        translateBar.OnTranslate = RunTranslate;
        translateBar.OnImprove = ImproveByVoice;
        translateBar.OnCopied = _ => Feedback.Stop();

        overlay.Dock = dock;
        dock.Mic = ToggleHandsFree;
        dock.Cancel = Cancel;
        dock.Translate = () => translateBar.ShowMenuNear(() => dock.CapsuleBounds);
        dock.Recent = () => translateBar.ShowRecentNear(() => dock.CapsuleBounds);
        dock.CardOpen = () => translateBar.CardOpen;
        dock.ShowDock();
        Settings.Changed += () => { if (Settings.Current.ShowDock) dock.ShowDock(); else dock.Hide(); };

        recorder.Level = overlay.PushLevel;
        tapTimer.Tick += (_, _) => { tapTimer.Stop(); if (state == RecState.PendingTap) CancelRecording(silently: true); };
        announceTimer.Tick += (_, _) => { announceTimer.Stop(); if (state is RecState.PushToTalk or RecState.HandsFree) Announce(); };
        maxTimer.Tick += (_, _) => { maxTimer.Stop(); StopAndProcess(); };

        if (Settings.Current.Stt == SttProvider.LocalWhisper)
            _ = LocalWhisper.Shared.EnsureServer().ContinueWith(_ => { });   // warm up

        if (Settings.IsFirstRun)
        {
            Settings.Current.Save();
            var m = WhisperModel.All.FirstOrDefault(x => x.Id == Settings.Current.WhisperModel);
            if (m != null && !ModelDownloader.Shared.IsDownloaded(m)) ModelDownloader.Shared.Download(m);
            OpenSettings();
            tray.ShowBalloonTip(6000, "FreeFlow is running",
                $"Downloading the speech model… When it's done, hold {Labels.Short(Settings.Current.Trigger)} anywhere and talk.", ToolTipIcon.Info);
        }
    }

    // ───────── key state machine ─────────

    void TriggerDown(bool shift)
    {
        switch (state)
        {
            case RecState.HandsFree:
                ignoreNextUp = true;
                StopAndProcess();
                break;
            case RecState.PendingTap:   // second tap → hands-free
                tapTimer.Stop();
                state = RecState.HandsFree;
                ignoreNextUp = true;
                Announce();
                overlay.Set(commandMode ? Phase.Command : Phase.HandsFree);
                break;
            case RecState.Idle:
                commandMode = shift;
                downTime = DateTime.Now;
                StartRecording(RecState.PushToTalk);
                break;
        }
    }

    void TriggerUp()
    {
        if (ignoreNextUp) { ignoreNextUp = false; return; }
        if (state != RecState.PushToTalk) return;
        if ((DateTime.Now - downTime).TotalMilliseconds < 300)
        {
            state = RecState.PendingTap;
            tapTimer.Start();
        }
        else StopAndProcess();
    }

    void ToggleHandsFree()
    {
        switch (state)
        {
            case RecState.Idle:
                commandMode = false;
                if (StartRecording(RecState.HandsFree)) Announce();
                break;
            case RecState.HandsFree:
            case RecState.PushToTalk:
            case RecState.PendingTap:
                StopAndProcess();
                break;
        }
    }

    void Cancel()
    {
        if (translateBar.Visible) { translateBar.HideBar(); return; }
        switch (state)
        {
            case RecState.PushToTalk:
            case RecState.PendingTap:
            case RecState.HandsFree:
                CancelRecording(silently: false);
                break;
            case RecState.Processing:
                cts?.Cancel();
                FinishIdle();
                overlay.Flash("Cancelled");
                break;
        }
    }

    // ───────── recording ─────────

    bool StartRecording(RecState newState)
    {
        context = FocusContext.Current();
        announced = false;
        try { wavPath = recorder.Start(); }
        catch (Exception ex)
        {
            overlay.Flash("Microphone error: " + ex.Message);
            Feedback.Error();
            return false;
        }
        state = newState;
        caretTask = CaretContext.CaptureAsync();   // what's around the cursor (mid-sentence?)
        dock.SetState(DockBar.DockState.Recording);
        Llm.WarmUp();
        announceTimer.Start();   // skip sounds/UI for accidental taps or Ctrl+Win shortcuts
        maxTimer.Start();
        return true;
    }

    void Announce()
    {
        if (announced) return;
        announced = true;
        Feedback.Start();
        if (Settings.Current.MuteMedia) Task.Delay(200).ContinueWith(_ => muter.Mute());
        overlay.ShowPhase(commandMode ? Phase.Command : state == RecState.HandsFree ? Phase.HandsFree : Phase.Listening);
    }

    void CancelRecording(bool silently)
    {
        tapTimer.Stop(); announceTimer.Stop(); maxTimer.Stop();
        recorder.Stop();
        TryDelete(wavPath);
        wavPath = null;
        if (announced && Settings.Current.MuteMedia) muter.Restore();
        ignoreNextUp = false;
        FinishIdle();
        if (silently) overlay.HidePill(); else overlay.Flash("Cancelled");
    }

    void FinishIdle()
    {
        state = RecState.Idle;
        announced = false;
        dock.SetState(DockBar.DockState.Idle);
    }

    async void StopAndProcess()
    {
        tapTimer.Stop(); announceTimer.Stop(); maxTimer.Stop();
        if (wavPath == null) { FinishIdle(); return; }
        var path = wavPath;
        wavPath = null;
        var (duration, peak) = recorder.Stop();
        if (Settings.Current.MuteMedia) muter.Restore();
        if (Settings.Current.MuteMedia) _ = Task.Delay(150).ContinueWith(_ => Feedback.Stop()); else Feedback.Stop();

        if (duration < 0.4 || (Settings.Current.SilenceDetection && peak < 0.18))
        {
            TryDelete(path);
            FinishIdle();
            overlay.Flash("No speech detected");
            return;
        }

        state = RecState.Processing;
        dock.SetState(DockBar.DockState.Busy);
        overlay.Set(Phase.Transcribing);
        var mode = commandMode;
        var ctx = context;
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        // Command mode: grab the selection now — all keys are released, so Ctrl+C is clean.
        string selection = mode ? (preSelection ?? Inserter.CopySelection()) : null;
        preSelection = null;

        var t0 = DateTime.Now;
        try
        {
            var raw = Cleanup.StripArtifacts(await Transcriber.Transcribe(path, ct));
            var stt = (DateTime.Now - t0).TotalSeconds;
            ct.ThrowIfCancellationRequested();
            if (raw.Length == 0) { FinishIdle(); overlay.Flash("No speech detected"); return; }

            CaretContext.Around around = null;
            if (!mode && caretTask != null)
            {
                var done = await Task.WhenAny(caretTask, Task.Delay(300));
                if (done == caretTask) around = caretTask.Result;
            }
            if (CaretContext.MidSentence(around)) ctx.ContinuesAfter = around.Before;

            var t1 = DateTime.Now;
            bool usedAi = false;
            string output;
            if (mode)
            {
                usedAi = true;
                overlay.Set(Phase.Thinking);
                output = await Polisher.Command(raw, selection, ctx, ct);
            }
            else if (Snippets.ExactMatch(raw) is { } snip) output = snip;
            else if (Polisher.NeedsAi(raw))
            {
                usedAi = true;
                overlay.Set(Phase.Polishing);
                output = Snippets.ExpandInline(await Polisher.Polish(raw, ctx, ct));
            }
            else output = Snippets.ExpandInline(Cleanup.Basic(raw));
            output = Vocabulary.Apply(output);
            if (!mode) output = CaretContext.Fit(output, around);
            ct.ThrowIfCancellationRequested();
            var ai = usedAi ? (DateTime.Now - t1).TotalSeconds : 0;

            Deliver(output, raw, duration, mode, ctx, stt, ai);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            FinishIdle();
            Feedback.Error();
            overlay.Flash(ex is FFException ? ex.Message : "Error: " + ex.Message);
            Program.Log(ex);
        }
        finally { TryDelete(path); }
    }

    void Deliver(string text, string raw, double duration, bool command, FocusContext ctx, double stt, double ai)
    {
        var t = text;
        if (Settings.Current.TrailingSpace && !command && ctx.Category != AppCategory.Terminal && t.Length > 0 && !char.IsWhiteSpace(t[^1]))
            t += " ";
        var (ok, why) = Inserter.Insert(t);
        lastText = t;
        HistoryStore.Shared.Add(new HistoryEntry
        {
            Date = DateTime.Now, Text = text, Raw = raw, App = ctx.AppName, Mode = command ? "command" : "dictation",
            Seconds = duration, SttSeconds = stt, AiSeconds = ai,
        });
        FinishIdle();
        if (ok)
        {
            overlay.Set(Phase.Done, $"{stt + ai:0.0}s");
        }
        else { Feedback.Error(); overlay.Flash(why); }
    }

    // ───────── translate & improve by voice ─────────

    async void RunTranslate(TLang lang)
    {
        if (state != RecState.Idle) return;
        var text = Inserter.SelectionOrWholeField();
        if (string.IsNullOrWhiteSpace(text)) { overlay.Flash("Nothing to translate — click into a text box first"); return; }
        state = RecState.Processing;
        dock.SetState(DockBar.DockState.Busy);
        overlay.ShowPhase(Phase.Translating);
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        var t0 = DateTime.Now;
        try
        {
            var outp = await Llm.Translate(text, lang, ct);
            ct.ThrowIfCancellationRequested();
            FinishIdle();
            var (ok, why) = Inserter.Insert(outp);
            lastText = outp;
            if (ok) overlay.Set(Phase.Done, $"{lang.Short} · {(DateTime.Now - t0).TotalSeconds:0.0}s");
            else overlay.Flash(why);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            FinishIdle();
            Feedback.Error();
            overlay.Flash(ex is FFException ? ex.Message : "Translation failed: " + ex.Message);
            Program.Log(ex);
        }
    }

    /// Hands-free Command mode on the selection — or the whole text box if nothing is selected.
    void ImproveByVoice()
    {
        if (state != RecState.Idle) return;
        preSelection = Inserter.CopySelection();
        if (string.IsNullOrWhiteSpace(preSelection))
        {
            Inserter.SelectAll();
            preSelection = Inserter.CopySelection();
        }
        commandMode = true;
        if (StartRecording(RecState.HandsFree))
        {
            Announce();
            overlay.Flash($"Say how to improve it, then tap {Labels.Short(Settings.Current.Trigger)} to finish");
            var t = new System.Windows.Forms.Timer { Interval = 2600 };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); if (state == RecState.HandsFree) overlay.ShowPhase(Phase.Command); };
            t.Start();
        }
        else preSelection = null;
    }

    void PasteLast()
    {
        var t = lastText ?? HistoryStore.Shared.Entries.FirstOrDefault()?.Text;
        if (t == null) { overlay.Flash("Nothing dictated yet"); return; }
        var (ok, why) = Inserter.Insert(t);
        if (!ok) overlay.Flash(why);
    }

    static void TryDelete(string p) { try { if (p != null && File.Exists(p)) File.Delete(p); } catch { } }

    // ───────── windows ─────────

    void OpenSettings()
    {
        if (settingsForm == null || settingsForm.IsDisposed) settingsForm = new SettingsForm();
        settingsForm.Show();
        settingsForm.WindowState = FormWindowState.Normal;
        settingsForm.Activate();
    }

    void OpenHistory()
    {
        if (historyForm == null || historyForm.IsDisposed)
            historyForm = new HistoryForm(text =>
            {
                // give focus a moment to return to the previous app
                var t = new System.Windows.Forms.Timer { Interval = 350 };
                t.Tick += (_, _) => { t.Stop(); t.Dispose(); var (ok, why) = Inserter.Insert(text); if (!ok) overlay.Flash(why); };
                t.Start();
            });
        historyForm.Show();
        historyForm.Activate();
    }

    bool quitting;

    void Quit()
    {
        if (quitting) return;
        quitting = true;
        tray.Visible = false;
        hook.Dispose();
        hotkeys.Dispose();
        LocalWhisper.Shared.Stop();
        muter.Restore();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { tray.Dispose(); hook.Dispose(); LocalWhisper.Shared.Stop(); }
        base.Dispose(disposing);
    }
}

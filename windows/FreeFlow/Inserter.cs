using System.Media;
using NAudio.CoreAudioApi;

namespace FreeFlow;

/// Puts text into whatever app has focus: clipboard + Ctrl+V (clipboard restored after), or simulated typing.
static class Inserter
{
    public static (bool inserted, string why) Insert(string text)
    {
        if (Native.OwnWindowIsForeground())
        {
            SetClipboard(text, transient: false);
            return (false, "Copied — click into another app first");
        }
        WaitForModifiersReleased();
        if (Settings.Current.Insert == InsertMethod.Type) { TypeText(text); return (true, null); }
        Paste(text);
        return (true, null);
    }

    static void WaitForModifiersReleased()
    {
        // If a hotkey like Ctrl+Alt+Space is still held, Ctrl+V would become Ctrl+Alt+V.
        int[] mods = { 0x10, 0x11, 0x12, Native.VK_LWIN, Native.VK_RWIN };
        var until = DateTime.UtcNow.AddMilliseconds(1200);
        while (mods.Any(Native.IsDown) && DateTime.UtcNow < until) Thread.Sleep(15);
    }

    static void Paste(string text)
    {
        var saved = Settings.Current.RestoreClipboard ? Snapshot() : null;
        if (!SetClipboard(text, transient: true)) return;
        uint seq = Native.GetClipboardSequenceNumber();
        Native.Send(Native.Key(Native.VK_CONTROL, false), Native.Key(Native.VK_V, false),
                    Native.Key(Native.VK_V, true), Native.Key(Native.VK_CONTROL, true));
        if (saved == null) return;
        var t = new System.Windows.Forms.Timer { Interval = 700 };
        t.Tick += (_, _) =>
        {
            t.Stop(); t.Dispose();
            if (Native.GetClipboardSequenceNumber() == seq) Restore(saved);   // user didn't copy something else meanwhile
        };
        t.Start();
    }

    static void TypeText(string text)
    {
        var inputs = new List<Native.INPUT>();
        foreach (var c in text)
        {
            if (c == '\r') continue;
            if (c == '\n') { inputs.Add(Native.Key(Native.VK_RETURN, false)); inputs.Add(Native.Key(Native.VK_RETURN, true)); }
            else { inputs.Add(Native.Unicode(c, false)); inputs.Add(Native.Unicode(c, true)); }
        }
        var arr = inputs.ToArray();
        Task.Run(() =>
        {
            for (int i = 0; i < arr.Length; i += 40)
            {
                Native.Send(arr.Skip(i).Take(40).ToArray());
                Thread.Sleep(6);
            }
        });
    }

    /// Command mode: copy the current selection (Ctrl+C) and give the clipboard back.
    public static string CopySelection()
    {
        WaitForModifiersReleased();
        var saved = Snapshot();
        uint seq = Native.GetClipboardSequenceNumber();
        Native.Send(Native.Key(Native.VK_CONTROL, false), Native.Key(Native.VK_C, false),
                    Native.Key(Native.VK_C, true), Native.Key(Native.VK_CONTROL, true));
        var until = DateTime.UtcNow.AddMilliseconds(350);
        while (Native.GetClipboardSequenceNumber() == seq && DateTime.UtcNow < until) Thread.Sleep(15);
        if (Native.GetClipboardSequenceNumber() == seq) return null;
        string text = null;
        try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); } catch { }
        Restore(saved);
        return text;
    }

    /// The selection if there is one, otherwise the whole text box (Ctrl+A stays selected, so pasting replaces it).
    public static string SelectionOrWholeField()
    {
        var s = CopySelection();
        if (!string.IsNullOrWhiteSpace(s)) return s;
        Native.Send(Native.Key(Native.VK_CONTROL, false), Native.Key(0x41, false), Native.Key(0x41, true), Native.Key(Native.VK_CONTROL, true));
        Thread.Sleep(60);
        return CopySelection();
    }

    public static void SelectAll()
    {
        WaitForModifiersReleased();
        Native.Send(Native.Key(Native.VK_CONTROL, false), Native.Key(0x41, false), Native.Key(0x41, true), Native.Key(Native.VK_CONTROL, true));
        Thread.Sleep(60);
    }

    public static bool SetClipboard(string text, bool transient)
    {
        try
        {
            var d = new DataObject();
            d.SetData(DataFormats.UnicodeText, text);
            if (transient)
            {
                // Keep dictation out of Win+V clipboard history and cloud sync.
                d.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
                d.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
                d.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
            }
            Clipboard.SetDataObject(d, true, 10, 50);
            return true;
        }
        catch { return false; }
    }

    static DataObject Snapshot()
    {
        try
        {
            var src = Clipboard.GetDataObject();
            if (src == null) return null;
            var copy = new DataObject();
            foreach (var f in src.GetFormats(false))
            {
                try { var v = src.GetData(f, false); if (v != null) copy.SetData(f, false, v); } catch { }
            }
            return copy;
        }
        catch { return null; }
    }

    static void Restore(DataObject saved)
    {
        try
        {
            if (saved == null || saved.GetFormats().Length == 0) Clipboard.Clear();
            else Clipboard.SetDataObject(saved, true, 10, 50);
        }
        catch { }
    }
}

static class Feedback
{
    static readonly SoundPlayer StartP = new(Tone(880, 1175, 70));
    static readonly SoundPlayer StopP = new(Tone(1175, 784, 70));
    static readonly SoundPlayer ErrorP = new(Tone(330, 220, 160));

    public static void Start() { if (Settings.Current.Sounds) Try(StartP); }
    public static void Stop() { if (Settings.Current.Sounds) Try(StopP); }
    public static void Error() { if (Settings.Current.Sounds) Try(ErrorP); }
    static void Try(SoundPlayer p) { try { p.Stream.Position = 0; p.Play(); } catch { } }

    /// A short sine sweep as an in-memory WAV.
    static MemoryStream Tone(double f0, double f1, int ms)
    {
        const int rate = 22050;
        int n = rate * ms / 1000;
        var ms2 = new MemoryStream();
        var w = new BinaryWriter(ms2);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + n * 2); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(n * 2);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double f = f0 + (f1 - f0) * t;
            phase += 2 * Math.PI * f / rate;
            double env = Math.Min(1, i / (rate * 0.005)) * (1 - t);
            w.Write((short)(Math.Sin(phase) * env * 0.25 * short.MaxValue));
        }
        w.Flush();
        ms2.Position = 0;
        return ms2;
    }
}

/// Mutes the default speakers while dictating, then restores them.
sealed class MediaMuter
{
    bool mutedByUs;

    public void Mute()
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            using var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (!dev.AudioEndpointVolume.Mute) { dev.AudioEndpointVolume.Mute = true; mutedByUs = true; }
        }
        catch { }
    }

    public void Restore()
    {
        if (!mutedByUs) return;
        try
        {
            using var en = new MMDeviceEnumerator();
            using var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            dev.AudioEndpointVolume.Mute = false;
        }
        catch { }
        mutedByUs = false;
    }
}

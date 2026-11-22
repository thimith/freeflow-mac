using System.Diagnostics;

namespace FreeFlow;

static class Program
{
    public static Icon AppIcon { get; private set; }

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "FreeFlow.SingleInstance", out bool first);
        if (!first) return;   // already running (look in the tray)

        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.ThreadException += (_, e) => Log(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(e.ExceptionObject as Exception);

        try { AppIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { AppIcon = SystemIcons.Application; }

        Application.Run(new TrayApp());
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); } catch { }
    }

    public static string ErrorLogPath => Path.Combine(Paths.AppData, "error.log");

    public static void Log(Exception ex)
    {
        if (ex == null) return;
        Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}");
    }

    /// An error you saw in the bubble (e.g. "Groq API key was rejected"), so it can be read later in Settings → Help.
    public static void LogError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        Write($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
    }

    static readonly object logLock = new();

    static void Write(string entry)
    {
        try
        {
            lock (logLock)
            {
                var p = ErrorLogPath;
                File.AppendAllText(p, entry + "\n\n");
                // keep it small: when it passes ~1 MB, keep only the newest ~200 KB
                var fi = new FileInfo(p);
                if (fi.Length > 1_000_000)
                {
                    var all = File.ReadAllText(p);
                    var tail = all[^200_000..];
                    int cut = tail.IndexOf("\n\n[", StringComparison.Ordinal);
                    File.WriteAllText(p, cut >= 0 ? tail[(cut + 2)..] : tail);
                }
            }
        }
        catch { }
    }

    /// Newest first: each entry is "[time] message (and details)".
    public static List<string> RecentErrors(int max = 50)
    {
        try
        {
            if (!File.Exists(ErrorLogPath)) return new List<string>();
            var text = File.ReadAllText(ErrorLogPath).Replace("\r\n", "\n");
            var parts = text.Split("\n\n[", StringSplitOptions.RemoveEmptyEntries)
                .Select((t, i) => (i == 0 && t.StartsWith("[") ? t : "[" + t).Trim())
                .Where(t => t.Length > 1).ToList();
            parts.Reverse();
            return parts.Take(max).ToList();
        }
        catch { return new List<string>(); }
    }

    public static void ClearErrors()
    {
        try { lock (logLock) File.Delete(ErrorLogPath); } catch { }
    }
}

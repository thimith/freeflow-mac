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

    public static void Log(Exception ex)
    {
        if (ex == null) return;
        try { File.AppendAllText(Path.Combine(Paths.AppData, "error.log"), $"[{DateTime.Now:u}] {ex}\n\n"); } catch { }
    }
}

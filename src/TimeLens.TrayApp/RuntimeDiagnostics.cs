namespace TimeLens.TrayApp;

/// <summary>Local, best-effort diagnostics and exception boundaries for native callbacks.</summary>
public static class RuntimeDiagnostics
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Initialize(string directory)
    {
        _path = Path.Combine(directory, "runtime.log");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write($"Unhandled exception; terminating={e.IsTerminating}: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write($"Unobserved background task: {e.Exception}");
            e.SetObserved();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Write($"Process exit; code={Environment.ExitCode}");
        Write($"Started {typeof(RuntimeDiagnostics).Assembly.GetName().Version}; pid={Environment.ProcessId}; path={Environment.ProcessPath}");
    }

    public static bool TryRun(string operation, Action action)
    {
        try { action(); return true; }
        catch (Exception ex) { Write($"{operation}: {ex}"); return false; }
    }

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                if (_path is null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (File.Exists(_path) && new FileInfo(_path).Length > 1_048_576)
                    File.Move(_path, _path + ".previous", true);
                File.AppendAllText(_path, $"{DateTime.UtcNow:o} {message}{Environment.NewLine}");
            }
        }
        catch { /* Diagnostics must never cause a second callback failure. */ }
    }
}

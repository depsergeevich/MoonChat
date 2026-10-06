using System.Diagnostics;
using Avalonia;

namespace MoonChat.Client;

internal static class Program
{
    /// <summary>
    /// Address of the MoonChat server. Obvious place to point the client at a
    /// different host or port.
    /// </summary>
    private const string ApiBaseAddress = "http://127.0.0.1:8080/";

    [STAThread]
    public static int Main(string[] args)
    {
        // Set MOONCHAT_DEBUG_LOG to capture Avalonia's own diagnostics (XAML
        // load failures, binding errors) that would otherwise go nowhere in a
        // windowed app.
        var logPath = Environment.GetEnvironmentVariable("MOONCHAT_DEBUG_LOG");
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            Trace.Listeners.Add(new FileTraceListener(logPath));
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // A failure during startup would otherwise close the process
            // silently, which is very hard to diagnose.
            Trace.WriteLine("MoonChat failed to start: " + ex);

            if (!string.IsNullOrWhiteSpace(logPath))
            {
                File.AppendAllText(logPath, Environment.NewLine + ex + Environment.NewLine);
            }

            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

/// <summary>Writes trace output to a file, creating it if needed.</summary>
internal sealed class FileTraceListener : TraceListener
{
    private readonly object _gate = new();
    private readonly string _path;

    public FileTraceListener(string path)
    {
        _path = path;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");
        }
        catch (Exception)
        {
            // If the file cannot be used, tracing silently does nothing.
        }
    }

    public override void Write(string? message)
    {
    }

    public override void WriteLine(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var line = $"{DateTime.Now:HH:mm:ss.fff} [trace] {message}";

        lock (_gate)
        {
            try
            {
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception)
            {
                // Never let diagnostics break the app.
            }
        }
    }
}
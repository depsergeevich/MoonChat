namespace MoonChat.Client.Services;

/// <summary>Receives diagnostics from the UI layer.</summary>
public interface IDiagnostics
{
    void Log(string level, string message);

    void Info(string message) => Log("info", message);

    void Warn(string message) => Log("warn", message);

    void Error(string message, Exception? ex = null)
        => Log("error", ex is null ? message : $"{message}: {ex.Message}");
}

/// <summary>
/// Writes diagnostics to the debug output and, when
/// MOONCHAT_DEBUG_LOG is set, to that file. WebView2 swallows console output,
/// so without this a UI failure would be invisible.
/// </summary>
public sealed class DiagnosticLog : IDiagnostics
{
    private readonly string? _filePath;

    public DiagnosticLog()
    {
        var configured = Environment.GetEnvironmentVariable("MOONCHAT_DEBUG_LOG");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configured)!);
            _filePath = configured;
        }
        catch (Exception)
        {
            _filePath = null;
        }
    }

    public void Log(string level, string message)
    {
        // Goes through Trace, which the app points at MOONCHAT_DEBUG_LOG.
        System.Diagnostics.Trace.WriteLine($"[{level}] {message}");
    }
}
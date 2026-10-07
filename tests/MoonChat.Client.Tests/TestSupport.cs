using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using MoonChat.Client.Services;

namespace MoonChat.Client.Tests;

/// <summary>
/// A <see cref="SessionStore"/> wired to a throwaway directory.
///
/// The production store hard-codes its cache path to %LOCALAPPDATA%\MoonChat
/// (SessionStore.cs:26). Redirecting the private field keeps the tests hermetic
/// and, just as importantly, stops them from clobbering the developer's real
/// signed-in session.
/// </summary>
internal sealed class TempSessionStore : IDisposable
{
    private readonly string _root;

    private TempSessionStore(string root, SessionStore store)
    {
        _root = root;
        Store = store;
    }

    public SessionStore Store { get; }

    /// <summary>Absolute path of the cache file this store reads and writes.</summary>
    public string Path => System.IO.Path.Combine(_root, "session.json");

    public static TempSessionStore Create()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "moonchat-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var store = new SessionStore();
        var field = typeof(SessionStore).GetField("_path", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SessionStore._path not found; the test helper needs updating.");

        field.SetValue(store, System.IO.Path.Combine(root, "session.json"));
        return new TempSessionStore(root, store);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked cache file must not fail the test run.
        }
    }
}

/// <summary>Records outgoing requests and replays canned responses, with no network.</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string> Bodies { get; } = [];

    public static StubHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        return _responder(request);
    }
}

/// <summary>A diagnostics sink that keeps what it was told, for assertions.</summary>
internal sealed class RecordingDiagnostics : IDiagnostics
{
    public List<(string Level, string Message)> Entries { get; } = [];

    public void Log(string level, string message) => Entries.Add((level, message));
}

/// <summary>
/// Bounded polling for conditions that settle through an <c>async void</c>
/// command. Uses a deadline rather than a fixed sleep so a fast machine is not
/// penalised and a slow one still fails instead of hanging.
/// </summary>
internal static class Wait
{
    public static bool Until(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return condition();
    }
}
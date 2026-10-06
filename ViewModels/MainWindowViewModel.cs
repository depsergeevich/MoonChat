using MoonChat.Client.Services;
using MoonChat.Client.ViewModels;

namespace MoonChat.Client.ViewModels;

/// <summary>
/// Top-level shell state: decides between the sign-in screen and the chat view,
/// and starts the chat session once someone is signed in.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ChatState _chat;
    private readonly SessionStore _session;
    private readonly IDiagnostics _log;

    private bool _isAuthenticated;
    private bool _starting = true;

    public MainWindowViewModel(
        ChatState chat,
        LoginViewModel login,
        ChatViewModel chatView,
        SessionStore session,
        IDiagnostics log)
    {
        _chat = chat;
        _session = session;
        _log = log;

        Login = login;
        Chat = chatView;

        Login.SignedIn = StartChatAsync;
        Chat.SignedOut = SignOutAsync;
        _session.Changed += OnSessionChanged;

        _isAuthenticated = _session.IsSignedIn;
    }

    public LoginViewModel Login { get; }

    public ChatViewModel Chat { get; }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set => SetProperty(ref _isAuthenticated, value);
    }

    /// <summary>Hides the shell until the stored session has been read.</summary>
    public bool IsStarting
    {
        get => _starting;
        private set => SetProperty(ref _starting, value);
    }

    public bool ShowLogin => !IsStarting && !IsAuthenticated;

    public bool ShowChat => !IsStarting && IsAuthenticated;

    public void Start()
    {
        IsStarting = false;
        OnPropertiesChanged(nameof(ShowLogin), nameof(ShowChat));

        if (IsAuthenticated)
        {
            // A returning user goes straight into the app, so the session load
            // has to be kicked off here as well as after a sign-in.
            _ = StartChatAsync();
        }
    }

    private void OnSessionChanged()
    {
        if (_session.IsSignedIn == IsAuthenticated)
        {
            return;
        }

        IsAuthenticated = _session.IsSignedIn;
        OnPropertiesChanged(nameof(ShowLogin), nameof(ShowChat));

        if (IsAuthenticated)
        {
            _ = StartChatAsync();
        }
    }

    private async Task StartChatAsync()
    {
        IsAuthenticated = true;
        OnPropertiesChanged(nameof(ShowLogin), nameof(ShowChat));

        try
        {
            await _chat.StartAsync();
        }
        catch (ApiException ex)
        {
            _log.Error("could not load chat", ex);
        }
        catch (Exception ex)
        {
            _log.Error("unexpected failure while loading chat", ex);
        }
    }

    private async Task SignOutAsync()
    {
        IsAuthenticated = false;
        OnPropertiesChanged(nameof(ShowLogin), nameof(ShowChat));
        await Task.CompletedTask;
    }

    public void Dispose() => _session.Changed -= OnSessionChanged;
}
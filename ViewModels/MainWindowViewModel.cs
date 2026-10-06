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

    public bool ShowLogin => !IsAuthenticated;

    public bool ShowChat => IsAuthenticated;

    /// <summary>
    /// Called once the window opens. The stored session is already read by
    /// this point, so a returning user goes straight into the chat shell.
    /// </summary>
    public void Start()
    {
        if (IsAuthenticated)
        {
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
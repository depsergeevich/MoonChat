using MoonChat.Client.Models;
using MoonChat.Client.Services;

namespace MoonChat.Client.ViewModels;

/// <summary>
/// Owns the sign-in form and switches the shell to the chat view once a session
/// exists.
/// </summary>
public sealed class LoginViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly SessionStore _session;
    private readonly IDiagnostics _log;

    private string _username = "";
    private string _displayName = "";
    private string _password = "";
    private bool _isRegistering;
    private bool _busy;
    private string? _error;

    public LoginViewModel(ApiClient api, SessionStore session, IDiagnostics log)
    {
        _api = api;
        _session = session;
        _log = log;

        SignInCommand = new RelayCommand(_ => SubmitAsync(), _ => CanSubmit);
        ToggleModeCommand = new RelayCommand(_ => ToggleMode());
    }

    /// <summary>Raised after a successful sign-in or registration.</summary>
    public Func<Task>? SignedIn { get; set; }

    public RelayCommand SignInCommand { get; }

    public RelayCommand ToggleModeCommand { get; }

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                RefreshCommands();
            }
        }
    }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
            {
                RefreshCommands();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                RefreshCommands();
            }
        }
    }

    public bool IsRegistering
    {
        get => _isRegistering;
        private set => SetProperty(ref _isRegistering, value);
    }

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (SetProperty(ref _busy, value))
            {
                RefreshCommands();
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    /// <summary>Convenience for hiding the form while a request is in flight.</summary>
    public bool IsIdle => !IsBusy;

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public string SubmitLabel => IsRegistering ? "Create account" : "Sign in";

    public string SwitchLabel => IsRegistering
        ? "Already have an account? Sign in"
        : "New here? Create an account";

    public string Heading => IsRegistering ? "Create your account" : "Welcome back";

    public string LogoInitial =>
        string.IsNullOrWhiteSpace(Username) ? "M" : Username[..1].ToUpperInvariant();

    public bool IsDisplayNameVisible => IsRegistering;

    private bool CanSubmit =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        (!IsRegistering || !string.IsNullOrWhiteSpace(DisplayName));

    private void RefreshCommands()
    {
        SignInCommand.RaiseCanExecuteChanged();
        OnPropertiesChanged(nameof(LogoInitial));
    }

    private void ToggleMode()
    {
        IsRegistering = !IsRegistering;
        Error = null;

        OnPropertiesChanged(
            nameof(SubmitLabel),
            nameof(SwitchLabel),
            nameof(Heading),
            nameof(IsDisplayNameVisible));
    }

    private async Task SubmitAsync()
    {
        if (!CanSubmit)
        {
            return;
        }

        IsBusy = true;
        Error = null;

        try
        {
            if (IsRegistering)
            {
                await _api.RegisterAsync(Username.Trim(), DisplayName.Trim(), Password);
            }
            else
            {
                await _api.LoginAsync(Username.Trim(), Password);
            }

            Password = "";

            if (SignedIn is not null)
            {
                await SignedIn();
            }
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            _log.Error("sign-in failed", ex);
            Error = $"Could not reach the server ({ex.Message}).";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
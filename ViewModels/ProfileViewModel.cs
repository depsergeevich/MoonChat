using MoonChat.Client.Models;
using MoonChat.Client.Services;

namespace MoonChat.Client.ViewModels;

/// <summary>
/// The account screen: photo, display name, password, and the destructive
/// actions. Everything here acts on the signed-in user, so the shell closes it
/// as soon as the session goes away.
/// </summary>
public sealed class ProfileViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly ChatState _state;
    private readonly SessionStore _session;
    private readonly IFilePicker _picker;
    private readonly IDiagnostics _log;

    private string _displayName = "";
    private string _currentPassword = "";
    private string _newPassword = "";
    private string _confirmPassword = "";
    private string _deletePassword = "";
    private bool _confirmingDelete;
    private bool _busy;
    private string? _error;
    private string? _status;

    public ProfileViewModel(
        ApiClient api,
        ChatState state,
        SessionStore session,
        IFilePicker picker,
        IDiagnostics log)
    {
        _api = api;
        _state = state;
        _session = session;
        _picker = picker;
        _log = log;

        _displayName = session.User?.DisplayName ?? "";

        SaveNameCommand = new RelayCommand(_ => SaveNameAsync(), _ => CanSaveName);
        ChoosePhotoCommand = new RelayCommand(_ => ChoosePhotoAsync(), _ => !IsBusy);
        RemovePhotoCommand = new RelayCommand(_ => RemovePhotoAsync(), _ => !IsBusy && HasAvatar);
        ChangePasswordCommand = new RelayCommand(_ => ChangePasswordAsync(), _ => CanChangePassword);
        SignOutEverywhereCommand = new RelayCommand(_ => SignOutEverywhereAsync(), _ => !IsBusy);
        RequestDeleteCommand = new RelayCommand(_ => ConfirmingDelete = true);
        CancelDeleteCommand = new RelayCommand(_ => ConfirmingDelete = false);
        DeleteAccountCommand = new RelayCommand(_ => DeleteAccountAsync(), _ => CanDeleteAccount);
        CloseCommand = new RelayCommand(_ => Closed?.Invoke());

        _session.Changed += OnSessionChanged;
    }

    // --- commands ---

    public RelayCommand SaveNameCommand { get; }
    public RelayCommand ChoosePhotoCommand { get; }
    public RelayCommand RemovePhotoCommand { get; }
    public RelayCommand ChangePasswordCommand { get; }
    public RelayCommand SignOutEverywhereCommand { get; }
    public RelayCommand RequestDeleteCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }
    public RelayCommand DeleteAccountCommand { get; }
    public RelayCommand CloseCommand { get; }

    /// <summary>Raised when the session ends, so the shell can drop this screen.</summary>
    public Action? Closed { get; set; }

    // --- identity ---

    public string Username => _session.User?.Username ?? "";

    public string AvatarUrl => _session.User?.AvatarUrl ?? "";

    public long UserId => _session.User?.Id ?? 0;

    public bool HasAvatar => !string.IsNullOrEmpty(AvatarUrl);

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
            {
                SaveNameCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // --- password ---

    public string CurrentPassword
    {
        get => _currentPassword;
        set
        {
            if (SetProperty(ref _currentPassword, value))
            {
                ChangePasswordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewPassword
    {
        get => _newPassword;
        set
        {
            if (SetProperty(ref _newPassword, value))
            {
                ChangePasswordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set
        {
            if (SetProperty(ref _confirmPassword, value))
            {
                ChangePasswordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DeletePassword
    {
        get => _deletePassword;
        set
        {
            if (SetProperty(ref _deletePassword, value))
            {
                DeleteAccountCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Two-step confirmation, so a stray click cannot delete an account.</summary>
    public bool ConfirmingDelete
    {
        get => _confirmingDelete;
        private set
        {
            if (SetProperty(ref _confirmingDelete, value))
            {
                DeleteAccountCommand.RaiseCanExecuteChanged();
            }
        }
    }

    // --- status ---

    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (SetProperty(ref _busy, value))
            {
                RaiseCommands();
            }
        }
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Confirmation of the last successful action.</summary>
    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    private bool CanSaveName =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(DisplayName) &&
        DisplayName.Trim() != (_session.User?.DisplayName ?? "");

    private bool CanChangePassword =>
        !IsBusy &&
        !string.IsNullOrEmpty(CurrentPassword) &&
        NewPassword.Length >= 8 &&
        NewPassword == ConfirmPassword;

    private bool CanDeleteAccount => !IsBusy && !string.IsNullOrEmpty(DeletePassword);

    // --- actions ---

    private async Task ChoosePhotoAsync()
    {
        Error = null;
        Status = null;

        try
        {
            using var stream = await _picker.PickImageAsync();
            if (stream is null)
            {
                return; // cancelled
            }

            await _state.UploadAvatarAsync(
                stream,
                fileName: "avatar",
                contentType: "application/octet-stream");

            Status = "Photo updated.";
            RaiseIdentity();
        }
        catch (ApiException ex)
        {
            Error = ex.Code switch
            {
                "unsupported_image" => "That file is not a PNG, JPEG, GIF or WebP image.",
                "image_too_large" => "That image is too large. The limit is 4 MB.",
                _ => ex.Message,
            };
        }
        catch (Exception ex)
        {
            _log.Error("avatar upload failed", ex);
            Error = "Could not upload that image.";
        }
    }

    private async Task RemovePhotoAsync()
    {
        Error = null;
        Status = null;
        try
        {
            await _api.DeleteAvatarAsync();
            await _state.RefreshContactsAsync();
            Status = "Photo removed.";
            RaiseIdentity();
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
    }

    private async Task SaveNameAsync()
    {
        var name = DisplayName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Error = "A display name cannot be empty.";
            return;
        }

        Error = null;
        Status = null;
        try
        {
            await _api.UpdateDisplayNameAsync(name);
            DisplayName = name;
            Status = "Display name updated.";
            RaiseIdentity();
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
    }

    private async Task ChangePasswordAsync()
    {
        Error = null;
        Status = null;
        try
        {
            await _api.ChangePasswordAsync(CurrentPassword, NewPassword);
            CurrentPassword = "";
            NewPassword = "";
            ConfirmPassword = "";
            Status = "Password changed. Other devices have been signed out.";
        }
        catch (ApiException ex)
        {
            Error = ex.Code switch
            {
                "invalid_credentials" => "That is not your current password.",
                "password_too_short" => "Use at least 8 characters.",
                "password_reused" => "Choose a password you have not used here before.",
                _ => ex.Message,
            };
        }
    }

    private async Task SignOutEverywhereAsync()
    {
        Error = null;
        try
        {
            await _api.LogoutEverywhereAsync();
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
    }

    private async Task DeleteAccountAsync()
    {
        Error = null;
        try
        {
            await _api.DeleteAccountAsync(DeletePassword);
            DeletePassword = "";
            Closed?.Invoke();
        }
        catch (ApiException ex)
        {
            Error = ex.Code == "invalid_credentials"
                ? "That is not your password."
                : ex.Message;
        }
    }

    // --- plumbing ---

    private void OnSessionChanged()
    {
        if (!_session.IsSignedIn)
        {
            Closed?.Invoke();
            return;
        }

        RaiseIdentity();
    }

    /// <summary>
    /// Re-reads the identity bits after the server changed them. The cached
    /// user object is the single source of truth, so there is nothing to keep in
    /// sync by hand.
    /// </summary>
    private void RaiseIdentity()
    {
        OnPropertiesChanged(nameof(Username), nameof(AvatarUrl), nameof(HasAvatar));
    }

    private void RaiseCommands()
    {
        SaveNameCommand.RaiseCanExecuteChanged();
        ChoosePhotoCommand.RaiseCanExecuteChanged();
        RemovePhotoCommand.RaiseCanExecuteChanged();
        ChangePasswordCommand.RaiseCanExecuteChanged();
        SignOutEverywhereCommand.RaiseCanExecuteChanged();
        DeleteAccountCommand.RaiseCanExecuteChanged();
    }

    public void Dispose() => _session.Changed -= OnSessionChanged;
}
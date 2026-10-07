using System.Collections.ObjectModel;
using MoonChat.Client.Models;
using MoonChat.Client.Services;

namespace MoonChat.Client.ViewModels;

/// <summary>
/// The chat screen: conversation list, contacts, message thread and composer.
/// Mirrors <see cref="ChatState"/> into bindable collections.
/// </summary>
public sealed class ChatViewModel : ViewModelBase, IDisposable
{
    private readonly ChatState _state;
    private readonly ApiClient _api;
    private readonly SessionStore _session;
    private readonly IDiagnostics _log;

    private Conversation? _activeConversation;
    private bool _showContacts;
    private string _draft = "";
    private string _newContact = "";
    private string? _error;
    private string? _sendError;
    private bool _sending;
    private bool _loadingOlder;
    private bool _peerTyping;
    private string _connectionStatus = "Offline";
    private string _messagesSignature = "";

    public ChatViewModel(ChatState state, ApiClient api, SessionStore session, IDiagnostics log)
    {
        _state = state;
        _api = api;
        _session = session;
        _log = log;

        OpenConversationCommand = new RelayCommand(p => OpenConversationAsync(p as Conversation));
        StartConversationCommand = new RelayCommand(p => StartConversationAsync(p as Contact));
        SendCommand = new RelayCommand(_ => SendAsync(), _ => CanSend);
        AddContactCommand = new RelayCommand(_ => AddContactAsync(), _ => CanAddContact);
        LoadOlderCommand = new RelayCommand(_ => LoadOlderAsync(), _ => !_loadingOlder && HasMoreHistory);
        SignOutCommand = new RelayCommand(_ => SignOutAsync());
        ShowChatsCommand = new RelayCommand(_ => ShowContacts = false);
        ShowContactsCommand = new RelayCommand(_ => ShowContacts = true);
        ShowProfileCommand = new RelayCommand(_ => OpenProfile?.Invoke());

        _state.Changed += OnStateChanged;
        SyncAll();
    }

    public ObservableCollection<MessageItem> Messages { get; } = [];

    public ObservableCollection<PendingItem> Pending { get; } = [];

    public RelayCommand OpenConversationCommand { get; }

    public RelayCommand StartConversationCommand { get; }

    public RelayCommand SendCommand { get; }

    public RelayCommand AddContactCommand { get; }

    public RelayCommand LoadOlderCommand { get; }

    public RelayCommand SignOutCommand { get; }

    public RelayCommand ShowChatsCommand { get; }

    public RelayCommand ShowContactsCommand { get; }

    /// <summary>Raised after signing out so the shell can show the login view.</summary>
    public Func<Task>? SignedOut { get; set; }

    /// <summary>Raised when the account button is pressed, so the shell can cover the chat.</summary>
    public Action? OpenProfile { get; set; }

    public RelayCommand ShowProfileCommand { get; }

    public ObservableCollection<Conversation> Conversations => _state.Conversations;

    public ObservableCollection<Contact> Contacts => _state.Contacts;

    public User? Me => _session.User;

    public string MeName => Me?.DisplayName ?? "";

    public string MeUsername => Me?.Username ?? "";

    public string MeAvatarUrl => Me?.AvatarUrl ?? "";

    public long MeId => Me?.Id ?? 0;

    public Conversation? ActiveConversation
    {
        get => _activeConversation;
        private set
        {
            if (SetProperty(ref _activeConversation, value))
            {
                OnPropertiesChanged(
                    nameof(HasActiveConversation),
                    nameof(ActiveConversationId),
                    nameof(PeerName),
                    nameof(PeerUsername),
                    nameof(PeerOnline),
                    nameof(PeerPresenceText),
                    nameof(PeerSubtitle));
            }
        }
    }

    public bool HasActiveConversation => ActiveConversation is not null;

    /// <summary>Id of the open thread, or 0. Used to mark the selected row.</summary>
    public long ActiveConversationId => ActiveConversation?.Id ?? 0;

    public string PeerName => ActiveConversation?.Peer.DisplayName ?? "";

    public string PeerUsername => ActiveConversation?.Peer.Username ?? "";

    /// <summary>
    /// Whether the person in the open thread is connected right now. Distinct
    /// from <see cref="IsConnected"/>, which is about *this* client's socket.
    /// </summary>
    public bool PeerOnline => ActiveConversation?.Peer.Online ?? false;

    /// <summary>"Online", "Last seen 5m ago", or empty.</summary>
    public string PeerPresenceText => ActiveConversation?.Peer.PresenceText ?? "";

    /// <summary>Username plus presence, which is what the thread header shows.</summary>
    public string PeerSubtitle
    {
        get
        {
            var handle = string.IsNullOrEmpty(PeerUsername) ? "" : "@" + PeerUsername;
            if (!string.IsNullOrEmpty(PeerPresenceText))
            {
                return string.IsNullOrEmpty(handle)
                    ? PeerPresenceText
                    : $"{handle} · {PeerPresenceText}";
            }
            return handle;
        }
    }

    public string PeerAvatarUrl => ActiveConversation?.Peer.AvatarUrl ?? "";

    public long PeerId => ActiveConversation?.Peer.Id ?? 0;

    public bool ShowContacts
    {
        get => _showContacts;
        set
        {
            if (SetProperty(ref _showContacts, value))
            {
                OnPropertiesChanged(nameof(ShowChatsTab), nameof(ShowContactsTab));
            }
        }
    }

    public bool ShowChatsTab => !ShowContacts;

    public bool ShowContactsTab => ShowContacts;

    public long TotalUnread
    {
        get
        {
            long total = 0;
            foreach (var conversation in Conversations)
            {
                total += conversation.UnreadCount;
            }
            return total;
        }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public bool IsConnected => _state.Status == RealtimeStatus.Connected;

    public bool IsConnecting => _state.Status == RealtimeStatus.Connecting;

    public bool HasMoreHistory
    {
        get => _state.HasMoreHistory;
        private set => SetProperty(ref _hasMoreHistory, value);
    }

    private bool _hasMoreHistory;

    public bool IsLoadingOlder
    {
        get => _loadingOlder;
        private set => SetProperty(ref _loadingOlder, value);
    }

    public bool PeerTyping
    {
        get => _peerTyping;
        private set => SetProperty(ref _peerTyping, value);
    }

    /// <summary>Ready-made text for the typing indicator row.</summary>
    public string PeerTypingText => PeerTyping ? $"{PeerName} is typing…" : string.Empty;

    /// <summary>Ready-made text for the empty state.</summary>
    public string WelcomeText => string.IsNullOrEmpty(MeName) ? "Welcome" : $"Welcome, {MeName}";

    public string Draft
    {
        get => _draft;
        set
        {
            if (SetProperty(ref _draft, value))
            {
                SendCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewContact
    {
        get => _newContact;
        set
        {
            if (SetProperty(ref _newContact, value))
            {
                AddContactCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string? Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    public string? SendError
    {
        get => _sendError;
        private set => SetProperty(ref _sendError, value);
    }

    public bool IsSending
    {
        get => _sending;
        private set => SetProperty(ref _sending, value);
    }

    public bool CanSend => !IsSending && !string.IsNullOrWhiteSpace(Draft) && HasActiveConversation;

    public bool CanAddContact => !string.IsNullOrWhiteSpace(NewContact);

    public bool HasMessages => Messages.Count > 0;

    public bool HasConversations => Conversations.Count > 0;

    public bool HasContacts => Contacts.Count > 0;

    /// <summary>
    /// Two-way bound to the conversation list so selecting a row is what opens
    /// the thread. Setting it to null while refreshing is ignored.
    /// </summary>

    private void OnStateChanged() => SyncAll();

    /// <summary>
    /// Copies state into the bindable collections. Message rows are only
    /// rebuilt when the set actually changed, which keeps typing indicators and
    /// presence updates from resetting the list.
    /// </summary>
    private void SyncAll()
    {
        OnPropertiesChanged(
            nameof(Conversations),
            nameof(Contacts),
            nameof(Me),
            nameof(MeName),
            nameof(MeUsername),
            nameof(MeAvatarUrl),
            nameof(MeId),
            nameof(TotalUnread),
            nameof(IsConnected),
            nameof(IsConnecting),
            nameof(HasMoreHistory),
            nameof(IsLoadingOlder),
            nameof(HasConversations),
            nameof(HasContacts),
            nameof(PeerName),
            nameof(PeerOnline),
            nameof(PeerPresenceText),
            nameof(PeerSubtitle),
            nameof(MeName));

        ConnectionStatus = _state.Status switch
        {
            RealtimeStatus.Connected => "Live",
            RealtimeStatus.Connecting => "Connecting…",
            _ => "Offline",
        };

        ActiveConversation = _state.ActiveConversation;
        PeerTyping = _state.PeerTypingIn is not null;
        HasMoreHistory = _state.HasMoreHistory;

        SyncMessages();
        SendCommand.RaiseCanExecuteChanged();
        AddContactCommand.RaiseCanExecuteChanged();
        LoadOlderCommand.RaiseCanExecuteChanged();
    }

    private void SyncMessages()
    {
        var messages = _state.ActiveMessages;
        var lastId = messages.Count > 0 ? messages[^1].Id : 0;
        var peerRead = _state.PeerReadUpTo(_state.ActiveConversationId);

        // The peer-read cursor is part of the signature because a read receipt
        // can arrive without the message set changing at all, and that receipt
        // is what reveals the "Seen" marker.
        var signature = $"{_state.ActiveConversationId}:{messages.Count}:{lastId}:{peerRead}";
        if (signature == _messagesSignature)
        {
            return;
        }
        _messagesSignature = signature;

        Messages.Clear();

        // Only the newest of my own messages can carry the marker.
        var newestMine = messages.LastOrDefault(m => m.SenderId == MeId)?.Id ?? 0;

        ChatMessage? previous = null;
        foreach (var message in messages)
        {
            var seen = message.SenderId == MeId && message.Id == newestMine && message.Id <= peerRead;
            Messages.Add(new MessageItem(message, MeId, previous, seen));
            previous = message;
        }

        SyncPending();

        OnPropertiesChanged(nameof(HasMessages), nameof(Pending));
    }

    private void SyncPending()
    {
        Pending.Clear();
        foreach (var pending in _state.ActivePending)
        {
            Pending.Add(new PendingItem(pending.Body));
        }
    }

    private async Task OpenConversationAsync(Conversation? conversation)
    {
        if (conversation is null)
        {
            return;
        }

        Error = null;
        _messagesSignature = "";

        try
        {
            await _state.OpenConversationAsync(conversation.Id);
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            _log.Error("open conversation failed", ex);
            Error = "Could not open the conversation.";
        }
    }

    private async Task StartConversationAsync(Contact? contact)
    {
        if (contact is null)
        {
            return;
        }

        Error = null;
        _messagesSignature = "";

        try
        {
            await _state.StartConversationAsync(contact.Id);
            ShowContacts = false;
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
        catch (Exception ex)
        {
            _log.Error("start conversation failed", ex);
            Error = "Could not start the conversation.";
        }
    }

    private async Task SendAsync()
    {
        var body = Draft.Trim();
        var conversation = ActiveConversation;
        if (string.IsNullOrEmpty(body) || conversation is null)
        {
            return;
        }

        Draft = "";
        SendError = null;
        IsSending = true;

        try
        {
            await _state.SendAsync(conversation.Id, body);
        }
        catch (ApiException ex)
        {
            SendError = ex.Message;
        }
        catch (Exception ex)
        {
            _log.Error("send failed", ex);
            SendError = "Could not send: " + ex.Message;
        }
        finally
        {
            IsSending = false;
        }
    }

    private async Task AddContactAsync()
    {
        var username = NewContact.Trim();
        if (string.IsNullOrEmpty(username))
        {
            return;
        }

        Error = null;
        try
        {
            await _state.AddContactAsync(username);
            NewContact = "";
            await _state.RefreshContactsAsync();
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
    }

    private async Task LoadOlderAsync()
    {
        IsLoadingOlder = true;
        try
        {
            await _state.LoadOlderMessagesAsync();
        }
        catch (ApiException ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsLoadingOlder = false;
        }
    }

    private async Task SignOutAsync()
    {
        await _state.StopAsync();
        await _api.LogoutAsync();

        if (SignedOut is not null)
        {
            await SignedOut();
        }
    }

    /// <summary>Called by the view when the composer receives Enter.</summary>
    public async Task SubmitComposerAsync()
    {
        if (CanSend)
        {
            await SendAsync();
            return;
        }

        // Typing on an open conversation is what drives the indicator.
        if (_state.ActiveConversationId != 0)
        {
            await _state.ReportTypingAsync(_state.ActiveConversationId);
        }
    }

    public void Dispose() => _state.Changed -= OnStateChanged;
}
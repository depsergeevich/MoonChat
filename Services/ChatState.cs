using System.Collections.ObjectModel;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Threading;
using MoonChat.Client.Models;

namespace MoonChat.Client.Services;

/// <summary>How the live connection currently looks.</summary>
public enum RealtimeStatus
{
    Disconnected,
    Connecting,
    Connected,
}

/// <summary>
/// Owns all chat state and the single websocket connection. Components read
/// from here and subscribe to <see cref="Changed"/> to redraw.
/// </summary>
public sealed class ChatState : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const int MaxBackoffSeconds = 30;

    private readonly ApiClient _api;
    private readonly SessionStore _session;
    private readonly IDiagnostics _log;
    private readonly SemaphoreSlim _sendGate = new(1, 1);



    /// <summary>
    /// An outgoing message that the server has not acknowledged yet. Kept apart
    /// from history so it can never be confused with a persisted row.
    /// </summary>
    public sealed record PendingSend(string ClientMessageId, long ConversationId, string Body, long CreatedAt);

    private readonly Dictionary<long, List<ChatMessage>> _history = [];

    // Conversations whose newest page has actually been fetched. The
    // conversation list seeds history with just the last message, so presence
    // in _history alone cannot mean "loaded".
    private readonly HashSet<long> _historyLoaded = [];
    private readonly List<PendingSend> _pendingSends = [];
    private readonly Dictionary<long, CancellationTokenSource> _typingTimers = [];

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _lifetime;
    private Task? _pump;

    public ChatState(ApiClient api, SessionStore session, IDiagnostics log)
    {
        _api = api;
        _session = session;
        _log = log;
    }

    public ObservableCollection<Conversation> Conversations { get; } = [];
    public ObservableCollection<Contact> Contacts { get; } = [];

    public User? Me => _session.User;
    public RealtimeStatus Status { get; private set; } = RealtimeStatus.Disconnected;
    public string? StatusDetail { get; private set; }

    public long ActiveConversationId { get; private set; }
    public long? PeerTypingIn { get; private set; }
    public bool HasMoreHistory { get; private set; }

    /// <summary>Raised whenever any displayed state changed.</summary>
    public event Action? Changed;

    /// <summary>Persisted messages of the open conversation, oldest first.</summary>
    public IReadOnlyList<ChatMessage> ActiveMessages
        => ActiveConversationId != 0 && _history.TryGetValue(ActiveConversationId, out var list)
            ? list
            : [];

    /// <summary>Outgoing messages still awaiting acknowledgement, oldest first.</summary>
    public IReadOnlyList<PendingSend> ActivePending
        => _pendingSends
            .Where(p => p.ConversationId == ActiveConversationId)
            .OrderBy(p => p.CreatedAt)
            .ToList();

    public Conversation? ActiveConversation
        => Conversations.FirstOrDefault(c => c.Id == ActiveConversationId);

    // --- lifecycle ---

    /// <summary>Loads contacts and conversations, then opens the live channel.</summary>
    public async Task StartAsync()
    {
        await RefreshContactsAsync();
        await RefreshConversationsAsync();

        // Reopen whatever was on screen last time, if that thread still exists.
        var restore = _session.LastConversationId;
        if (restore is not null && Conversations.Any(c => c.Id == restore.Value))
        {
            try
            {
                await OpenConversationAsync(restore.Value);
            }
            catch (ApiException ex)
            {
                _log.Error($"could not reopen conversation {restore.Value}", ex);
            }
        }

        Connect();
    }

    public async Task StopAsync()
    {
        foreach (var cts in _typingTimers.Values)
        {
            cts.Cancel();
        }
        _typingTimers.Clear();

        await _sendGate.WaitAsync();
        try
        {
            await CloseSocketAsync();
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private void Connect()
    {
        if (_lifetime is not null)
        {
            return; // already running
        }

        _lifetime = new CancellationTokenSource();
        _pump = Task.Run(() => RunConnectionLoopAsync(_lifetime.Token));
    }

    /// <summary>
    /// Keeps the websocket alive, reconnecting with exponential backoff and
    /// resyncing missed messages after every successful reconnect.
    /// </summary>
    private async Task RunConnectionLoopAsync(CancellationToken ct)
    {
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                SetStatus(RealtimeStatus.Connecting, attempt == 0 ? null : $"reconnecting (attempt {attempt + 1})");

                var socket = new ClientWebSocket();
                socket.Options.SetRequestHeader("Authorization", "Bearer " + _session.Token);

                var baseUri = new Uri(_api.BaseAddress);
                var scheme = baseUri.Scheme == "https" ? "wss" : "ws";
                var uri = new UriBuilder(baseUri) { Scheme = scheme, Path = "/api/ws" }.Uri;

                await socket.ConnectAsync(uri, ct);
                _socket = socket;

                attempt = 0;
                SetStatus(RealtimeStatus.Connected, null);
                Notify();

                await ResyncAfterReconnectAsync();

                await ReadLoopAsync(socket, ct);

                // A clean close still means the channel is gone.
                if (!ct.IsCancellationRequested)
                {
                    SetStatus(RealtimeStatus.Disconnected, "connection closed");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetStatus(RealtimeStatus.Disconnected, ex.Message);
            }
            finally
            {
                _socket = null;
                Notify();
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            var delaySeconds = Math.Min(Math.Pow(2, attempt), MaxBackoffSeconds);
            attempt++;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ReadLoopAsync(ClientWebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];

        while (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (message.Length == 0)
            {
                continue;
            }

            try
            {
                HandleFrame(Encoding.UTF8.GetString(message.ToArray()));
            }
            catch (JsonException)
            {
                // Ignore frames we cannot parse rather than tearing down the channel.
            }
        }
    }

    private void HandleFrame(string json)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(json, Json);
        if (envelope is null)
        {
            return;
        }

        switch (envelope.Type)
        {
            case "hello":
            case "pong":
                break;

            case "message_new":
                Receive(envelope.Payload?.Deserialize<MessageNewPayload>(Json)?.ChatMessage);
                break;

            case "ack":
                var ack = envelope.Payload?.Deserialize<AckPayload>(Json);
                if (ack is not null)
                {
                    DropPending(ack.ClientMessageId);
                    Receive(ack.ChatMessage);
                }
                break;

            case "presence":
                ApplyPresence(envelope.Payload?.Deserialize<PresencePayload>(Json));
                break;

            case "typing":
                var typing = envelope.Payload?.Deserialize<TypingEventPayload>(Json);
                if (typing is not null && typing.ConversationId == ActiveConversationId && typing.UserId != Me?.Id)
                {
                    PeerTypingIn = typing.Typing ? typing.UserId : null;
                    Notify();
                }
                break;

            case "token_refresh":
                var refreshed = envelope.Payload?.Deserialize<TokenRefreshPayload>(Json);
                if (refreshed is not null)
                {
                    _session.UpdateToken(refreshed.Token);
                }
                break;

            case "contacts_changed":
                _ = RefreshContactsAsync();
                break;

            case "conversations_changed":
                _ = RefreshConversationsAsync();
                break;
        }
    }

    // --- receiving ---

    /// <summary>
    /// Adds an incoming message, ignoring duplicates. Both the broadcast and
    /// the sender's own ack deliver the same row, so deduplication by id is
    /// what stops the UI showing it twice.
    /// </summary>
    private void Receive(ChatMessage? message)
    {
        if (message is null)
        {
            return;
        }

        _log.Info($"received message {message.Id} in conversation {message.ConversationId}: {message.Body}");

        OnUi(() =>
        {
            var list = HistoryFor(message.ConversationId);
            if (list.Any(m => m.Id == message.Id))
            {
                return;
            }

            list.Add(message);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));

            // The server also broadcasts to the sender. If the ack was lost, the
            // echo is what tells us the send went through.
            if (message.SenderId == Me?.Id)
            {
                _pendingSends.RemoveAll(p => p.ConversationId == message.ConversationId && p.Body == message.Body);
            }

            TouchConversation(message);
            Notify();
        });
    }

    /// <summary>Records an outgoing message that is not yet confirmed by the server.</summary>
    private void QueuePending(string clientMessageId, long conversationId, string body)
    {
        var pending = new PendingSend(clientMessageId, conversationId, body, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        OnUi(() =>
        {
            _pendingSends.Add(pending);
            Notify();
        });
    }

    private void DropPending(string clientMessageId)
    {
        OnUi(() => _pendingSends.RemoveAll(p => p.ClientMessageId == clientMessageId));
    }

    /// <summary>Fetches anything that arrived while the socket was down.</summary>
    private async Task ResyncAfterReconnectAsync()
    {
        try
        {
            await RefreshContactsAsync();
            await RefreshConversationsAsync();

            if (ActiveConversationId != 0)
            {
                var newest = ActiveMessages.Count > 0 ? ActiveMessages[^1].Id : 0;
                var page = await _api.GetMessagesAsync(ActiveConversationId, after: newest);

                OnUi(() =>
                {
                    var list = HistoryFor(ActiveConversationId);
                    var known = list.Select(m => m.Id).ToHashSet();
                    foreach (var message in page.Messages.Where(m => !known.Contains(m.Id)))
                    {
                        list.Add(message);
                    }
                    list.Sort((a, b) => a.Id.CompareTo(b.Id));
                    Notify();
                });
            }
        }
        catch (ApiException ex) when (ex.IsAuthFailure)
        {
            SetStatus(RealtimeStatus.Disconnected, "session expired, please sign in again");
        }
        catch (Exception ex)
        {
            SetStatus(RealtimeStatus.Connected, "resync failed: " + ex.Message);
        }
    }

    // --- sending ---

    /// <summary>
    /// Sends a message. The websocket is preferred; when it is unavailable the
    /// REST fallback delivers it instead, so the user never loses a message.
    /// </summary>
    public async Task SendAsync(long conversationId, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        var clientMessageId = Guid.NewGuid().ToString("N");

        if (Status != RealtimeStatus.Connected)
        {
            var sent = await _api.SendMessageAsync(conversationId, body, ct);
            Receive(sent);
            return;
        }

        QueuePending(clientMessageId, conversationId, body);

        try
        {
            await SendFrameAsync(new
            {
                type = "send_message",
                id = clientMessageId,
                payload = new { conversationId, body, clientMessageId },
            }, ct);
        }
        catch (Exception)
        {
            // The socket died mid-send: fall back to REST so the text still lands.
            DropPending(clientMessageId);
            var sent = await _api.SendMessageAsync(conversationId, body, ct);
            Receive(sent);
        }
    }

    private async Task SendFrameAsync(object frame, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame, Json));

        await _sendGate.WaitAsync(ct);
        try
        {
            var socket = _socket;
            if (socket is null || socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("websocket is not open");
            }
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task CloseSocketAsync()
    {
        var socket = _socket;
        _socket = null;
        if (socket is null)
        {
            return;
        }

        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client closing", CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // The socket is going away regardless.
        }
        finally
        {
            socket.Dispose();
        }
    }

    // --- typing indicators ---

    /// <summary>Sends a typing signal, throttled to one per two seconds.</summary>
    public async Task ReportTypingAsync(long conversationId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        if (_typingTimers.TryGetValue(conversationId, out var existing))
        {
            if (existing.Token == default && now - _lastTypingAt < TimeSpan.FromSeconds(2))
            {
                return;
            }
        }

        _lastTypingAt = now;
        _typingTimers.Remove(conversationId);

        if (Status != RealtimeStatus.Connected)
        {
            return;
        }

        try
        {
            await SendFrameAsync(new
            {
                type = "typing",
                payload = new { conversationId, typing = true },
            }, ct);
        }
        catch (Exception)
        {
            // Typing is best-effort.
        }

        // Auto-reset so the peer does not keep showing the indicator forever.
        var cts = new CancellationTokenSource();
        _typingTimers[conversationId] = cts;
        _ = ResetTypingLaterAsync(conversationId, cts);
    }

    private DateTimeOffset _lastTypingAt = DateTimeOffset.MinValue;

    private async Task ResetTypingLaterAsync(long conversationId, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4), cts.Token);
            if (Status == RealtimeStatus.Connected)
            {
                await SendFrameAsync(new
                {
                    type = "typing",
                    payload = new { conversationId, typing = false },
                }, CancellationToken.None);
            }
        }
        catch (Exception)
        {
            // Nothing to do; the indicator is ephemeral.
        }
        finally
        {
            cts.Dispose();
            _typingTimers.Remove(conversationId);
        }
    }

    // --- conversation navigation ---

    /// <summary>Opens a thread, loading its history the first time.</summary>
    public async Task OpenConversationAsync(long conversationId, CancellationToken ct = default)
    {
        if (ActiveConversationId == conversationId && HistoryFor(conversationId).Count > 0)
        {
            await MarkActiveReadAsync();
            return;
        }

        _session.RememberConversation(conversationId);

        OnUi(() =>
        {
            ActiveConversationId = conversationId;
            PeerTypingIn = null;
            HasMoreHistory = false;
            Notify();
        });

        if (!_historyLoaded.Contains(conversationId))
        {
            try
            {
                // Seed with the newest page so the view opens at the bottom.
                var page = await _api.GetMessagesAsync(conversationId, limit: 50, ct: ct);
                OnUi(() =>
                {
                    _history[conversationId] = page.Messages;
                    _historyLoaded.Add(conversationId);
                    HasMoreHistory = page.HasMore;
                    Notify();
                });
            }
            catch (ApiException)
            {
                OnUi(() => _history[conversationId] = []);
                throw;
            }
        }

        await MarkActiveReadAsync();
    }

    /// <summary>Loads one older page and prepends it to the open conversation.</summary>
    public async Task LoadOlderMessagesAsync(CancellationToken ct = default)
    {
        var list = HistoryFor(ActiveConversationId);
        if (list.Count == 0 || !HasMoreHistory)
        {
            return;
        }

        var page = await _api.GetMessagesAsync(ActiveConversationId, before: list[0].Id, ct: ct);
        if (page.Messages.Count == 0)
        {
            HasMoreHistory = false;
            Notify();
            return;
        }

        OnUi(() =>
        {
            var known = list.Select(m => m.Id).ToHashSet();
            list.InsertRange(0, page.Messages.Where(m => !known.Contains(m.Id)));
            HasMoreHistory = page.HasMore;
            Notify();
        });
    }

    private async Task MarkActiveReadAsync()
    {
        var list = ActiveMessages;
        if (list.Count == 0)
        {
            return;
        }

        var newest = list[^1].Id;
        var conversation = ActiveConversation;
        if (conversation is not null && conversation.LastReadMessageId >= newest)
        {
            return;
        }

        try
        {
            if (Status == RealtimeStatus.Connected)
            {
                await SendFrameAsync(new
                {
                    type = "read_receipt",
                    payload = new { conversationId = ActiveConversationId, messageId = newest },
                }, CancellationToken.None);
            }
            else
            {
                await _api.MarkReadAsync(ActiveConversationId, newest);
            }

            if (conversation is not null)
            {
                OnUi(() =>
                {
                    conversation.UnreadCount = 0;
                    conversation.LastReadMessageId = newest;
                    Notify();
                });
            }
        }
        catch (Exception)
        {
            // A missed read receipt only affects the badge until the next sync.
        }
    }

    // --- refreshing ---

    public async Task RefreshContactsAsync(CancellationToken ct = default)
    {
        try
        {
            var contacts = await _api.GetContactsAsync(ct);
            OnUi(() =>
            {
                Contacts.Clear();
                foreach (var contact in contacts)
                {
                    Contacts.Add(contact);
                }
                Notify();
            });
        }
        catch (ApiException ex) when (ex.IsAuthFailure)
        {
            _session.Clear();
        }
    }

    public async Task RefreshConversationsAsync(CancellationToken ct = default)
    {
        var conversations = await _api.GetConversationsAsync(ct);

        OnUi(() =>
        {
            // Preserve the open conversation id across the swap.
            var previouslyActive = ActiveConversationId;

            Conversations.Clear();
            foreach (var conversation in conversations)
            {
                if (conversation.LastMessage is not null && !_history.ContainsKey(conversation.Id))
                {
                    _history[conversation.Id] = [conversation.LastMessage];
                }
                Conversations.Add(conversation);
            }

            if (previouslyActive != 0 && Conversations.All(c => c.Id != previouslyActive))
            {
                ActiveConversationId = 0;
            }

            Notify();
        });
    }

    public async Task<Conversation> StartConversationAsync(long contactId, CancellationToken ct = default)
    {
        var opened = await _api.OpenConversationAsync(contactId, ct);

        if (!_history.TryGetValue(opened.ConversationId, out var list) || list.Count == 0)
        {
            _history[opened.ConversationId] = opened.Messages;
        }

        await RefreshConversationsAsync(ct);
        await OpenConversationAsync(opened.ConversationId, ct);

        return Conversations.FirstOrDefault(c => c.Id == opened.ConversationId)
               ?? new Conversation { Id = opened.ConversationId };
    }

    public Task AddContactAsync(string username, CancellationToken ct = default)
        => _api.AddContactAsync(username, ct);

    public async Task RemoveContactAsync(long userId, CancellationToken ct = default)
    {
        await _api.RemoveContactAsync(userId, ct);
        await RefreshContactsAsync(ct);
    }

    public async Task UploadAvatarAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var user = await _api.UploadAvatarAsync(content, fileName, contentType, ct);
        await _session.SaveAsync(_session.Token, user);
        await RefreshContactsAsync(ct);
        Notify();
    }

    // --- helpers ---

    private void ApplyPresence(PresencePayload? presence)
    {
        if (presence is null)
        {
            return;
        }

        OnUi(() =>
        {
            foreach (var contact in Contacts)
            {
                if (contact.Id == presence.UserId)
                {
                    contact.Online = presence.Online;
                    contact.LastSeenAt = presence.LastSeenAt;
                }
            }

            var peer = Conversations.FirstOrDefault(c => c.Peer.Id == presence.UserId)?.Peer;
            if (peer is not null)
            {
                peer.LastSeenAt = presence.LastSeenAt;
            }

            Notify();
        });
    }

    /// <summary>Keeps the conversation list preview in step with new messages.</summary>
    private void TouchConversation(ChatMessage message)
    {
        var conversation = Conversations.FirstOrDefault(c => c.Id == message.ConversationId);
        if (conversation is null)
        {
            // A message for a thread we have not listed yet: pull the list.
            _ = RefreshConversationsAsync();
            return;
        }

        OnUi(() =>
        {
            conversation.LastMessage = message;

            if (message.ConversationId == ActiveConversationId)
            {
                _ = MarkActiveReadAsync();
            }
            else if (message.SenderId != Me?.Id)
            {
                conversation.UnreadCount += 1;
            }

            Notify();
        });
    }

    private List<ChatMessage> HistoryFor(long conversationId)
    {
        if (!_history.TryGetValue(conversationId, out var list))
        {
            list = [];
            _history[conversationId] = list;
        }
        return list;
    }

    private void SetStatus(RealtimeStatus status, string? detail)
    {
        Status = status;
        StatusDetail = detail;
        _log.Info($"realtime status: {status}{(detail is null ? "" : " (" + detail + ")")}");
        Notify();
    }

    private void Notify() => OnUi(() => Changed?.Invoke());

    /// <summary>
    /// Runs an action on the UI thread, or immediately when already there.
    /// Frames arrive on a socket thread, and mutating the observable collections
    /// from there would race with rendering.
    /// </summary>
    private static void OnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime?.Cancel();
        if (_pump is not null)
        {
            try
            {
                await _pump.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception)
            {
                // Shutdown races are not interesting.
            }
        }
        await StopAsync();
        _lifetime?.Dispose();
    }
}
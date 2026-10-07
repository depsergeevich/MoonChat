namespace MoonChat.Client.Models;

using System.Text.Json.Serialization;

// Client-side mirror of the server's wire package. Field names match the JSON
// contract exactly so System.Text.Json can bind them without attributes, with
// JsonPropertyName only where the C# name had to diverge.

public sealed class User : Observable
{
    private string _displayName = "";
    private string _avatarUrl = "";
    private bool _online;
    private long _lastSeenAt;

    public long Id { get; set; }
    public string Username { get; set; } = "";

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (SetProperty(ref _displayName, value))
            {
                Raise(nameof(Initial));
            }
        }
    }

    public string AvatarUrl
    {
        get => _avatarUrl;
        set => SetProperty(ref _avatarUrl, value);
    }

    /// <summary>Maintained from presence frames; the REST payloads omit it.</summary>
    public bool Online
    {
        get => _online;
        set
        {
            if (SetProperty(ref _online, value))
            {
                Raise(nameof(PresenceText));
            }
        }
    }

    public long LastSeenAt
    {
        get => _lastSeenAt;
        set
        {
            if (SetProperty(ref _lastSeenAt, value))
            {
                Raise(nameof(PresenceText));
            }
        }
    }

    public string Initial => DisplayName.Length > 0 ? DisplayName[..1].ToUpperInvariant() : "?";

    /// <summary>"Online", or when they were last around, or nothing.</summary>
    public string PresenceText => Online
        ? "Online"
        : DescribeLastSeen(LastSeenAt);

    internal static string DescribeLastSeen(long lastSeenAt)
    {
        var ago = RelativeTime.Describe(lastSeenAt);
        return string.IsNullOrEmpty(ago) ? "" : "Last seen " + ago;
    }
}

public sealed class Contact : Observable
{
    private string _displayName = "";
    private string _avatarUrl = "";
    private bool _online;
    private long _lastSeenAt;

    public long Id { get; set; }
    public string Username { get; set; } = "";

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string AvatarUrl
    {
        get => _avatarUrl;
        set => SetProperty(ref _avatarUrl, value);
    }

    public long LastSeenAt
    {
        get => _lastSeenAt;
        set
        {
            if (SetProperty(ref _lastSeenAt, value))
            {
                Raise(nameof(PresenceText), nameof(Subtitle));
            }
        }
    }

    public bool Online
    {
        get => _online;
        set
        {
            if (SetProperty(ref _online, value))
            {
                Raise(nameof(PresenceText), nameof(Subtitle));
            }
        }
    }

    public string PresenceText => Online ? "Online" : User.DescribeLastSeen(LastSeenAt);

    /// <summary>
    /// Second line of a contact row: the handle, plus presence when there is
    /// something to say. Built here because the view should not have to join
    /// two optional strings with a separator.
    /// </summary>
    public string Subtitle
    {
        get
        {
            var handle = string.IsNullOrEmpty(Username) ? "" : "@" + Username;
            if (string.IsNullOrEmpty(PresenceText))
            {
                return handle;
            }
            return string.IsNullOrEmpty(handle) ? PresenceText : $"{handle} · {PresenceText}";
        }
    }

    public User ToUser() => new()
    {
        Id = Id,
        Username = Username,
        DisplayName = DisplayName,
        AvatarUrl = AvatarUrl,
        LastSeenAt = LastSeenAt,
        Online = Online,
    };
}

// Named ChatMessage rather than Message because System.Windows.Forms also
// defines a Message type and WinForms implicit usings would make every
// reference ambiguous.
public sealed class ChatMessage
{
    public long Id { get; set; }
    public long ConversationId { get; set; }
    public long SenderId { get; set; }
    public string SenderUsername { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string SenderAvatarUrl { get; set; } = "";
    public string Body { get; set; } = "";
    public long CreatedAt { get; set; }

    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt);
}

public sealed class Conversation
{
    public long Id { get; set; }
    public User Peer { get; set; } = new();
    public ChatMessage? LastMessage { get; set; }
    public long UnreadCount { get; set; }
    public long LastReadMessageId { get; set; }
}

public sealed class MessagePage
{
    public List<ChatMessage> Messages { get; set; } = [];
    public bool HasMore { get; set; }
}

public sealed class AuthResponse
{
    public string Token { get; set; } = "";
    public User User { get; set; } = new();
}

public sealed class OpenConversationResponse
{
    public long ConversationId { get; set; }
    public List<ChatMessage> Messages { get; set; } = [];
}

/// <summary>One realtime frame, matching the server's envelope.</summary>
public sealed class Envelope
{
    public string Type { get; set; } = "";
    public string? Id { get; set; }
    public System.Text.Json.JsonElement? Payload { get; set; }
}

// Frame payload types.
public sealed class HelloPayload
{
    public long UserId { get; set; }
    public string Username { get; set; } = "";
    public long ServerTime { get; set; }
}

public sealed class MessageNewPayload
{
    [JsonPropertyName("message")]
    public ChatMessage ChatMessage { get; set; } = new();
}

public sealed class AckPayload
{
    public string ClientMessageId { get; set; } = "";

    [JsonPropertyName("message")]
    public ChatMessage ChatMessage { get; set; } = new();
}

public sealed class PresencePayload
{
    public long UserId { get; set; }
    public bool Online { get; set; }
    public long LastSeenAt { get; set; }
}

public sealed class TypingPayload
{
    public long ConversationId { get; set; }
    public bool Typing { get; set; }
}

public sealed class TypingEventPayload
{
    public long ConversationId { get; set; }
    public long UserId { get; set; }
    public bool Typing { get; set; }
}

/// <summary>
/// A read receipt. Sent by the client as { conversationId, messageId }; the
/// server echoes it back to the other members with <see cref="UserId"/> filled
/// in, which is what makes a "Seen" marker possible.
/// </summary>
public sealed class ReadReceiptPayload
{
    public long ConversationId { get; set; }
    public long MessageId { get; set; }
    public long UserId { get; set; }
}

public sealed class TokenRefreshPayload
{
    public string Token { get; set; } = "";
}

public sealed class ApiErrorBody
{
    public ApiError? Error { get; set; }
}

public sealed class ApiError
{
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
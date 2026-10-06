namespace MoonChat.Client.Models;

using System.Text.Json.Serialization;

// Client-side mirror of the server's wire package. Field names match the JSON
// contract exactly so System.Text.Json can bind them without attributes, with
// JsonPropertyName only where the C# name had to diverge.

public sealed class User
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public long LastSeenAt { get; set; }

    public string Initial => DisplayName.Length > 0 ? DisplayName[..1].ToUpperInvariant() : "?";
}

public sealed class Contact
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public long LastSeenAt { get; set; }
    public bool Online { get; set; }

    public User ToUser() => new()
    {
        Id = Id,
        Username = Username,
        DisplayName = DisplayName,
        AvatarUrl = AvatarUrl,
        LastSeenAt = LastSeenAt,
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

public sealed class ReadReceiptPayload
{
    public long ConversationId { get; set; }
    public long MessageId { get; set; }
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
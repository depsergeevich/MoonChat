using System.Text.Json;
using MoonChat.Client.Models;

namespace MoonChat.Client.Tests;

/// <summary>
/// The DTOs are the client's mirror of the Go server's wire contract. They bind
/// without attributes except where a C# name diverges, so these tests use
/// realistic camelCase payloads and assert the wire shape actually round-trips.
/// The options mirror ApiClient/ChatState (JsonSerializerDefaults.Web).
/// </summary>
public class ContractsTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string UserJson = """
        {"id":42,"username":"ada","displayName":"Ada Lovelace","avatarUrl":"/avatars/42.png","lastSeenAt":1700000000000}
        """;

    // --- User ---

    [Fact]
    public void User_binds_a_full_payload()
    {
        var user = JsonSerializer.Deserialize<User>(UserJson, Web)!;

        Assert.Equal(42, user.Id);
        Assert.Equal("ada", user.Username);
        Assert.Equal("Ada Lovelace", user.DisplayName);
        Assert.Equal("/avatars/42.png", user.AvatarUrl);
        Assert.Equal(1_700_000_000_000, user.LastSeenAt);
    }

    [Fact]
    public void User_defaults_missing_fields_to_empty_strings()
    {
        var user = JsonSerializer.Deserialize<User>("""{"id":1}""", Web)!;

        Assert.Equal("", user.Username);
        Assert.Equal("", user.DisplayName);
        Assert.Equal("", user.AvatarUrl);
        Assert.Equal(0, user.LastSeenAt);
    }

    [Theory]
    [InlineData("Ada", "A")]
    [InlineData("a", "A")]
    [InlineData("ün", "Ü")]
    [InlineData("", "?")]
    public void User_Initial_uses_the_first_letter_of_the_display_name(string displayName, string expected)
    {
        var user = new User { DisplayName = displayName };

        Assert.Equal(expected, user.Initial);
    }

    // --- Contact ---

    [Fact]
    public void Contact_binds_the_presence_flag()
    {
        var contact = JsonSerializer.Deserialize<Contact>(
            """{"id":7,"username":"grace","displayName":"Grace Hopper","avatarUrl":"/a/7.png","lastSeenAt":5,"online":true}""",
            Web)!;

        Assert.Equal(7, contact.Id);
        Assert.Equal("grace", contact.Username);
        Assert.True(contact.Online);
        Assert.Equal(5, contact.LastSeenAt);
    }

    [Fact]
    public void Contact_defaults_online_to_false()
    {
        var contact = JsonSerializer.Deserialize<Contact>("""{"id":7,"username":"grace"}""", Web)!;

        Assert.False(contact.Online);
    }

    [Fact]
    public void Contact_Online_defaults_to_false_when_the_field_is_absent()
    {
        // Server omits `online` when it is false, so an absent flag must mean
        // "offline" and not "unknown".
        var contact = JsonSerializer.Deserialize<Contact>(
            """{"id":7,"username":"grace","displayName":"Grace"}""", Web)!;

        Assert.False(contact.Online);
    }

    [Fact]
    public void Contact_ToUser_carries_the_profile_across()
    {
        var contact = new Contact
        {
            Id = 7,
            Username = "grace",
            DisplayName = "Grace Hopper",
            AvatarUrl = "/a/7.png",
            LastSeenAt = 5,
            Online = true,
        };

        var user = contact.ToUser();

        Assert.Equal(7, user.Id);
        Assert.Equal("grace", user.Username);
        Assert.Equal("Grace Hopper", user.DisplayName);
        Assert.Equal("/a/7.png", user.AvatarUrl);
        Assert.Equal(5, user.LastSeenAt);
    }

    // --- ChatMessage ---

    [Fact]
    public void ChatMessage_binds_a_realistic_payload()
    {
        var message = JsonSerializer.Deserialize<ChatMessage>("""
            {"id":9001,"conversationId":5,"senderId":42,"senderUsername":"ada","senderName":"Ada Lovelace",
             "senderAvatarUrl":"/avatars/42.png","body":"hello there","createdAt":1700000000123}
            """, Web)!;

        Assert.Equal(9001, message.Id);
        Assert.Equal(5, message.ConversationId);
        Assert.Equal(42, message.SenderId);
        Assert.Equal("ada", message.SenderUsername);
        Assert.Equal("Ada Lovelace", message.SenderName);
        Assert.Equal("/avatars/42.png", message.SenderAvatarUrl);
        Assert.Equal("hello there", message.Body);
        Assert.Equal(1_700_000_000_123, message.CreatedAt);
    }

    [Fact]
    public void ChatMessage_body_preserves_whitespace_and_unicode()
    {
        var json = JsonSerializer.Serialize(new ChatMessage { Body = "  padded émoji 🎉\n" }, Web);

        var round = JsonSerializer.Deserialize<ChatMessage>(json, Web)!;

        Assert.Equal("  padded émoji 🎉\n", round.Body);
    }

    [Fact]
    public void ChatMessage_body_binds_an_empty_string()
    {
        var message = JsonSerializer.Deserialize<ChatMessage>("""{"id":1,"body":""}""", Web)!;

        Assert.Equal("", message.Body);
    }

    [Fact]
    public void ChatMessage_binds_a_missing_body_as_empty()
    {
        var message = JsonSerializer.Deserialize<ChatMessage>("""{"id":1}""", Web)!;

        Assert.Equal("", message.Body);
    }

    /// <summary>
    /// Genuine gap: the property is declared as a non-nullable string with a ""
    /// default, but an explicit JSON null overwrites that default and binds as
    /// null. A missing key is safe (""); a null value is not, and
    /// MessageItem.Body forwards it straight to the view.
    /// </summary>
    [Fact]
    public void ChatMessage_body_binds_an_explicit_json_null_as_null()
    {
        var message = JsonSerializer.Deserialize<ChatMessage>("""{"id":1,"body":null}""", Web)!;

        // Looks wrong: the annotation promises non-null, so this should either
        // stay "" or throw at the boundary rather than leak null into the view.
        Assert.Null(message.Body);
    }

    [Fact]
    public void ChatMessage_Timestamp_converts_the_unix_millis()
    {
        var message = JsonSerializer.Deserialize<ChatMessage>("""{"id":1,"createdAt":1700000000123}""", Web)!;

        var expected = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123);

        Assert.Equal(expected.ToUnixTimeMilliseconds(), message.Timestamp.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void ChatMessage_Timestamp_handles_the_unix_epoch()
    {
        var message = new ChatMessage { CreatedAt = 0 };

        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds(0).ToUnixTimeMilliseconds(),
            message.Timestamp.ToUnixTimeMilliseconds());
    }

    // --- Conversation / MessagePage ---

    [Fact]
    public void Conversation_binds_nested_peer_and_last_message()
    {
        var conversation = JsonSerializer.Deserialize<Conversation>("""
            {"id":5,"peer":{"id":42,"username":"ada","displayName":"Ada Lovelace"},
             "lastMessage":{"id":9001,"conversationId":5,"senderId":42,"body":"hi","createdAt":1700000000000},
             "unreadCount":3,"lastReadMessageId":8900}
            """, Web)!;

        Assert.Equal(5, conversation.Id);
        Assert.Equal(42, conversation.Peer.Id);
        Assert.Equal("Ada Lovelace", conversation.Peer.DisplayName);
        Assert.NotNull(conversation.LastMessage);
        Assert.Equal("hi", conversation.LastMessage!.Body);
        Assert.Equal(3, conversation.UnreadCount);
        Assert.Equal(8900, conversation.LastReadMessageId);
    }

    [Fact]
    public void Conversation_binds_a_null_last_message()
    {
        var conversation = JsonSerializer.Deserialize<Conversation>("""{"id":5,"peer":{"id":1},"lastMessage":null}""", Web)!;

        Assert.Null(conversation.LastMessage);
    }

    [Fact]
    public void Conversation_without_a_peer_gets_an_empty_one()
    {
        // Peer is a non-nullable property with a default, so the converter list
        // never hits a null reference on a malformed payload.
        var conversation = JsonSerializer.Deserialize<Conversation>("""{"id":5}""", Web)!;

        Assert.NotNull(conversation.Peer);
        Assert.Equal("", conversation.Peer.Username);
    }

    [Fact]
    public void Conversation_unread_count_binds_a_large_value()
    {
        var conversation = JsonSerializer.Deserialize<Conversation>("""{"id":5,"unreadCount":9007199254740993}""", Web)!;

        // long, not int: a badge count must not overflow at a million messages.
        Assert.Equal(9_007_199_254_740_993L, conversation.UnreadCount);
    }

    [Fact]
    public void MessagePage_binds_messages_and_hasMore()
    {
        var page = JsonSerializer.Deserialize<MessagePage>("""
            {"messages":[{"id":2,"body":"second"},{"id":1,"body":"first"}],"hasMore":true}
            """, Web)!;

        Assert.Equal(2, page.Messages.Count);
        Assert.Equal("second", page.Messages[0].Body);
        Assert.True(page.HasMore);
    }

    [Fact]
    public void MessagePage_defaults_to_an_empty_list_and_hasMore_false()
    {
        var page = JsonSerializer.Deserialize<MessagePage>("{}", Web)!;

        Assert.Empty(page.Messages);
        Assert.False(page.HasMore);
    }

    // --- auth ---

    [Fact]
    public void AuthResponse_binds_a_login_payload()
    {
        var auth = JsonSerializer.Deserialize<AuthResponse>(
            $$"""{"token":"jwt-abc","user":{{UserJson}}}""", Web)!;

        Assert.Equal("jwt-abc", auth.Token);
        Assert.Equal("ada", auth.User.Username);
    }

    [Fact]
    public void AuthResponse_defaults_to_an_empty_token()
    {
        var auth = JsonSerializer.Deserialize<AuthResponse>("{}", Web)!;

        Assert.Equal("", auth.Token);
        Assert.NotNull(auth.User);
    }

    // --- realtime envelope ---

    [Fact]
    public void Envelope_binds_type_id_and_payload()
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(
            """{"type":"message_new","id":"req-1","payload":{"message":{"id":1,"body":"hi"}}}""", Web)!;

        Assert.Equal("message_new", envelope.Type);
        Assert.Equal("req-1", envelope.Id);
        Assert.Equal(JsonValueKind.Object, envelope.Payload?.ValueKind);
    }

    [Fact]
    public void Envelope_binds_a_missing_id_as_null()
    {
        // Server-initiated frames carry no correlation id.
        var envelope = JsonSerializer.Deserialize<Envelope>("""{"type":"presence","payload":{}}""", Web)!;

        Assert.Null(envelope.Id);
    }

    [Fact]
    public void Envelope_binds_a_missing_payload_as_null()
    {
        var envelope = JsonSerializer.Deserialize<Envelope>("""{"type":"pong"}""", Web)!;

        Assert.Null(envelope.Payload);
    }

    [Fact]
    public void MessageNewPayload_reads_the_nested_message()
    {
        // ChatState deserialises every frame through MessageNewPayload, so the
        // "message" key must bind to the ChatMessage property.
        var envelope = JsonSerializer.Deserialize<Envelope>(
            """{"type":"message_new","payload":{"message":{"id":9001,"conversationId":5,"senderId":42,"body":"hello","createdAt":1700000000000}}}""",
            Web)!;

        var payload = envelope.Payload!.Value.Deserialize<MessageNewPayload>(Web)!;

        Assert.Equal(9001, payload.ChatMessage.Id);
        Assert.Equal("hello", payload.ChatMessage.Body);
    }

    /// <summary>
    /// Documents why the <c>[JsonPropertyName("message")]</c> attributes exist:
    /// the server key is "message", so without them the nested object would
    /// silently bind to a default-constructed ChatMessage.
    /// </summary>
    [Fact]
    public void MessageNewPayload_ignores_an_unannotated_chatMessage_key()
    {
        // "chatMessage" (the camelCased C# property name) is NOT the wire key.
        var payload = JsonSerializer.Deserialize<MessageNewPayload>(
            """{"chatMessage":{"id":9001,"body":"hello"}}""", Web)!;

        Assert.Equal(0, payload.ChatMessage.Id);
        Assert.Equal("", payload.ChatMessage.Body);
    }

    [Fact]
    public void AckPayload_binds_the_client_id_and_message()
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(
            """{"type":"ack","payload":{"clientMessageId":"abc123","message":{"id":9002,"conversationId":5,"senderId":42,"body":"sent","createdAt":1700000000001}}}""",
            Web)!;

        var ack = envelope.Payload!.Value.Deserialize<AckPayload>(Web)!;

        Assert.Equal("abc123", ack.ClientMessageId);
        Assert.Equal(9002, ack.ChatMessage.Id);
        Assert.Equal("sent", ack.ChatMessage.Body);
    }

    [Fact]
    public void PresencePayload_binds_online_and_lastSeen()
    {
        var presence = JsonSerializer.Deserialize<PresencePayload>(
            """{"userId":42,"online":true,"lastSeenAt":1700000000000}""", Web)!;

        Assert.Equal(42, presence.UserId);
        Assert.True(presence.Online);
        Assert.Equal(1_700_000_000_000, presence.LastSeenAt);
    }

    [Fact]
    public void PresencePayload_binds_online_false()
    {
        var presence = JsonSerializer.Deserialize<PresencePayload>(
            """{"userId":42,"online":false,"lastSeenAt":1700000000000}""", Web)!;

        Assert.False(presence.Online);
    }

    [Fact]
    public void TypingEventPayload_binds_the_conversation_and_user()
    {
        var typing = JsonSerializer.Deserialize<TypingEventPayload>(
            """{"conversationId":5,"userId":42,"typing":true}""", Web)!;

        Assert.Equal(5, typing.ConversationId);
        Assert.Equal(42, typing.UserId);
        Assert.True(typing.Typing);
    }

    [Fact]
    public void ReadReceiptPayload_binds_conversation_and_message()
    {
        var receipt = JsonSerializer.Deserialize<ReadReceiptPayload>(
            """{"conversationId":5,"messageId":9001}""", Web)!;

        Assert.Equal(5, receipt.ConversationId);
        Assert.Equal(9001, receipt.MessageId);
    }

    [Fact]
    public void TokenRefreshPayload_binds_the_new_token()
    {
        var refreshed = JsonSerializer.Deserialize<TokenRefreshPayload>("""{"token":"jwt-new"}""", Web)!;

        Assert.Equal("jwt-new", refreshed.Token);
    }

    [Fact]
    public void HelloPayload_binds_user_and_server_time()
    {
        var hello = JsonSerializer.Deserialize<HelloPayload>(
            """{"userId":42,"username":"ada","serverTime":1700000000000}""", Web)!;

        Assert.Equal(42, hello.UserId);
        Assert.Equal("ada", hello.Username);
        Assert.Equal(1_700_000_000_000, hello.ServerTime);
    }

    [Fact]
    public void OpenConversationResponse_binds_messages()
    {
        var opened = JsonSerializer.Deserialize<OpenConversationResponse>(
            """{"conversationId":5,"messages":[{"id":1,"body":"a"},{"id":2,"body":"b"}]}""", Web)!;

        Assert.Equal(5, opened.ConversationId);
        Assert.Equal(2, opened.Messages.Count);
    }

    // --- errors ---

    [Fact]
    public void ApiErrorBody_binds_the_nested_error()
    {
        var body = JsonSerializer.Deserialize<ApiErrorBody>(
            """{"error":{"code":"unauthorized","message":"token expired"}}""", Web)!;

        Assert.NotNull(body.Error);
        Assert.Equal("unauthorized", body.Error!.Code);
        Assert.Equal("token expired", body.Error.Message);
    }

    [Fact]
    public void ApiErrorBody_binds_a_null_error()
    {
        var body = JsonSerializer.Deserialize<ApiErrorBody>("""{"error":null}""", Web)!;

        Assert.Null(body.Error);
    }

    [Fact]
    public void ApiError_binds_an_empty_body()
    {
        var error = JsonSerializer.Deserialize<ApiErrorBody>("{}", Web)!;

        Assert.Null(error.Error);
    }

    [Fact]
    public void Contact_list_binds_from_a_json_array()
    {
        var contacts = JsonSerializer.Deserialize<List<Contact>>(
            """[{"id":1,"username":"a"},{"id":2,"username":"b","online":true}]""", Web)!;

        Assert.Equal(2, contacts.Count);
        Assert.False(contacts[0].Online);
        Assert.True(contacts[1].Online);
    }

    [Fact]
    public void a_wide_range_of_ids_survive_a_round_trip()
    {
        // Ids are longs; anything above int.MaxValue must not be truncated.
        var json = JsonSerializer.Serialize(
            new ChatMessage { Id = 4_294_967_296, ConversationId = 9_007_199_254_740_993 },
            Web);

        var round = JsonSerializer.Deserialize<ChatMessage>(json, Web)!;

        Assert.Equal(4_294_967_296L, round.Id);
        Assert.Equal(9_007_199_254_740_993L, round.ConversationId);
    }
}
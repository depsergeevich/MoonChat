using MoonChat.Client.Models;

namespace MoonChat.Client.ViewModels;

/// <summary>
/// A single row in the message list. Wraps the persisted message with the
/// presentation facts the view needs, so the XAML stays free of comparisons.
/// </summary>
public sealed class MessageItem : ViewModelBase
{
    public MessageItem(ChatMessage message, long myUserId, ChatMessage? previous)
    {
        Message = message;
        IsMine = message.SenderId == myUserId;

        // Consecutive messages from one sender within a few minutes share an
        // avatar and name header.
        var grouped = previous is not null
            && previous.SenderId == message.SenderId
            && message.Timestamp - previous.Timestamp < TimeSpan.FromMinutes(2);

        ShowHeader = !IsMine && !grouped;
    }

    public ChatMessage Message { get; }

    public bool IsMine { get; }

    public bool ShowHeader { get; }

    public string Body => Message.Body;

    public string Time => Message.Timestamp.ToLocalTime().ToString("HH:mm");

    public string SenderName => Message.SenderName;

    public string AvatarUrl => Message.SenderAvatarUrl;

    public long SenderId => Message.SenderId;
}

/// <summary>An outgoing message that the server has not acknowledged yet.</summary>
public sealed class PendingItem : ViewModelBase
{
    public PendingItem(string body) => Body = body;

    public bool IsMine => true;

    public bool ShowHeader => false;

    public string Body { get; }

    public string Time => "sending…";
}
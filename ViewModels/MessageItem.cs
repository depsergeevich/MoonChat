using Avalonia.Layout;
using MoonChat.Client.Models;

namespace MoonChat.Client.ViewModels;

/// <summary>
/// A single row in the message list. Wraps the persisted message with the
/// presentation facts the view needs, so the XAML stays free of comparisons.
/// </summary>
public sealed class MessageItem : ViewModelBase
{
    public MessageItem(ChatMessage message, long myUserId, ChatMessage? previous, bool seen = false)
    {
        Message = message;
        IsMine = message.SenderId == myUserId;
        Seen = seen;

        // Consecutive messages from one sender within a few minutes share an
        // avatar and name header.
        var grouped = previous is not null
            && previous.SenderId == message.SenderId
            && message.Timestamp - previous.Timestamp < TimeSpan.FromMinutes(2);

        ShowHeader = !IsMine && !grouped;
    }

    public ChatMessage Message { get; }

    /// <summary>True for messages the signed-in user sent. Drives the accent colour.</summary>
    public bool IsMine { get; }

    public bool ShowHeader { get; }

    /// <summary>
    /// Set on the newest own message once the peer has read up to it. Off for
    /// everything else: repeating "Seen" down the thread would be noise.
    /// </summary>
    public bool Seen { get; }

    public string Body => Message.Body;

    public string Time => Message.Timestamp.ToLocalTime().ToString("HH:mm");

    /// <summary>
    /// Own messages sit on the right, incoming ones on the left. Exposed as an
    /// alignment so the view needs no triggers.
    /// </summary>
    public HorizontalAlignment BubbleAlignment =>
        IsMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;

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
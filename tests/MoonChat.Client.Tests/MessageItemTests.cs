using Avalonia.Layout;
using MoonChat.Client.Models;
using MoonChat.Client.ViewModels;

namespace MoonChat.Client.Tests;

/// <summary>
/// <see cref="MessageItem"/> decides avatar/name grouping, which side a bubble
/// sits on, and the visible timestamp. All three drive the XAML directly, so
/// regressions here are visible to users but easy to break.
/// </summary>
public class MessageItemTests
{
    private const long MeId = 1;
    private const long PeerId = 2;

    private static readonly DateTimeOffset BaseTime =
        new(2024, 5, 17, 12, 0, 0, TimeSpan.Zero);

    private static ChatMessage Msg(long senderId, long offsetSeconds, long id = 10) => new()
    {
        Id = id,
        ConversationId = 5,
        SenderId = senderId,
        SenderUsername = senderId == MeId ? "me" : "ada",
        SenderName = senderId == MeId ? "Me" : "Ada Lovelace",
        SenderAvatarUrl = $"/avatars/{senderId}.png",
        Body = $"body-{id}",
        CreatedAt = BaseTime.AddSeconds(offsetSeconds).ToUnixTimeMilliseconds(),
    };

    private static MessageItem Item(ChatMessage message, ChatMessage? previous = null)
        => new(message, MeId, previous);

    // --- grouping ---

    [Fact]
    public void first_message_from_a_peer_shows_a_header()
    {
        var message = Msg(PeerId, 0);

        Assert.True(Item(message).ShowHeader);
    }

    [Fact]
    public void same_sender_one_second_apart_hides_the_header()
    {
        var previous = Msg(PeerId, 0, id: 1);
        var current = Msg(PeerId, 1, id: 2);

        Assert.False(Item(current, previous).ShowHeader);
    }

    [Fact]
    public void different_sender_within_the_window_shows_a_header()
    {
        // The gap is under two minutes, but a THIRD participant spoke, so this
        // row must be labelled rather than folded into the previous bubble.
        var previous = Msg(PeerId, 0, id: 1);
        var current = Msg(3, 30, id: 2);

        Assert.True(Item(current, previous).ShowHeader);
    }

    [Fact]
    public void an_incoming_row_after_one_of_mine_is_labelled_but_mine_never_is()
    {
        // The asymmetry the view relies on: ShowHeader is "!IsMine && !grouped",
        // so the peer's message always gets a header and mine never does,
        // whichever way round they arrive.
        var mine = Msg(MeId, 0, id: 1);
        var theirs = Msg(PeerId, 10, id: 2);
        var mineAgain = Msg(MeId, 20, id: 3);

        Assert.True(Item(theirs, mine).ShowHeader);
        Assert.False(Item(mineAgain, theirs).ShowHeader);
    }

    [Fact]
    public void same_sender_just_under_two_minutes_hides_the_header()
    {
        var previous = Msg(PeerId, 0, id: 1);
        var current = Msg(PeerId, 119, id: 2);

        Assert.False(Item(current, previous).ShowHeader);
    }

    [Fact]
    public void same_sender_exactly_two_minutes_later_shows_a_header()
    {
        // The rule is a strict "< 2 minutes", so 120s is the first grouped-out
        // value. This boundary is worth pinning: an off-by-one here changes how
        // every conversation looks.
        var previous = Msg(PeerId, 0, id: 1);
        var current = Msg(PeerId, 120, id: 2);

        Assert.True(Item(current, previous).ShowHeader);
    }

    [Fact]
    public void same_sender_one_millisecond_under_two_minutes_hides_the_header()
    {
        var previous = new ChatMessage
        {
            Id = 1,
            SenderId = PeerId,
            CreatedAt = BaseTime.ToUnixTimeMilliseconds(),
        };
        var current = new ChatMessage
        {
            Id = 2,
            SenderId = PeerId,
            CreatedAt = BaseTime.AddMilliseconds(119_999).ToUnixTimeMilliseconds(),
        };

        Assert.False(Item(current, previous).ShowHeader);
    }

    [Fact]
    public void same_sender_one_millisecond_over_two_minutes_shows_a_header()
    {
        var previous = new ChatMessage
        {
            Id = 1,
            SenderId = PeerId,
            CreatedAt = BaseTime.ToUnixTimeMilliseconds(),
        };
        var current = new ChatMessage
        {
            Id = 2,
            SenderId = PeerId,
            CreatedAt = BaseTime.AddMilliseconds(120_001).ToUnixTimeMilliseconds(),
        };

        Assert.True(Item(current, previous).ShowHeader);
    }

    /// <summary>
    /// Own messages never show a header, regardless of grouping. That is what
    /// keeps the right-hand column clean when you send several in a row.
    /// </summary>
    [Fact]
    public void own_message_never_shows_a_header_even_when_not_grouped()
    {
        var previous = Msg(PeerId, 0, id: 1);
        var current = Msg(MeId, 10_000, id: 2);

        var item = Item(current, previous);

        Assert.True(item.IsMine);
        Assert.False(item.ShowHeader);
    }

    [Fact]
    public void own_message_with_no_previous_never_shows_a_header()
        => Assert.False(Item(Msg(MeId, 0)).ShowHeader);

    // --- IsMine / alignment ---

    [Fact]
    public void own_message_is_mine_and_aligns_right()
    {
        var item = Item(Msg(MeId, 0));

        Assert.True(item.IsMine);
        Assert.Equal(HorizontalAlignment.Right, item.BubbleAlignment);
    }

    [Fact]
    public void peer_message_is_not_mine_and_aligns_left()
    {
        var item = Item(Msg(PeerId, 0));

        Assert.False(item.IsMine);
        Assert.Equal(HorizontalAlignment.Left, item.BubbleAlignment);
    }

    [Fact]
    public void user_id_zero_is_treated_as_mine()
    {
        // Documents a real hazard: an unsigned-in or not-yet-loaded session has
        // myUserId == 0. If the server ever sends SenderId 0 the message renders
        // as the user's own. Looks wrong, but the fix is production code.
        var message = new ChatMessage { Id = 1, SenderId = 0, CreatedAt = BaseTime.ToUnixTimeMilliseconds() };

        var item = new MessageItem(message, myUserId: 0, previous: null);

        Assert.True(item.IsMine);
        Assert.Equal(HorizontalAlignment.Right, item.BubbleAlignment);
    }

    // --- pass-through properties ---

    [Fact]
    public void exposes_the_wrapped_message_fields()
    {
        var message = Msg(PeerId, 0);

        var item = Item(message);

        Assert.Same(message, item.Message);
        Assert.Equal("body-10", item.Body);
        Assert.Equal("Ada Lovelace", item.SenderName);
        Assert.Equal("/avatars/2.png", item.AvatarUrl);
        Assert.Equal(PeerId, item.SenderId);
    }

    [Fact]
    public void empty_body_is_preserved()
    {
        var message = Msg(PeerId, 0);
        message.Body = "";

        Assert.Equal("", Item(message).Body);
    }

    [Fact]
    public void Time_renders_the_local_time_of_day()
    {
        var message = Msg(PeerId, 0);

        var expected = BaseTime.ToLocalTime().ToString("HH:mm");

        Assert.Equal(expected, Item(message).Time);
        Assert.Matches(@"^\d{2}:\d{2}$", Item(message).Time);
    }

    /// <summary>
    /// <see cref="MessageItem.Time"/> and the converters both call
    /// <c>DateTimeOffset.FromUnixTimeMilliseconds</c> with no range guard, so a
    /// malformed CreatedAt throws while building the row rather than degrading.
    /// Documents current behaviour; looks wrong.
    /// </summary>
    [Fact]
    public void absurd_timestamp_throws_when_the_time_is_read()
    {
        var message = new ChatMessage { Id = 1, SenderId = PeerId, CreatedAt = long.MaxValue };

        var item = Item(message);

        Assert.Throws<ArgumentOutOfRangeException>(() => item.Time);
    }

    // --- PendingItem ---

    [Fact]
    public void pending_item_is_always_own_and_unlabelled()
    {
        var pending = new PendingItem("sending text");

        Assert.True(pending.IsMine);
        Assert.False(pending.ShowHeader);
        Assert.Equal("sending text", pending.Body);
    }

    [Fact]
    public void pending_item_shows_the_sending_label()
    {
        // Exact string, because it is user-visible copy.
        Assert.Equal("sending…", new PendingItem("x").Time);
    }

    [Fact]
    public void pending_item_keeps_an_empty_body()
        => Assert.Equal("", new PendingItem("").Body);

    // --- a realistic grouped thread ---

    [Fact]
    public void a_typical_thread_groups_correctly_end_to_end()
    {
        // ada, ada, ada, me, ada -- five consecutive rows, checking the header
        // the view would actually render for each.
        var m1 = Msg(PeerId, 0, id: 1);
        var m2 = Msg(PeerId, 20, id: 2);
        var m3 = Msg(PeerId, 40, id: 3);
        var m4 = Msg(MeId, 60, id: 4);
        var m5 = Msg(PeerId, 80, id: 5);

        var headers = new List<bool>
        {
            Item(m1).ShowHeader,
            Item(m2, m1).ShowHeader,
            Item(m3, m2).ShowHeader,
            Item(m4, m3).ShowHeader,
            Item(m5, m4).ShowHeader,
        };

        Assert.Equal([true, false, false, false, true], headers);
    }

    /// <summary>
    /// Genuine bug: grouping only compares against the immediately previous
    /// message. If history arrives out of order (an older page prepended after a
    /// live frame, or a server clock skew), a message can land on the wrong
    /// side of the two-minute window and wrongly inherit the previous sender's
    /// header. Documents current behaviour; the fix would be in production code.
    /// </summary>
    [Fact]
    public void out_of_order_timestamps_group_incorrectly()
    {
        // previous is NEWER than current by three minutes. The subtraction is
        // negative, and "negative < 2 minutes" is true, so the rows are grouped
        // even though they are three minutes apart.
        var previous = Msg(PeerId, 600, id: 1);
        var current = Msg(PeerId, 420, id: 2);

        Assert.True(Item(current, previous).ShowHeader == false);
    }
}
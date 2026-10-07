using System.Globalization;
using MoonChat.Client.Converters;

namespace MoonChat.Client.Tests;

/// <summary>
/// The converters are pure functions behind <c>IValueConverter</c>, so they can
/// be driven directly without an Avalonia application or a headless platform.
/// </summary>
public class ConverterTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // --- LongToBoolConverter: marks the selected conversation row ---

    [Fact]
    public void LongToBool_matches_ids_returns_true()
        => Assert.Equal(true, LongToBoolConverter.Instance.Convert(42L, typeof(bool), "42", Invariant));

    [Fact]
    public void LongToBool_different_ids_returns_false()
        => Assert.Equal(false, LongToBoolConverter.Instance.Convert(41L, typeof(bool), "42", Invariant));

    [Fact]
    public void LongToBool_unselected_id_zero_returns_false()
        => Assert.Equal(false, LongToBoolConverter.Instance.Convert(0L, typeof(bool), "42", Invariant));

    [Fact]
    public void LongToBool_null_value_returns_false()
        => Assert.Equal(false, LongToBoolConverter.Instance.Convert(null, typeof(bool), "42", Invariant));

    [Fact]
    public void LongToBool_null_parameter_returns_false()
        => Assert.Equal(false, LongToBoolConverter.Instance.Convert(42L, typeof(bool), null, Invariant));

    [Fact]
    public void LongToBool_unparsable_parameter_returns_false()
        => Assert.Equal(false, LongToBoolConverter.Instance.Convert(42L, typeof(bool), "not-a-number", Invariant));

    [Fact]
    public void LongToBool_empty_parameter_returns_false()
        => Assert.Equal(false, LongToBoolConverter.Instance.Convert(42L, typeof(bool), "", Invariant));

    [Fact]
    public void LongToBool_parameter_with_surrounding_whitespace_still_matches()
        => Assert.Equal(true, LongToBoolConverter.Instance.Convert(42L, typeof(bool), " 42 ", Invariant));

    [Fact]
    public void LongToBool_negative_ids_compare_numerically()
        => Assert.Equal(true, LongToBoolConverter.Instance.Convert(-7L, typeof(bool), "-7", Invariant));

    /// <summary>
    /// The converter only accepts <see cref="long"/>. If a binding ever supplies
    /// an <see cref="int"/> (or a double, or a string) the row silently stops
    /// being selectable, because the pattern match fails and returns false.
    /// </summary>
    [Fact]
    public void LongToBool_int_value_does_not_match_even_when_numerically_equal()
    {
        // Documents current behaviour: an int 42 does NOT match parameter "42".
        // This looks wrong — IValueConverter is untyped, so an int can easily
        // reach it (e.g. a literal CommandParameter in XAML, or a value that
        // came back from JSON as int). The fix would be to convert via
        // Convert.ToInt64 instead of pattern-matching on long.
        Assert.Equal(false, LongToBoolConverter.Instance.Convert(42, typeof(bool), "42", Invariant));
    }

    [Fact]
    public void LongToBool_long_max_id_matches_itself()
        => Assert.Equal(true, LongToBoolConverter.Instance.Convert(long.MaxValue, typeof(bool), long.MaxValue.ToString(), Invariant));

    [Fact]
    public void LongToBool_convert_back_throws()
        => Assert.Throws<NotSupportedException>(
            () => LongToBoolConverter.Instance.ConvertBack(true, typeof(long), "42", Invariant));

    // --- PositiveToBoolConverter: shows or hides the unread badge ---

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(int.MaxValue, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(int.MinValue, false)]
    public void PositiveToBool_int_input(object value, bool expected)
        => Assert.Equal(expected, PositiveToBoolConverter.Instance.Convert(value, typeof(bool), null, Invariant));

    [Theory]
    [InlineData(1L, true)]
    [InlineData(long.MaxValue, true)]
    [InlineData(0L, false)]
    [InlineData(-5L, false)]
    public void PositiveToBool_long_input(object value, bool expected)
        => Assert.Equal(expected, PositiveToBoolConverter.Instance.Convert(value, typeof(bool), null, Invariant));

    [Fact]
    public void PositiveToBool_null_returns_false()
        => Assert.Equal(false, PositiveToBoolConverter.Instance.Convert(null, typeof(bool), null, Invariant));

    /// <summary>
    /// Documents a genuine gap: only int and long are handled. The unread count
    /// on the wire is a JSON number that binds as <c>long</c> today, but any
    /// binding that produces a double, decimal, or string would make the badge
    /// disappear. Looks wrong / fragile, but changing it is production code.
    /// </summary>
    [Theory]
    [InlineData(1.5d)]
    [InlineData(1.0d)]
    [InlineData("1")]
    [InlineData(true)]
    public void PositiveToBool_unhandled_numeric_types_return_false(object value)
    {
        // Looks wrong: these are all "greater than zero" but the switch has no
        // arm for them, so the unread badge silently hides.
        Assert.Equal(false, PositiveToBoolConverter.Instance.Convert(value, typeof(bool), null, Invariant));
    }

    [Fact]
    public void PositiveToBool_convert_back_throws()
        => Assert.Throws<NotSupportedException>(
            () => PositiveToBoolConverter.Instance.ConvertBack(true, typeof(int), null, Invariant));

    // --- UnixTimeConverter: formats the bubble timestamp ---

    [Fact]
    public void UnixTime_zero_returns_empty()
        => Assert.Equal("", UnixTimeConverter.Instance.Convert(0L, typeof(string), null, Invariant));

    [Fact]
    public void UnixTime_null_returns_empty()
        => Assert.Equal("", UnixTimeConverter.Instance.Convert(null, typeof(string), null, Invariant));

    [Fact]
    public void UnixTime_non_long_returns_empty()
    {
        // Deliberately an int: the converter pattern-matches on long, so a 32-bit
        // value (which an int literal produces) renders as blank instead of a
        // timestamp. The cast keeps it from widening to long.
        Assert.Equal("", UnixTimeConverter.Instance.Convert((object)1_700_000_000, typeof(string), null, Invariant));
    }

    [Fact]
    public void UnixTime_today_renders_time_only()
    {
        var millis = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var rendered = (string)UnixTimeConverter.Instance.Convert(millis, typeof(string), null, Invariant)!;

        Assert.Matches(@"^\d{2}:\d{2}$", rendered);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(millis).LocalDateTime.ToString("HH:mm"), rendered);
    }

    [Fact]
    public void UnixTime_past_date_renders_day_and_month()
    {
        // 2021-03-04T05:06:07Z — comfortably in the past, so never "today".
        var millis = new DateTimeOffset(2021, 3, 4, 5, 6, 7, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var local = DateTimeOffset.FromUnixTimeMilliseconds(millis).LocalDateTime;
        var rendered = (string)UnixTimeConverter.Instance.Convert(millis, typeof(string), null, Invariant)!;

        Assert.Equal(local.ToString("d MMM"), rendered);
        Assert.DoesNotContain(":", rendered);
    }

    [Fact]
    public void UnixTime_future_date_also_renders_a_date()
    {
        var millis = new DateTimeOffset(2099, 12, 31, 23, 59, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var local = DateTimeOffset.FromUnixTimeMilliseconds(millis).LocalDateTime;
        var rendered = (string)UnixTimeConverter.Instance.Convert(millis, typeof(string), null, Invariant)!;

        Assert.Equal(local.ToString("d MMM"), rendered);
    }

    /// <summary>
    /// A negative timestamp (a clock-skewed server, say) is not zero, so it is
    /// formatted rather than blanked. Documents current behaviour.
    /// </summary>
    [Fact]
    public void UnixTime_negative_timestamp_does_not_throw()
    {
        var rendered = UnixTimeConverter.Instance.Convert(-1000L, typeof(string), null, Invariant);
        Assert.IsType<string>(rendered);
    }

    /// <summary>
    /// Genuine bug: the converter guards <c>millis == 0</c> but not the range,
    /// so <c>DateTimeOffset.FromUnixTimeMilliseconds</c> throws
    /// <see cref="ArgumentOutOfRangeException"/> for an absurd value. Inside
    /// Avalonia a throwing converter takes down the binding (and typically the
    /// window) rather than degrading to a blank. The value comes straight off
    /// the wire in <c>Conversation.LastMessage</c> / the message list, so one
    /// malformed server field is enough to trigger it.
    /// </summary>
    [Fact]
    public void UnixTime_out_of_range_timestamp_throws()
    {
        // Looks wrong: should return a placeholder rather than throwing.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => UnixTimeConverter.Instance.Convert(long.MaxValue, typeof(string), null, Invariant));
    }

    [Fact]
    public void UnixTime_convert_back_throws()
        => Assert.Throws<NotSupportedException>(
            () => UnixTimeConverter.Instance.ConvertBack("12:00", typeof(long), null, Invariant));
}
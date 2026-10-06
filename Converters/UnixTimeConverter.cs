using System.Globalization;
using Avalonia.Data.Converters;

namespace MoonChat.Client.Converters;

/// <summary>Formats Unix milliseconds as a short chat-style timestamp.</summary>
public sealed class UnixTimeConverter : IValueConverter
{
    public static readonly UnixTimeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long millis || millis == 0)
        {
            return string.Empty;
        }

        var time = DateTimeOffset.FromUnixTimeMilliseconds(millis).LocalDateTime;

        // Today shows only the time; anything older gets a date.
        return time.Date == DateTime.Today ? time.ToString("HH:mm") : time.ToString("d MMM");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
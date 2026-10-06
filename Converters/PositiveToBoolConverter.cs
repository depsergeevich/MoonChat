using System.Globalization;
using Avalonia.Data.Converters;

namespace MoonChat.Client.Converters;

/// <summary>
/// True when a number is greater than zero. Used for unread badges, where the
/// value is a count that the view shows or hides whole.
/// </summary>
public sealed class PositiveToBoolConverter : IValueConverter
{
    public static readonly PositiveToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            int i => i > 0,
            long l => l > 0,
            _ => false,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
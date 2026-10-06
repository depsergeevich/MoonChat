using System.Globalization;
using Avalonia.Data.Converters;

namespace MoonChat.Client.Converters;

/// <summary>
/// True when a bound id equals <c>ConverterParameter</c>. Used to mark the
/// selected row without relying on list-selection chrome.
/// </summary>
public sealed class LongToBoolConverter : IValueConverter
{
    public static readonly LongToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long actual || parameter is null)
        {
            return false;
        }

        return long.TryParse(parameter.ToString(), out var expected) && actual == expected;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
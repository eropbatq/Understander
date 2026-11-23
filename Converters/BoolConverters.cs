using System.Globalization;
using Microsoft.Maui.Controls;
using Understander.ViewModels;

namespace Understander.Converters;

/// <summary>
/// Преобразует bool в HorizontalOptions (true → Start, false → End).
/// </summary>
public class BoolToHorizontalOptionConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? LayoutOptions.Start : LayoutOptions.End;
        return LayoutOptions.Start;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Преобразует null → false, non-null → true (для IsVisible).
/// </summary>
public class NullHasConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Преобразует код языка в название.
/// </summary>
public class CodeToNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string code && !string.IsNullOrEmpty(code))
        {
            var langs = new Dictionary<string, string>
            {
                { "ru", "русский" }, { "en", "английский" }, { "de", "немецкий" },
                { "fr", "французский" }, { "es", "испанский" }, { "it", "итальянский" },
                { "tr", "турецкий" }, { "zh", "китайский" }
            };
            if (langs.TryGetValue(code.ToLower(), out var name))
                return name;
        }
        return value?.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

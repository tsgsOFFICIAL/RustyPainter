using Avalonia.Data.Converters;
using System.Globalization;
using System;

namespace RustyPainter.Converters;

/// <summary>
/// Compares a bound enum value against a string <c>ConverterParameter</c> (parsed to the same enum
/// type), for toggling a "active"-style CSS class per item in a list of otherwise-identical buttons
/// that all share one selection value, e.g. <c>Classes.active="{Binding ActivePage,
/// Converter={x:Static converters:EnumEqualsConverter.Instance}, ConverterParameter=Dashboard}"</c>.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null && parameter is string tag
            && Enum.TryParse(value.GetType(), tag, out object? target)
            && value.Equals(target);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
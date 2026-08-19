using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RAPluginManifestEditor.Converters;

public class CountToVisibleConverter : IValueConverter
{
    public static readonly CountToVisibleConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i && i > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

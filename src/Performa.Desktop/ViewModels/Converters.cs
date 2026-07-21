using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Performa.Desktop.ViewModels;

/// <summary>Maps a bool "is clean" to a status colour: green when clean, amber otherwise.</summary>
public sealed class BoolBrush : IValueConverter
{
    public static readonly BoolBrush Instance = new();

    private static readonly Color Clean = Color.Parse("#4FB286");
    private static readonly Color Dirty = Color.Parse("#E0A34E");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Clean : Dirty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

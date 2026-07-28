using System.Globalization;
using Avalonia.Data.Converters;

namespace Performa.Desktop.Infrastructure;

/// <summary>
/// Turns a bar's share of the peak (0..1) into a pixel height.
///
/// The view model deliberately publishes a ratio rather than pixels: the chart's
/// plot height belongs to the view, and a view model that knows about pixels
/// cannot be re-laid-out without editing it.
/// </summary>
public sealed class ShareToHeight : IValueConverter
{
    /// <summary>Plot height in pixels, matching the chart row in DashboardView.</summary>
    public double PlotHeight { get; set; } = 84;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double share ? Math.Max(2, share * PlotHeight) : 2d;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

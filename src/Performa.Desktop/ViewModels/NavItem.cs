using Avalonia;
using Avalonia.Media;
using Performa.Desktop.Infrastructure;

namespace Performa.Desktop.ViewModels;

public sealed class NavItem(string title, string iconKey, ObservableObject page, bool dormant = false)
{
    public string Title { get; } = title;
    public ObservableObject Page { get; } = page;
    public bool Dormant { get; } = dormant;

    public Geometry? Icon { get; } = ResolveIcon(iconKey);

    private static Geometry? ResolveIcon(string key)
        => Application.Current?.Resources.TryGetResource(key, null, out var value) == true
            ? value as Geometry
            : null;
}

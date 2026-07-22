using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Performa.Desktop.ViewModels;
using Performa.Prefs;

namespace Performa.Desktop;

public partial class App : Application
{
    /// <summary>
    /// Single place the theme is applied. Everything visual reaches its brushes
    /// through DynamicResource, so setting the variant re-themes the live window
    /// without a restart.
    /// </summary>
    public static void ApplyTheme(AppTheme theme)
    {
        if (Current is null) return;
        Current.RequestedThemeVariant =
            theme == AppTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    public override void Initialize()
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = new MainViewModel();
            ApplyTheme(vm.Engine.Prefs.Theme);
            desktop.MainWindow = new MainWindow { DataContext = vm };

            // Launched by Windows at boot: come up minimised rather than stealing focus.
            if (desktop.Args?.Contains(Services.StartupService.StartupArgument) == true
                && desktop.MainWindow is { } w)
                w.WindowState = Avalonia.Controls.WindowState.Minimized;
        }

        base.OnFrameworkInitializationCompleted();
    }
}

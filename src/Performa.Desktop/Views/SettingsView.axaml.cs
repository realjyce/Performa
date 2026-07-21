using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Performa.Desktop.ViewModels;

namespace Performa.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => AvaloniaXamlLoader.Load(this);

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null || DataContext is not SettingsViewModel vm) return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose your workspace folder",
            AllowMultiple = false,
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            vm.ApplyWorkspace(path);
    }
}

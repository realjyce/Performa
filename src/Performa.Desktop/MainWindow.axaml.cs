using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Performa.Desktop.ViewModels;

namespace Performa.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var titleBar = this.FindControl<Grid>("TitleBar")!;
        titleBar.PointerPressed += OnTitleBarPressed;

        this.FindControl<Button>("MinBtn")!.Click += (_, _) =>
            WindowState = WindowState.Minimized;
        this.FindControl<Button>("MaxBtn")!.Click += (_, _) =>
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        this.FindControl<Button>("CloseBtn")!.Click += (_, _) => Close();

        // Click anywhere in the console drawer focuses the input, like a terminal.
        this.FindControl<Border>("ConsoleDrawer")!.PointerPressed += (_, _) => FocusConsole();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsConsoleOpen)
            && sender is MainViewModel { IsConsoleOpen: true })
            FocusConsole();
    }

    private void FocusConsole()
        => Dispatcher.UIThread.Post(
            () => this.FindControl<TextBox>("ConsoleInput")?.Focus(),
            DispatcherPriority.Input);

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}

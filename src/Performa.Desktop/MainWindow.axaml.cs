using Avalonia.Controls;
using Avalonia.Input;

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
    }

    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}

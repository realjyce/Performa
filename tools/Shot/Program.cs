using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Performa.Desktop;
using Performa.Desktop.ViewModels;

// Headless screenshot harness for the desktop shell. Renders a page to PNG so
// the UI can be verified visually, the way puppeteer shots verify the web work.

var outFile = args.Length > 0 ? args[0] : "shot.png";
var pageIndex = args.Length > 1 && int.TryParse(args[1], out var pi) ? pi : 0;
var width = args.Length > 2 && int.TryParse(args[2], out var wd) ? wd : 1200;
var height = args.Length > 3 && int.TryParse(args[3], out var ht) ? ht : 780;

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

// "dots" mode: a filmstrip of the thinking mark, so the whole loop can be seen
// as stills instead of trusted. Frame count and spacing come from the loop
// length, and the control drives itself off a real clock, so this waits in real
// time between captures.
if (args.Length > 1 && args[1] == "dots")
{
    var frames = args.Length > 2 ? int.Parse(args[2]) : 16;
    var spacingMs = args.Length > 3 ? int.Parse(args[3]) : 400;

    var dots = new Performa.Desktop.Controls.ThinkingDots
    {
        Width = 120,
        Height = 60,
        IsActive = true,
        DotBrush = Avalonia.Media.Brushes.White,
    };
    var strip = new Window
    {
        Width = 120,
        Height = 60,
        Background = Avalonia.Media.Brushes.Black,
        Content = dots,
        WindowDecorations = WindowDecorations.None,
    };
    strip.Show();

    for (var f = 0; f < frames; f++)
    {
        var until = DateTime.Now.AddMilliseconds(spacingMs);
        while (DateTime.Now < until)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(8);
        }
        // Headless has no render timer driving the control's own invalidation,
        // so the frame is forced here. The geometry under test is unaffected.
        dots.InvalidateVisual();
        Dispatcher.UIThread.RunJobs();
        var shot = strip.CaptureRenderedFrame();
        if (shot is null) continue;
        using var fs = File.Create($"{Path.GetFileNameWithoutExtension(outFile)}-{f:D2}.png");
        shot.Save(fs);
    }
    Console.WriteLine($"saved {frames} frames every {spacingMs}ms");
    return;
}

var vm = new MainViewModel();

// The launch walkthrough sits over everything, which is right for a first run
// and wrong for a page shot. PERFORMA_SHOT_ONBOARDING=1 keeps it for the one
// capture that is meant to show it.
if (Environment.GetEnvironmentVariable("PERFORMA_SHOT_ONBOARDING") != "1")
    vm.ShowOnboarding = false;

// Shot builds its own window, so the theme App would normally apply on startup
// has to be applied here. PERFORMA_SHOT_THEME overrides the stored preference.
App.ApplyTheme(
    Environment.GetEnvironmentVariable("PERFORMA_SHOT_THEME")?.ToLowerInvariant() switch
    {
        "light" => Performa.Prefs.AppTheme.Light,
        "dark" => Performa.Prefs.AppTheme.Dark,
        _ => vm.Engine.Prefs.Theme,
    });

// The first-run overlay scrims the whole window, which hides whatever is being
// checked. Cleared in the view model only, so no name is written to preferences.
vm.NeedsName = false;

// The assistant sits outside both nav lists, so -1 reaches it.
if (pageIndex < 0)
    vm.OpenAssistantCommand.Execute(null);
else
    vm.Selected = vm.NavItems.Concat(vm.UtilityItems).ElementAt(pageIndex);

// Optional: drive the assistant with a question so the shot shows a real answer.
if (args.Length > 4 && vm.Selected.Page is AssistantViewModel assistant)
    assistant.AskSuggestionCommand.Execute(args[4]);

// Optional: fire a dashboard quick action (e.g. "quick:standup") to test navigation.
if (args.Length > 4 && args[4].StartsWith("quick:") && vm.Selected.Page is DashboardViewModel dash)
    dash.QuickCommand.Execute(args[4]["quick:".Length..]);

// Optional: "gh:code" puts Settings into the mid-device-flow state so the code
// panel and the advanced fields can be seen without a real GitHub round trip.
if (args.Length > 4 && args[4] == "gh:code" && vm.Selected.Page is SettingsViewModel settings)
{
    settings.DeviceCode = "F4C2-9K7L";
    settings.ShowGitHubAdvanced = true;
    settings.GitHubNote = "Enter this code at https://github.com/login/device — "
        + "your browser should already be open.";
}

// Optional: open the console and run a command (e.g. "console:standup Performa").
if (args.Length > 4 && args[4].StartsWith("console:"))
{
    vm.IsConsoleOpen = true;
    vm.Console.Input = args[4]["console:".Length..];
    vm.Console.RunCommand.Execute(null);
}

var window = new MainWindow
{
    DataContext = vm,
    Width = width,
    Height = height,
};
window.Show();

// Optional: navigate AFTER show, mimicking a real sidebar click (rebuilds the
// view via the ViewLocator, which the initial render does not exercise).
if (args.Length > 5 && args[5].StartsWith("navto:"))
{
    Dispatcher.UIThread.RunJobs();
    Thread.Sleep(400);
    var target = args[5]["navto:".Length..];
    // Assistant lives on its own button rather than in the nav lists, so it
    // is reached by name through the same command the sidebar uses.
    if (target == "assistant") vm.OpenAssistantCommand.Execute(null);
    else vm.Selected = vm.NavItems.Concat(vm.UtilityItems).ElementAt(int.Parse(target));
}

// Optional: "combo" opens the first dropdown so the popup itself can be checked.
if (args.Length > 4 && args[4] == "combo")
{
    Dispatcher.UIThread.RunJobs();
    Thread.Sleep(600);
    Dispatcher.UIThread.RunJobs();
    var combo = window.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault();
    if (combo is not null) combo.IsDropDownOpen = true;
}

// Pump the UI + let the async git load settle before capturing. A network
// round trip needs far longer than a git read, so PERFORMA_SHOT_WAIT extends it.
var waitSeconds = int.TryParse(
    Environment.GetEnvironmentVariable("PERFORMA_SHOT_WAIT"), out var w) ? w : 8;
var deadline = DateTime.Now.AddSeconds(waitSeconds);
while (DateTime.Now < deadline)
{
    Dispatcher.UIThread.RunJobs();
    Thread.Sleep(120);
}

// Long pages put the interesting card below the fold. PERFORMA_SHOT_SCROLL
// moves the first scroller down by that many pixels before capturing.
if (int.TryParse(Environment.GetEnvironmentVariable("PERFORMA_SHOT_SCROLL"), out var scroll)
    && scroll > 0)
{
    // Tree order finds the sidebar and combo popups first, so pick the one
    // that actually has the most to scroll: that is the page body.
    var viewer = window.GetVisualDescendants().OfType<ScrollViewer>()
        .Where(s => s.Extent.Height > s.Viewport.Height)
        .OrderByDescending(s => s.Extent.Height)
        .FirstOrDefault();
    if (viewer is not null)
    {
        viewer.Offset = viewer.Offset.WithY(Math.Min(scroll, viewer.ScrollBarMaximum.Y));
        for (var i = 0; i < 12; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(120); }
        Console.WriteLine($"scrolled to {viewer.Offset.Y} of {viewer.ScrollBarMaximum.Y}");
    }
    else Console.WriteLine("no scrollable viewer found");
}

var frame = window.CaptureRenderedFrame();
if (frame is not null)
    using (var fs = File.Create(outFile))
        frame.Save(fs);
Console.WriteLine($"saved {outFile} (page {pageIndex}, {width}x{height})");

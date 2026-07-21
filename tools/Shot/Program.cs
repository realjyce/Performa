using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Performa.Desktop;
using Performa.Desktop.ViewModels;

// Headless screenshot harness for the desktop shell. Renders a page to PNG so
// the UI can be verified visually, the way puppeteer shots verify the web work.

var outFile = args.Length > 0 ? args[0] : "shot.png";
var pageIndex = args.Length > 1 ? int.Parse(args[1]) : 0;
var width = args.Length > 2 ? int.Parse(args[2]) : 1200;
var height = args.Length > 3 ? int.Parse(args[3]) : 780;

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var vm = new MainViewModel();
vm.Selected = vm.NavItems.Concat(vm.UtilityItems).ElementAt(pageIndex);

// Optional: drive the assistant with a question so the shot shows a real answer.
if (args.Length > 4 && vm.Selected.Page is AssistantViewModel assistant)
    assistant.AskSuggestionCommand.Execute(args[4]);

// Optional: fire a dashboard quick action (e.g. "quick:standup") to test navigation.
if (args.Length > 4 && args[4].StartsWith("quick:") && vm.Selected.Page is DashboardViewModel dash)
    dash.QuickCommand.Execute(args[4]["quick:".Length..]);

var window = new MainWindow
{
    DataContext = vm,
    Width = width,
    Height = height,
};
window.Show();

// Pump the UI + let the async git load settle before capturing.
var deadline = DateTime.Now.AddSeconds(8);
while (DateTime.Now < deadline)
{
    Dispatcher.UIThread.RunJobs();
    Thread.Sleep(120);
}

var frame = window.CaptureRenderedFrame();
if (frame is not null)
    using (var fs = File.Create(outFile))
        frame.Save(fs);
Console.WriteLine($"saved {outFile} (page {pageIndex}, {width}x{height})");

using System.Text;
using Performa.Desktop.Infrastructure;
using Performa.Desktop.Services;

namespace Performa.Desktop.ViewModels;

/// <summary>
/// The embedded CLI. Same engine as the terminal `performa`, one click away in
/// the app. Type `standup`, `changelog`, `summary &lt;repo&gt;`, `loose`, `repos`.
/// </summary>
public sealed class ConsoleViewModel : ObservableObject
{
    private readonly PerformaEngine _engine;
    private readonly StringBuilder _log = new();

    public ConsoleViewModel(PerformaEngine engine)
    {
        _engine = engine;
        RunCommand = new RelayCommand(() => _ = RunAsync(Input));
        Append("performa console — type `help` for commands.");
    }

    private string _input = "";
    public string Input { get => _input; set => SetProperty(ref _input, value); }

    private string _output = "";
    public string Output { get => _output; private set => SetProperty(ref _output, value); }

    private bool _busy;
    public bool Busy { get => _busy; set => SetProperty(ref _busy, value); }

    public RelayCommand RunCommand { get; }

    private void Append(string text)
    {
        _log.AppendLine(text);
        Output = _log.ToString().TrimEnd();
    }

    private async Task RunAsync(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0 || Busy) return;
        Append("");
        Append("› " + line);
        Input = "";

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts[0].Equals("performa", StringComparison.OrdinalIgnoreCase))
            parts = parts[1..];
        if (parts.Length == 0) return;

        var cmd = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1] : null;

        if (cmd is "clear" or "cls") { _log.Clear(); Output = ""; return; }
        if (cmd is "help" or "?")
        {
            Append("commands:");
            Append("  repos                list workspace repositories");
            Append("  standup [repo]       what you did, grouped");
            Append("  changelog [repo]     release notes since last tag");
            Append("  summary <repo> [ref] what changed and why");
            Append("  loose [repo]         unfinished work");
            Append("  clear                wipe the console");
            return;
        }

        Busy = true;
        var result = await Task.Run(() => Execute(cmd, arg, parts));
        Append(result);
        Busy = false;
    }

    private string Execute(string cmd, string? arg, string[] parts)
    {
        if (cmd == "repos")
        {
            var repos = _engine.DiscoverRepos();
            return repos.Count == 0
                ? "no repositories in workspace"
                : string.Join('\n', repos.Select(p => "  " + System.IO.Path.GetFileName(p)));
        }

        var path = ResolveRepo(arg);
        if (path is null)
            return arg is null
                ? "no repositories found in the workspace"
                : $"repo not found: {arg}  (try `repos`)";

        try
        {
            return cmd switch
            {
                "standup" => _engine.RenderStandup(path).TrimEnd(),
                "changelog" => _engine.RenderChangelog(path).TrimEnd(),
                "summary" => _engine.RenderSummary(path,
                    parts.Length > 2 ? parts[2] : _engine.CurrentBranch(path)).TrimEnd(),
                "loose" => RenderLoose(path),
                _ => $"unknown command: {cmd}  (try `help`)",
            };
        }
        catch (Exception e)
        {
            return "error: " + e.Message;
        }
    }

    private string RenderLoose(string path)
    {
        var f = _engine.BuildLooseEnds(path);
        var lines = new List<string>();
        if (f.Working.Total > 0) lines.Add($"  {f.Working.Total} uncommitted file(s)");
        foreach (var b in f.UnpushedBranches)
            lines.Add(b.Upstream is null ? $"  {b.Name}: no upstream" : $"  {b.Name}: {b.Ahead} ahead");
        foreach (var b in f.StaleBranches)
            lines.Add($"  stale: {b.Name}");
        if (f.TodoTotal > 0) lines.Add($"  {f.TodoTotal} TODO/FIXME marker(s)");
        return lines.Count == 0 ? "  all clear" : string.Join('\n', lines);
    }

    private string? ResolveRepo(string? name)
    {
        var repos = _engine.DiscoverRepos();
        if (name is null) return repos.FirstOrDefault();
        return repos.FirstOrDefault(p =>
            System.IO.Path.GetFileName(p).Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}

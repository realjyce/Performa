using System.CommandLine;
using System.Text;
using Recap.Enrich;
using Recap.Git;
using Recap.Prefs;
using Recap.Reports;

try { Console.OutputEncoding = Encoding.UTF8; }
catch (IOException) { }

var repoOption = new Option<string>("--repo")
{
    Description = "Path to the git repository (default: current directory)",
    DefaultValueFactory = _ => ".",
    Recursive = true,
};
var formatOption = new Option<string?>("--format")
{
    Description = "Override output format for this run: md, text, or pretty",
    Recursive = true,
};
var noPromptOption = new Option<bool>("--no-prompt")
{
    Description = "Skip the accept/edit/reject prompt (also skipped when piped)",
    Recursive = true,
};

var root = new RootCommand(
    "recap: turns your local git history into standups, changelogs, branch summaries, and loose-end reports. Run bare for the dashboard.");
root.Options.Add(repoOption);
root.Options.Add(formatOption);
root.Options.Add(noPromptOption);

root.SetAction(parseResult => WithRepo(parseResult, ctx =>
{
    var state = ctx.Store.LoadState();
    var since = state.LastStandup.TryGetValue(ctx.Repo.Git.RepoPath, out var last)
        ? last
        : DateTimeOffset.Now.Date.AddDays(-1);
    var standup = FactBuilders.BuildStandup(ctx.Repo, ctx.Prefs, since, DateTimeOffset.Now);
    var loose = FactBuilders.BuildLooseEnds(ctx.Repo);
    Console.Write(RenderDashboard(ctx, standup, loose, since));
}));

var initCommand = new Command("init", "Answer a few questions to set your output preferences.");
initCommand.SetAction(parseResult =>
{
    var store = new PrefsStore();
    var prefs = store.LoadPrefs();
    RunInit(prefs);
    store.SavePrefs(prefs);
    Console.Error.WriteLine("recap: preferences saved.");
    return 0;
});
root.Subcommands.Add(initCommand);

var sinceOption = new Option<string?>("--since")
{
    Description = "Start point: a date (2026-07-18), 'yesterday', or any git ref. Default: your last standup.",
};
var standupCommand = new Command("standup", "What you did since a given point, grouped sensibly.");
standupCommand.Options.Add(sinceOption);
standupCommand.SetAction(parseResult => WithRepo(parseResult, ctx =>
{
    var state = ctx.Store.LoadState();
    var now = DateTimeOffset.Now;
    var since = ResolveSince(parseResult.GetValue(sinceOption), ctx.Repo, state);
    var facts = FactBuilders.BuildStandup(ctx.Repo, ctx.Prefs, since, now);
    var action = Emit(ctx, e => e.RenderStandup(facts, ctx.Prefs));
    if (action != FeedbackAction.Reject)
    {
        state.LastStandup[ctx.Repo.Git.RepoPath] = now;
        ctx.Store.SaveState(state);
    }
}));
root.Subcommands.Add(standupCommand);

var fromOption = new Option<string?>("--from")
{
    Description = "Start ref/tag. Default: the most recent tag (or full history if none).",
};
var toOption = new Option<string>("--to")
{
    Description = "End ref. Default: HEAD.",
    DefaultValueFactory = _ => "HEAD",
};
var changelogCommand = new Command("changelog", "Release notes generated from commits.");
changelogCommand.Options.Add(fromOption);
changelogCommand.Options.Add(toOption);
changelogCommand.SetAction(parseResult => WithRepo(parseResult, ctx =>
{
    var facts = FactBuilders.BuildChangelog(
        ctx.Repo, parseResult.GetValue(fromOption), parseResult.GetValue(toOption)!);
    Emit(ctx, e => e.RenderChangelog(facts, ctx.Prefs));
}));
root.Subcommands.Add(changelogCommand);

var targetArgument = new Argument<string>("target")
{
    Description = "A branch name or an explicit range like v1.0..HEAD",
};
var summaryCommand = new Command("summary", "What changed and why, for a branch or range.");
summaryCommand.Arguments.Add(targetArgument);
summaryCommand.SetAction(parseResult => WithRepo(parseResult, ctx =>
{
    var facts = FactBuilders.BuildSummary(ctx.Repo, ctx.Prefs, parseResult.GetValue(targetArgument)!);
    Emit(ctx, e => e.RenderSummary(facts, ctx.Prefs));
}));
root.Subcommands.Add(summaryCommand);

var looseEndsCommand = new Command("loose-ends", "Stale branches, unpushed commits, uncommitted work, TODO/FIXME markers.");
looseEndsCommand.SetAction(parseResult => WithRepo(parseResult, ctx =>
{
    var facts = FactBuilders.BuildLooseEnds(ctx.Repo);
    var enricher = new DeterministicEnricher(ctx.Pretty);
    Console.Write(enricher.RenderLooseEnds(facts, ctx.Prefs));
}));
root.Subcommands.Add(looseEndsCommand);

return root.Parse(args).Invoke();

int WithRepo(ParseResult parseResult, Action<CommandContext> run)
{
    var repoPath = parseResult.GetValue(repoOption)!;
    var git = new GitRunner(repoPath);
    if (!Directory.Exists(git.RepoPath))
    {
        Console.Error.WriteLine($"recap: directory not found: {git.RepoPath}");
        return 2;
    }
    if (!git.IsRepository())
    {
        Console.Error.WriteLine($"recap: not a git repository: {git.RepoPath}");
        return 2;
    }

    var store = new PrefsStore();
    var prefs = store.LoadPrefs();
    if (!prefs.Initialised)
        Console.Error.WriteLine("recap: using defaults. Run `recap init` once to set preferences.");

    var format = parseResult.GetValue(formatOption)?.ToLowerInvariant();
    var autoPretty = Ansi.TerminalSupportsStyling();
    var pretty = format switch
    {
        "pretty" => true,
        "md" or "markdown" or "text" or "txt" => false,
        _ => autoPretty,
    };
    switch (format)
    {
        case "md" or "markdown": prefs.Format = OutputFormat.Markdown; break;
        case "text" or "txt": prefs.Format = OutputFormat.Text; break;
    }

    try
    {
        run(new CommandContext(new RepoQueries(git), prefs, store, pretty,
            parseResult.GetValue(noPromptOption)));
        return 0;
    }
    catch (GitException e)
    {
        Console.Error.WriteLine($"recap: {e.Message}");
        return 1;
    }
}

FeedbackAction Emit(CommandContext ctx, Func<IEnricher, string> render)
{
    var markdown = render(new DeterministicEnricher(pretty: false));
    var display = ctx.Pretty ? render(new DeterministicEnricher(pretty: true)) : markdown;
    Console.Write(display);

    var (text, action) = Feedback.Collect(markdown, ctx.Prefs, ctx.Store, ctx.NoPrompt);
    if (action == FeedbackAction.Edit)
    {
        Console.WriteLine();
        Console.Write(text);
    }
    return action;
}

string RenderDashboard(CommandContext ctx, StandupFacts standup, LooseEndsFacts loose, DateTimeOffset since)
{
    var sb = new StringBuilder();
    var branch = ctx.Repo.CurrentBranch();
    var p = ctx.Pretty;

    if (p) sb.AppendLine($"{Ansi.Bold}{Ansi.Cyan}recap{Ansi.Reset}{Ansi.Dim} · {standup.RepoName} · {branch}{Ansi.Reset}");
    else sb.AppendLine($"# recap · {standup.RepoName} · {branch}");
    if (p) sb.AppendLine($"{Ansi.Dim}{new string('─', 44)}{Ansi.Reset}");
    sb.AppendLine();

    Section("Today", $"since {since:ddd d MMM}");
    var recent = standup.Groups.SelectMany(g => g.Commits).Take(5).ToList();
    if (recent.Count == 0) Line("nothing committed yet");
    foreach (var c in recent)
        Line(Classification.CleanSubject(c.Subject));
    sb.AppendLine();

    Section("Loose ends", null);
    var any = false;
    if (loose.Working.Total > 0) { WarnLine($"{loose.Working.Total} uncommitted file(s)"); any = true; }
    foreach (var b in loose.UnpushedBranches.Take(3))
    {
        WarnLine(b.Upstream is null ? $"{b.Name}: no upstream" : $"{b.Name}: {b.Ahead} ahead");
        any = true;
    }
    if (loose.StaleBranches.Count > 0) { WarnLine($"{loose.StaleBranches.Count} stale branch(es)"); any = true; }
    if (loose.TodoTotal > 0) { WarnLine($"{loose.TodoTotal} TODO/FIXME marker(s)"); any = true; }
    if (!any) Line("none");
    sb.AppendLine();

    if (p) sb.AppendLine($"{Ansi.Dim}recap standup · changelog · summary <branch> · loose-ends{Ansi.Reset}");
    else sb.AppendLine("Commands: recap standup | changelog | summary <branch> | loose-ends");
    return sb.ToString();

    void Section(string title, string? note)
    {
        var suffix = note is null ? "" : p ? $" {Ansi.Dim}({note}){Ansi.Reset}" : $" ({note})";
        sb.AppendLine(p ? $"{Ansi.Bold}{title}{Ansi.Reset}{suffix}" : $"## {title}{suffix}");
    }

    void Line(string text) => sb.AppendLine(p ? $"  {Ansi.Cyan}•{Ansi.Reset} {text}" : $"- {text}");
    void WarnLine(string text) => sb.AppendLine(p ? $"  {Ansi.Yellow}!{Ansi.Reset} {text}" : $"- {text}");
}

DateTimeOffset ResolveSince(string? since, RepoQueries repo, StateFile state)
{
    if (since is null)
    {
        return state.LastStandup.TryGetValue(repo.Git.RepoPath, out var last)
            ? last
            : DateTimeOffset.Now.Date.AddDays(-1);
    }
    if (since.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
        return DateTimeOffset.Now.Date.AddDays(-1);
    if (DateTimeOffset.TryParse(since, out var parsed))
        return parsed;

    var refDate = repo.Git.TryRun("show", "-s", "--format=%aI", since)?.Trim();
    if (refDate is not null && DateTimeOffset.TryParse(refDate.Split('\n')[^1], out var fromRef))
        return fromRef;
    throw new GitException($"could not resolve --since '{since}' as a date or git ref");
}

void RunInit(Preferences prefs)
{
    Console.Error.WriteLine("recap setup. Enter to keep the [default].");
    prefs.Format = Ask("Output format", ("md", OutputFormat.Markdown), ("text", OutputFormat.Text));
    prefs.Verbosity = Ask("Verbosity", ("normal", Verbosity.Normal), ("terse", Verbosity.Terse), ("detailed", Verbosity.Detailed));
    prefs.Grouping = Ask("Group commits by", ("area", Grouping.Area), ("kind", Grouping.Kind), ("flat", Grouping.Flat));
    prefs.Tone = Ask("Tone", ("plain", Tone.Plain), ("friendly", Tone.Friendly));
    prefs.Initialised = true;
}

static T Ask<T>(string label, params (string Key, T Value)[] choices)
{
    var keys = string.Join("/", choices.Select((c, i) => i == 0 ? $"[{c.Key}]" : c.Key));
    Console.Error.Write($"{label} ({keys}): ");
    var answer = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";
    foreach (var (key, value) in choices)
        if (key.StartsWith(answer, StringComparison.OrdinalIgnoreCase) && answer.Length > 0)
            return value;
    return choices[0].Value;
}

internal sealed record CommandContext(
    RepoQueries Repo, Preferences Prefs, PrefsStore Store, bool Pretty, bool NoPrompt);
